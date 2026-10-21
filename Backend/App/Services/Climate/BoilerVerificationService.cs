using HomeAssistant.apps;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HomeAssistant.Services.Climate;

internal interface IBoilerVerificationService
{
    /// <summary>
    /// Arms a verification of a press. <paramref name="openValves"/> must be the valves that are open once the
    /// pass that pressed has moved its valves: a valve shut in that pass can show nothing (2026-10-20, the dining
    /// valve). Call it before reporting that pass's valve changes, so a valve that has just opened still shows
    /// how long it was shut. <paramref name="boilerOffSince"/> is when the boiler was last believed to go off
    /// (null if unknown): a radiator whose valve was open all that time was still cold, because the boiler was
    /// not feeding it.
    /// </summary>
    void NotifyBoilerToggled(bool boilerNowOn, List<(string RoomName, ICustomClimateControlEntity Valve)> openValves, DateTimeOffset? boilerOffSince);
    void NotifyValveStartedHeating(string roomName, ICustomClimateControlEntity valve);
    void NotifyValveStopped(string roomName);
    Task CheckAsync();
    bool? GetVerifiedBoilerState();

    /// <summary>
    /// Discards any pending verification and valve history. Called when boiler control is
    /// handed to the boiler thermostat, so a verification left over from fingerbot mode
    /// cannot resurface later and provoke a corrective press.
    /// </summary>
    void Reset();
}

/// <summary>
/// Infers from the TRVs' own sensors whether a fingerbot press reached the boiler. A failed verification makes
/// the app press again to "correct" the boiler, and a wrong one inverts the app's belief until someone notices
/// (2026-10-20: two false turn-on failures, then a false turn-off pass, and the boiler ran all night while the
/// app showed it off). So a failure needs strong evidence, and anything less clears the verification without one:
/// <list type="bullet">
/// <item>Turn-on: passes as soon as any watched radiator has risen <see cref="TurnOnTemperatureRiseThreshold"/>,
/// and fails only if none has after <see cref="TurnOnDeadlineMinutes"/>. TRV sensors are slow: on 2026-10-20 one
/// read +0.5 °C after 16 minutes and went on to rise 4.8 °C, another first rose 28 minutes after the press. A
/// watched valve the app shuts during the wait stops counting, and if none is left nothing is concluded.</item>
/// <item>Turn-off: fails only if a watched radiator, after settling, <b>heats up again</b>: rises
/// <see cref="TurnOffReheatThreshold"/> above its lowest reading and gets back to at least its reading when the
/// boiler was turned off. That is a TRV with hot water behind it regulating itself. Falling is not evidence of a
/// turn-off: valves are left open while the boiler is believed off, and an open TRV on a running boiler cycles
/// (Bedroom 3, 21.5 to 23 °C all night), so it falls between cycles. A shut valve's radiator cools whether
/// or not the boiler runs, so only open valves are watched at all.</item>
/// </list>
/// </summary>
internal class BoilerVerificationService : IBoilerVerificationService
{
    private readonly ILogger<BoilerVerificationService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _checkSemaphore = new(1, 1);

    internal const double TurnOnTemperatureRiseThreshold = 1.0;
    // Until 2026-10-21 the turn-on was judged once, after 15 minutes, which failed twice on 2026-10-20 with the
    // boiler running.
    internal const int TurnOnDeadlineMinutes = 60;
    internal const int TurnOffSettlingMinutes = 30;
    internal const int TurnOffVerificationMinutes = 60;
    internal const double TurnOffReheatThreshold = 1.0;
    // How long a radiator must have been cold (before a turn-on) or hot (before a turn-off) to be
    // worth watching.
    internal const int RadiatorSettledMinutes = 30;

    private BoilerToggleEvent? _pendingVerification;
    private readonly Dictionary<string, ValveHeatingRecord> _valveHeatingRecords = [];

    public BoilerVerificationService(
        ILogger<BoilerVerificationService> logger,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public void NotifyBoilerToggled(bool boilerNowOn, List<(string RoomName, ICustomClimateControlEntity Valve)> openValves, DateTimeOffset? boilerOffSince)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        if (boilerNowOn)
        {
            SetupTurnOnVerification(now, openValves, boilerOffSince);
        }
        else
        {
            SetupTurnOffVerification(now, openValves);
        }
    }

    private void SetupTurnOnVerification(DateTimeOffset now, List<(string RoomName, ICustomClimateControlEntity Valve)> activeValves, DateTimeOffset? boilerOffSince)
    {
        // Watch every open valve whose radiator has had no hot water for half an hour: either the
        // boiler has been off that long, or the valve itself was shut that long.
        bool boilerOffLongEnough = boilerOffSince.HasValue && (now - boilerOffSince.Value).TotalMinutes >= RadiatorSettledMinutes;
        List<MonitoredValve> qualifyingValves = [];

        foreach ((string roomName, ICustomClimateControlEntity valve) in activeValves)
        {
            double? currentTemp = valve.CurrentTemperature;
            if (!currentTemp.HasValue)
                continue;

            bool valveShutLongEnough = _valveHeatingRecords.TryGetValue(roomName, out ValveHeatingRecord? record)
                && record.HeatingStoppedAt.HasValue
                && (now - record.HeatingStoppedAt.Value).TotalMinutes >= RadiatorSettledMinutes;

            if (boilerOffLongEnough || valveShutLongEnough)
            {
                qualifyingValves.Add(new MonitoredValve
                {
                    RoomName = roomName,
                    Valve = valve,
                    BaselineTemp = currentTemp.Value,
                    PeakTemp = currentTemp.Value
                });
            }
        }

        if (qualifyingValves.Count == 0)
        {
            _pendingVerification = null;
            _logger.LogDebug("Boiler verification: skipping turn-on verification (no valves cold long enough or no temp readings)");
            return;
        }

        _pendingVerification = new BoilerToggleEvent
        {
            BoilerTurnedOn = true,
            ToggleTime = now,
            MonitoredValves = qualifyingValves,
            State = VerificationState.Waiting
        };

        _logger.LogInformation(
            "Boiler verification: monitoring {Count} valve(s) to verify boiler turned ON. Rooms: {Rooms}. Passes on a {Rise}°C rise; fails if none has risen that much in {Minutes} minutes",
            qualifyingValves.Count,
            string.Join(", ", qualifyingValves.Select(v => $"{v.RoomName} ({v.BaselineTemp:F1}°C)")),
            TurnOnTemperatureRiseThreshold, TurnOnDeadlineMinutes);
    }

    private void SetupTurnOffVerification(DateTimeOffset now, List<(string RoomName, ICustomClimateControlEntity Valve)> openValves)
    {
        // Watch the valves that were heating for at least 30 minutes (hot radiators) and are still open, so they
        // would heat again if the boiler were still running.
        List<MonitoredValve> qualifyingValves = [];

        foreach ((string roomName, ICustomClimateControlEntity valve) in openValves)
        {
            double? currentTemp = valve.CurrentTemperature;
            if (!currentTemp.HasValue)
                continue;

            if (_valveHeatingRecords.TryGetValue(roomName, out ValveHeatingRecord? record)
                && !record.HeatingStoppedAt.HasValue
                && (now - record.HeatingStartedAt).TotalMinutes >= RadiatorSettledMinutes)
            {
                qualifyingValves.Add(new MonitoredValve
                {
                    RoomName = roomName,
                    Valve = valve,
                    BaselineTemp = currentTemp.Value,
                    PeakTemp = currentTemp.Value
                });
            }
        }

        if (qualifyingValves.Count == 0)
        {
            _pendingVerification = null;
            _logger.LogDebug("Boiler verification: skipping turn-off verification (no valves hot long enough or no temp readings)");
            return;
        }

        _pendingVerification = new BoilerToggleEvent
        {
            BoilerTurnedOn = false,
            ToggleTime = now,
            MonitoredValves = qualifyingValves,
            State = VerificationState.Waiting
        };

        _logger.LogInformation(
            "Boiler verification: monitoring {Count} valve(s) to verify boiler turned OFF. Rooms: {Rooms}. Settling for {SettlingMinutes} min, then watching {VerificationMinutes} min for a radiator heating up again",
            qualifyingValves.Count,
            string.Join(", ", qualifyingValves.Select(v => $"{v.RoomName} ({v.BaselineTemp:F1}°C)")),
            TurnOffSettlingMinutes, TurnOffVerificationMinutes);
    }

    public void NotifyValveStartedHeating(string roomName, ICustomClimateControlEntity valve)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        if (!_valveHeatingRecords.TryGetValue(roomName, out ValveHeatingRecord? record))
        {
            _valveHeatingRecords[roomName] = new ValveHeatingRecord
            {
                HeatingStartedAt = now,
                HeatingStoppedAt = null
            };
        }
        else if (record.HeatingStoppedAt.HasValue)
        {
            record.HeatingStartedAt = now;
            record.HeatingStoppedAt = null;
        }
    }

    public void NotifyValveStopped(string roomName)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        if (_valveHeatingRecords.TryGetValue(roomName, out ValveHeatingRecord? record))
        {
            // Reported on every evaluation while the valve stays shut, so keep the time it first shut:
            // overwriting it each cycle meant a valve could never count as shut for half an hour.
            record.HeatingStoppedAt ??= now;
        }
        else
        {
            _valveHeatingRecords[roomName] = new ValveHeatingRecord
            {
                HeatingStartedAt = now,
                HeatingStoppedAt = now
            };
        }

        // A radiator the app has shut can no longer show the boiler running, so it stops counting towards a
        // turn-on. (During a turn-off every room is reported stopped, because the boiler is believed off; that
        // check watches whether its open valves heat up again, so it is not touched here.)
        if (_pendingVerification is { BoilerTurnedOn: true, State: not VerificationState.Failed } verification
            && verification.MonitoredValves.Exists(mv => mv.RoomName == roomName))
        {
            // Its last reading still counts: it may have risen since the last check.
            if (PassTurnOnIfAnyRose(verification))
            {
                return;
            }

            verification.MonitoredValves.RemoveAll(mv => mv.RoomName == roomName);
            if (verification.MonitoredValves.Count == 0)
            {
                _logger.LogInformation(
                    "Boiler verification: every watched valve was shut before any showed a rise, so the turn-on cannot be judged. Not correcting anything");
                _pendingVerification = null;
            }
            else
            {
                _logger.LogDebug("Boiler verification: {Room} was shut, so no longer counts towards the turn-on", roomName);
            }
        }
    }

    public async Task CheckAsync()
    {
        if (_pendingVerification == null)
            return;

        if (!await _checkSemaphore.WaitAsync(0))
            return;

        try
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            BoilerToggleEvent verification = _pendingVerification;
            double minutesSinceToggle = (now - verification.ToggleTime).TotalMinutes;

            if (verification.BoilerTurnedOn)
            {
                CheckTurnOnVerification(verification, minutesSinceToggle);
            }
            else
            {
                CheckTurnOffVerification(verification, minutesSinceToggle, now);
            }
        }
        finally
        {
            _checkSemaphore.Release();
        }
    }

    public void Reset()
    {
        if (_pendingVerification != null)
        {
            _logger.LogInformation("Boiler verification: discarding the pending verification of a boiler turn-{Direction}", _pendingVerification.BoilerTurnedOn ? "ON" : "OFF");
        }

        _pendingVerification = null;
        _valveHeatingRecords.Clear();
    }

    public bool? GetVerifiedBoilerState()
    {
        if (_pendingVerification == null || _pendingVerification.State != VerificationState.Failed)
            return null;

        bool failedToTurnOn = _pendingVerification.BoilerTurnedOn;
        _pendingVerification = null;

        // If we failed to turn ON, the boiler is actually OFF (and vice versa)
        return !failedToTurnOn;
    }

    private void CheckTurnOnVerification(BoilerToggleEvent verification, double minutesSinceToggle)
    {
        // Every check counts, not just the last: a radiator that warmed and was then shut has still shown the boiler on.
        if (PassTurnOnIfAnyRose(verification) || minutesSinceToggle < TurnOnDeadlineMinutes)
            return;

        List<string> valves = [.. verification.MonitoredValves
            .Where(mv => mv.Valve.CurrentTemperature.HasValue)
            .Select(mv => $"{mv.RoomName} (most +{mv.PeakTemp - mv.BaselineTemp:F1}°C)")];
        if (valves.Count == 0)
            return;

        _logger.LogWarning(
            "Boiler verification FAILED: no watched valve rose {Rise}°C in {Minutes:F0} minutes - boiler may not have turned ON. Valves: {Failed}",
            TurnOnTemperatureRiseThreshold, minutesSinceToggle, string.Join(", ", valves));

        verification.State = VerificationState.Failed;
    }

    /// <summary>
    /// Brings each watched valve's highest reading up to date and, if any has risen far enough, passes the turn-on
    /// verification and returns true.
    /// </summary>
    private bool PassTurnOnIfAnyRose(BoilerToggleEvent verification)
    {
        foreach (MonitoredValve mv in verification.MonitoredValves)
        {
            double? currentTemp = mv.Valve.CurrentTemperature;
            if (currentTemp.HasValue && currentTemp.Value > mv.PeakTemp)
            {
                mv.PeakTemp = currentTemp.Value;
            }
        }

        List<string> risen = [.. verification.MonitoredValves
            .Where(mv => mv.PeakTemp - mv.BaselineTemp >= TurnOnTemperatureRiseThreshold)
            .Select(mv => $"{mv.RoomName} (+{mv.PeakTemp - mv.BaselineTemp:F1}°C)")];
        if (risen.Count == 0)
            return false;

        _logger.LogInformation(
            "Boiler verification PASSED: {PassedCount}/{TotalCount} valve(s) show temperature rise after {Minutes:F0} minutes - boiler is ON. Passed: {Passed}",
            risen.Count, verification.MonitoredValves.Count, (_timeProvider.GetUtcNow() - verification.ToggleTime).TotalMinutes,
            string.Join(", ", risen));

        _pendingVerification = null;
        return true;
    }

    private void CheckTurnOffVerification(BoilerToggleEvent verification, double minutesSinceToggle, DateTimeOffset now)
    {
        // During settling the radiators may still be warm from the water already in them: track how hot each got.
        if (minutesSinceToggle <= TurnOffSettlingMinutes)
        {
            foreach (MonitoredValve mv in verification.MonitoredValves)
            {
                double? currentTemp = mv.Valve.CurrentTemperature;
                if (currentTemp.HasValue && currentTemp.Value > mv.PeakTemp)
                {
                    mv.PeakTemp = currentTemp.Value;
                }
            }
            return;
        }

        if (verification.State == VerificationState.Waiting)
        {
            verification.State = VerificationState.Measuring;
            verification.MeasuringStartedAt = now;

            _logger.LogDebug(
                "Boiler verification: settling complete. Peak temps: {Peaks}. Now watching for {Minutes} minutes for a radiator heating up again",
                string.Join(", ", verification.MonitoredValves.Select(v => $"{v.RoomName} ({v.PeakTemp:F1}°C)")),
                TurnOffVerificationMinutes);
        }

        // A radiator heating up again with its valve still open has hot water behind it: the boiler is running.
        // It must rise well above its low and get back to its reading at the turn-off, so a room warmed by
        // something else (the sun, a heater plug) does not count.
        List<string> reheated = [];
        foreach (MonitoredValve mv in verification.MonitoredValves)
        {
            double? currentTemp = mv.Valve.CurrentTemperature;
            if (!currentTemp.HasValue)
                continue;

            if (!mv.LowestSinceSettled.HasValue || currentTemp.Value < mv.LowestSinceSettled.Value)
            {
                mv.LowestSinceSettled = currentTemp.Value;
            }

            if (currentTemp.Value - mv.LowestSinceSettled.Value >= TurnOffReheatThreshold && currentTemp.Value >= mv.BaselineTemp)
            {
                reheated.Add($"{mv.RoomName} (down to {mv.LowestSinceSettled.Value:F1}°C, back up to {currentTemp.Value:F1}°C, was {mv.BaselineTemp:F1}°C at the turn-off)");
            }
            // One that never cooled at all: still at its highest since the turn-off once settling is over, and well above
            // where it was. Water left in a radiator would have peaked and started falling by now. On 2026-10-21 den
            // radiator 2 climbed 20.0 -> 23.5 °C through the whole settling period, so it had no low to come back up from
            // and the rule above would have waited for yet another 1 °C.
            else if (currentTemp.Value >= mv.PeakTemp && currentTemp.Value - mv.BaselineTemp >= TurnOffReheatThreshold)
            {
                reheated.Add($"{mv.RoomName} (still climbing: {mv.BaselineTemp:F1}°C at the turn-off, {currentTemp.Value:F1}°C now)");
            }
        }

        if (reheated.Count > 0)
        {
            _logger.LogWarning(
                "Boiler verification FAILED: {Count} radiator(s) with an open valve heated up again {Minutes:F0} minutes after the boiler was turned off - boiler may not have turned OFF. Valves: {Failed}",
                reheated.Count, minutesSinceToggle, string.Join(", ", reheated));

            verification.State = VerificationState.Failed;
            return;
        }

        double minutesSinceMeasuring = (now - verification.MeasuringStartedAt!.Value).TotalMinutes;
        if (minutesSinceMeasuring < TurnOffVerificationMinutes)
            return;

        // Nothing heated up again. A radiator that only cooled is consistent with the boiler off, and one that did
        // neither proves nothing either way; neither is a reason to press.
        _logger.LogInformation(
            "Boiler verification PASSED: no radiator heated up again in {Minutes:F0} minutes - boiler is OFF. Valves: {Valves}",
            minutesSinceToggle,
            string.Join(", ", verification.MonitoredValves.Select(mv => $"{mv.RoomName} ({mv.PeakTemp:F1}°C -> {mv.Valve.CurrentTemperature:F1}°C)")));

        _pendingVerification = null;
    }
}

internal class BoilerToggleEvent
{
    public required bool BoilerTurnedOn { get; init; }
    public required DateTimeOffset ToggleTime { get; init; }
    public required List<MonitoredValve> MonitoredValves { get; init; }
    public VerificationState State { get; set; }
    public DateTimeOffset? MeasuringStartedAt { get; set; }
}

internal class MonitoredValve
{
    public required string RoomName { get; init; }
    public required ICustomClimateControlEntity Valve { get; init; }
    public required double BaselineTemp { get; set; }
    // The highest reading since the press: for a turn-on, how far it has risen; for a turn-off, how hot it was.
    public required double PeakTemp { get; set; }
    // Turn-off only: the lowest reading since settling ended, from which a radiator heating up again is measured.
    public double? LowestSinceSettled { get; set; }
}

internal enum VerificationState
{
    Waiting,
    Measuring,
    Failed
}

internal class ValveHeatingRecord
{
    public DateTimeOffset HeatingStartedAt { get; set; }
    public DateTimeOffset? HeatingStoppedAt { get; set; }
}

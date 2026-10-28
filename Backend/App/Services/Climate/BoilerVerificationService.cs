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

    /// <summary>
    /// Discards the pending verification only, keeping the valve history. Called when the belief changes from a
    /// fingerbot movement rather than from a press the app has just confirmed: the pending check was judging the old
    /// belief, but which radiators have had hot water, and for how long, is still true, and the new belief's own check
    /// needs it. (Until 2026-10-23 that used <see cref="Reset"/>, which wiped the history, so a movement to "off" could
    /// never arm a turn-off check.)
    /// </summary>
    void DiscardPendingVerification();

    /// <summary>Whether a verification of a turn-on (or turn-off) is pending and watching this valve.</summary>
    bool IsWatching(string valveEntityId, bool boilerTurnedOn);

    /// <summary>
    /// Whether a check of a press is still running: the radiators have not yet shown what the boiler did. False once it has
    /// passed, once a failure has been taken (<see cref="GetVerifiedBoilerState"/>), and when nothing could be watched.
    /// </summary>
    bool IsPending { get; }
}

/// <summary>
/// Infers from the TRVs' own sensors whether a fingerbot press reached the boiler. A failed verification makes
/// the app press again to "correct" the boiler, and a wrong one inverts the app's belief until someone notices
/// (2026-10-20: two false turn-on failures, then a false turn-off pass, and the boiler ran all night while the
/// app showed it off). So a failure needs strong evidence, and anything less clears the verification without one:
/// <list type="bullet">
/// <item>Turn-on: passes as soon as any watched radiator has risen <see cref="TurnOnTemperatureRiseThreshold"/>,
/// and fails only if none has after <see cref="TurnOnDeadlineMinutes"/>. TRV sensors are slow: on 2026-10-20 one
/// read +0.5 °C after 16 minutes and went on to rise 4.8 °C, another first rose 28 minutes after the press. Every
/// open valve is watched, including any that opens later; a watched valve the app shuts stops counting, but the
/// check waits for others rather than giving up. A radiator already warm at the press (hot water within the last
/// half hour) is not held to the deadline: it fails the press only by cooling <see cref="WarmRadiatorCoolingCelsius"/>
/// below its highest reading, after <see cref="WarmRadiatorCoolingMinutes"/>, while that reading stayed well under
/// its own setpoint (owner, 2026-10-22).</item>
/// <item>Turn-off: fails only if a watched radiator, after settling, <b>heats up again</b>: rises
/// <see cref="TurnOffReheatThreshold"/> above its lowest reading and gets back to at least its reading when the
/// boiler was turned off. That is a TRV with hot water behind it regulating itself. Or if it <b>never stopped
/// climbing</b>: a degree past its highest reading by the end of settling. Falling is not evidence of a
/// turn-off: valves are left open while the boiler is believed off, and an open TRV on a running boiler cycles
/// (Bedroom 3, 21.5 to 23 °C all night), so it falls between cycles. A shut valve's radiator cools whether
/// or not the boiler runs, so only open valves are watched, but every open one is, however briefly it has had hot
/// water (since 2026-10-23). One that had it for under <see cref="RadiatorSettledMinutes"/> was still near room
/// temperature at the turn-off, so it fails the press only by climbing to a new high.</item>
/// <item>Neither passes a turn-on nor fails a turn-off on a radiator warming while a shower may be warming it (owner,
/// 2026-10-28; HOME-ASSISTANT.md gotcha 29): running hot water can send hot water round the heating with the boiler's
/// heating off. The check waits until the shower can no longer be the cause, and judges from what it sees after.</item>
/// </list>
/// </summary>
internal class BoilerVerificationService : IBoilerVerificationService
{
    private readonly ILogger<BoilerVerificationService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IShowerDetector? _showers;
    private readonly SemaphoreSlim _checkSemaphore = new(1, 1);

    internal const double TurnOnTemperatureRiseThreshold = 1.0;
    // Until 2026-10-21 the turn-on was judged once, after 15 minutes, which failed twice on 2026-10-20 with the
    // boiler running.
    internal const int TurnOnDeadlineMinutes = 60;
    // Turn-on, a radiator already warm at the press: it fails the press by cooling this much below its highest reading,
    // once watched this long, while that highest reading was this far under its own setpoint (owner, 2026-10-22).
    internal const double WarmRadiatorCoolingCelsius = 1.0;
    internal const int WarmRadiatorCoolingMinutes = 30;
    internal const double WarmRadiatorSetpointMarginCelsius = 1.0;
    internal const int TurnOffSettlingMinutes = 30;
    internal const int TurnOffVerificationMinutes = 60;
    internal const double TurnOffReheatThreshold = 1.0;
    // How long a radiator must have been cold (before a turn-on) to count as cold, or hot (before a turn-off) to be
    // judged by heating up again as well as by climbing.
    internal const int RadiatorSettledMinutes = 30;

    private BoilerToggleEvent? _pendingVerification;
    private readonly Dictionary<string, ValveHeatingRecord> _valveHeatingRecords = [];

    /// <param name="showers">Null takes no showers into account (tests that are not about them).</param>
    public BoilerVerificationService(
        ILogger<BoilerVerificationService> logger,
        TimeProvider timeProvider,
        IShowerDetector? showers = null)
    {
        _logger = logger;
        _timeProvider = timeProvider;
        _showers = showers;
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
        // Watch every open valve. Cold ones (no hot water for half an hour: the boiler off that long, or the valve shut
        // that long) must rise within the deadline. Warm ones may not rise much, so they fail the press only by cooling
        // (owner, 2026-10-22, after a 06:23 press with only warm den radiators open could not be judged at all).
        bool boilerOffLongEnough = boilerOffSince.HasValue && (now - boilerOffSince.Value).TotalMinutes >= RadiatorSettledMinutes;
        List<MonitoredValve> watched = [];

        foreach ((string roomName, ICustomClimateControlEntity valve) in activeValves)
        {
            if (valve.CurrentTemperature is not double currentTemp)
                continue;

            bool valveShutLongEnough = _valveHeatingRecords.TryGetValue(roomName, out ValveHeatingRecord? record)
                && record.HeatingStoppedAt.HasValue
                && (now - record.HeatingStoppedAt.Value).TotalMinutes >= RadiatorSettledMinutes;

            watched.Add(NewTurnOnWatch(roomName, valve, currentTemp, warm: !(boilerOffLongEnough || valveShutLongEnough), now));
        }

        // Armed even with nothing to watch yet: a radiator whose valve opens later is watched from then
        // (NotifyValveStartedHeating). Until 2026-10-22 it was not armed at all without a cold one, and gave up once
        // the valves it had were shut, so the 02:42 press that never reached the boiler went unnoticed for three hours
        // while Bedroom 3's open valve cooled.
        _pendingVerification = new BoilerToggleEvent
        {
            BoilerTurnedOn = true,
            ToggleTime = now,
            MonitoredValves = watched,
            State = VerificationState.Waiting,
            RadiatorsWereColdAtToggle = boilerOffLongEnough,
            OpenSinceToggle = [.. activeValves.Select(v => v.Valve.EntityId)]
        };

        _logger.LogInformation(
            "Boiler verification: monitoring {Count} valve(s) to verify boiler turned ON. Rooms: {Rooms}. Passes on a {Rise}°C rise; fails once a cold one has been watched {Minutes} minutes without, or a warm one has cooled {Drop}°C. A valve opening later is watched too",
            watched.Count,
            watched.Count == 0 ? "none yet" : string.Join(", ", watched.Select(Describe)),
            TurnOnTemperatureRiseThreshold, TurnOnDeadlineMinutes, WarmRadiatorCoolingCelsius);
    }

    private static MonitoredValve NewTurnOnWatch(string roomName, ICustomClimateControlEntity valve, double currentTemp, bool warm, DateTimeOffset now) => new()
    {
        RoomName = roomName,
        Valve = valve,
        BaselineTemp = currentTemp,
        PeakTemp = currentTemp,
        WatchedSince = now,
        Warm = warm
    };

    private static string Describe(MonitoredValve mv) => $"{mv.RoomName} ({mv.BaselineTemp:F1}°C, {(mv.Warm ? "warm" : "cold")})";

    /// <summary>
    /// During a turn-on, starts watching a valve that has just opened. Its radiator is cold if the boiler had been
    /// off half an hour at the press and this valve has not been open since, or if the valve itself was shut half an
    /// hour; otherwise it is warm.
    /// </summary>
    private void WatchValveOpenedDuringTurnOn(string roomName, ICustomClimateControlEntity valve, ValveHeatingRecord? recordBeforeOpening, DateTimeOffset now)
    {
        if (_pendingVerification is not { BoilerTurnedOn: true, State: not VerificationState.Failed } verification)
            return;
        if (verification.MonitoredValves.Exists(mv => mv.Valve.EntityId == valve.EntityId))
            return;

        bool firstOpeningSincePress = verification.OpenSinceToggle.Add(valve.EntityId);
        bool shutLongEnough = recordBeforeOpening?.HeatingStoppedAt is { } stoppedAt && (now - stoppedAt).TotalMinutes >= RadiatorSettledMinutes;
        bool cold = (firstOpeningSincePress && verification.RadiatorsWereColdAtToggle) || shutLongEnough;
        if (valve.CurrentTemperature is not double currentTemp)
            return;

        MonitoredValve watch = NewTurnOnWatch(roomName, valve, currentTemp, warm: !cold, now);
        verification.MonitoredValves.Add(watch);
        _logger.LogInformation(
            "Boiler verification: {Room} ({Entity}) opened {Minutes:F0} minutes after the boiler was turned ON, so it is watched: {Watch}",
            roomName, valve.EntityId, (now - verification.ToggleTime).TotalMinutes, Describe(watch));
    }

    private void SetupTurnOffVerification(DateTimeOffset now, List<(string RoomName, ICustomClimateControlEntity Valve)> openValves)
    {
        // Watch every open valve: its radiator would heat again if the boiler were still running. Until 2026-10-23 only
        // those that had had hot water for half an hour were, so a turn-off within half an hour of a turn-on went
        // unchecked: at 14:37 that day Bedroom 3's had had 14 minutes, and the boiler ran on for over an hour with
        // nothing to catch it (HOME-ASSISTANT.md gotcha 27).
        List<MonitoredValve> qualifyingValves = [];

        foreach ((string roomName, ICustomClimateControlEntity valve) in openValves)
        {
            double? currentTemp = valve.CurrentTemperature;
            if (!currentTemp.HasValue)
                continue;

            bool settled = _valveHeatingRecords.TryGetValue(roomName, out ValveHeatingRecord? record)
                && !record.HeatingStoppedAt.HasValue
                && (now - record.HeatingStartedAt).TotalMinutes >= RadiatorSettledMinutes;

            qualifyingValves.Add(new MonitoredValve
            {
                RoomName = roomName,
                Valve = valve,
                BaselineTemp = currentTemp.Value,
                PeakTemp = currentTemp.Value,
                WatchedSince = now,
                RadiatorSettled = settled
            });
        }

        if (qualifyingValves.Count == 0)
        {
            _pendingVerification = null;
            // The app opens a valve for the check when none is open, so this means no valve has a reading at all.
            _logger.LogWarning(
                "Boiler verification: the turn-OFF cannot be checked, because no radiator valve is open with a reading ({Open} open), so nothing would show the boiler still running",
                openValves.Count);
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
            string.Join(", ", qualifyingValves.Select(v => $"{v.RoomName} ({v.BaselineTemp:F1}°C{(v.RadiatorSettled ? "" : $", hot water under {RadiatorSettledMinutes} min, judged only by climbing")})")),
            TurnOffSettlingMinutes, TurnOffVerificationMinutes);
    }

    public void NotifyValveStartedHeating(string roomName, ICustomClimateControlEntity valve)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        _valveHeatingRecords.TryGetValue(roomName, out ValveHeatingRecord? recordBeforeOpening);
        WatchValveOpenedDuringTurnOn(roomName, valve, recordBeforeOpening == null ? null : new ValveHeatingRecord
        {
            HeatingStartedAt = recordBeforeOpening.HeatingStartedAt,
            HeatingStoppedAt = recordBeforeOpening.HeatingStoppedAt
        }, now);

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
            // Its last reading still counts: it may have risen since the last check. One that has risen far enough has
            // shown the boiler on, so it stays watched, and the check at the end of this pass passes the press (unless a
            // shower may have warmed it).
            UpdateTurnOnPeaks(verification);
            if (verification.MonitoredValves.Exists(mv => mv.RoomName == roomName && HasRisen(mv, verification)))
            {
                return;
            }

            // Not given up when none is left (until 2026-10-22 it was): a cold radiator that opens later takes over.
            verification.MonitoredValves.RemoveAll(mv => mv.RoomName == roomName);
            _logger.LogInformation(
                "Boiler verification: {Room} was shut before showing a rise, so no longer counts towards the turn-on. Still watching: {Left}",
                roomName, verification.MonitoredValves.Count == 0 ? "none, until a cold radiator's valve opens" : string.Join(", ", verification.MonitoredValves.Select(mv => mv.RoomName)));
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
                await CheckTurnOnVerificationAsync(verification, minutesSinceToggle, now);
            }
            else
            {
                await CheckTurnOffVerificationAsync(verification, minutesSinceToggle, now);
            }
        }
        finally
        {
            _checkSemaphore.Release();
        }
    }

    public bool IsWatching(string valveEntityId, bool boilerTurnedOn) =>
        _pendingVerification is { } verification
        && verification.BoilerTurnedOn == boilerTurnedOn
        && verification.MonitoredValves.Exists(mv => string.Equals(mv.Valve.EntityId, valveEntityId, StringComparison.OrdinalIgnoreCase));

    public bool IsPending => _pendingVerification != null;

    public void DiscardPendingVerification()
    {
        if (_pendingVerification != null)
        {
            _logger.LogInformation("Boiler verification: discarding the pending verification of a boiler turn-{Direction}: the belief it was judging has changed", _pendingVerification.BoilerTurnedOn ? "ON" : "OFF");
        }

        _pendingVerification = null;
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

    private async Task CheckTurnOnVerificationAsync(BoilerToggleEvent verification, double minutesSinceToggle, DateTimeOffset now)
    {
        // Every check counts, not just the last: a radiator that warmed and was then shut has still shown the boiler on.
        if (await PassTurnOnIfAnyRoseAsync(verification, now))
            return;

        // A cold radiator has its deadline, from when it began to be watched, so one that opened later gets its full hour.
        // A shower cannot hide a boiler that never fired (it only adds heat), so it does not hold this back; but a radiator
        // that has risen, and is only waiting for a shower to be over before it passes the press, is no evidence.
        List<string> evidence = [.. verification.MonitoredValves
            .Where(mv => !mv.Warm && !HasRisen(mv, verification) && mv.Valve.CurrentTemperature.HasValue && (now - mv.WatchedSince).TotalMinutes >= TurnOnDeadlineMinutes)
            .Select(mv => $"{mv.RoomName} cold, most +{mv.PeakTemp - mv.BaselineTemp:F1}°C over {(now - mv.WatchedSince).TotalMinutes:F0} min")];

        // A warm one may already be near its hottest, so not rising proves nothing; cooling does. With its valve open
        // and hot water behind it a radiator cannot cool, unless its own TRV has reached its setpoint and is throttling
        // (Bedroom 3 swung 21.5-23 °C all night on 2026-10-20 that way), so it only counts while its highest reading
        // stayed well under the setpoint. The first half hour is left alone: a radiator whose hot water has stopped
        // still coasts up about 0.5 °C (seen 2026-10-21 22:40 and 2026-10-22 06:09) before it cools.
        evidence.AddRange(verification.MonitoredValves
            .Where(mv => mv.Warm
                && (now - mv.WatchedSince).TotalMinutes >= WarmRadiatorCoolingMinutes
                && mv.Valve.CurrentTemperature is double current
                && current <= mv.PeakTemp - WarmRadiatorCoolingCelsius
                && mv.Valve.TargetTemperature is double setpoint
                && mv.PeakTemp <= setpoint - WarmRadiatorSetpointMarginCelsius)
            .Select(mv => $"{mv.RoomName} warm, cooled from {mv.PeakTemp:F1}°C to {mv.Valve.CurrentTemperature:F1}°C with its valve open"));
        if (evidence.Count == 0)
            return;

        _logger.LogWarning(
            "Boiler verification FAILED: no watched valve rose {Rise}°C, {Minutes:F0} minutes since the press - boiler may not have turned ON. Evidence: {Evidence}. Watched: {Watched}",
            TurnOnTemperatureRiseThreshold, minutesSinceToggle, string.Join("; ", evidence),
            string.Join(", ", verification.MonitoredValves.Select(Describe)));

        verification.State = VerificationState.Failed;
    }

    private static void UpdateTurnOnPeaks(BoilerToggleEvent verification)
    {
        foreach (MonitoredValve mv in verification.MonitoredValves)
        {
            double? currentTemp = mv.Valve.CurrentTemperature;
            if (currentTemp.HasValue && currentTemp.Value > mv.PeakTemp)
            {
                mv.PeakTemp = currentTemp.Value;
            }
        }
    }

    /// <summary>
    /// Whether a watched radiator has shown the boiler on: risen far enough at any check, or, once a shower has been seen
    /// during this check, still that far up now. A radiator a shower warmed may have peaked during it with the boiler off.
    /// </summary>
    private static bool HasRisen(MonitoredValve mv, BoilerToggleEvent verification) =>
        (verification.ShowerSeen ? mv.Valve.CurrentTemperature ?? mv.BaselineTemp : mv.PeakTemp) - mv.BaselineTemp >= TurnOnTemperatureRiseThreshold;

    /// <summary>
    /// Brings each watched valve's highest reading up to date and, if any has risen far enough, passes the turn-on
    /// verification and returns true. Not while a shower may be warming the radiators.
    /// </summary>
    private async Task<bool> PassTurnOnIfAnyRoseAsync(BoilerToggleEvent verification, DateTimeOffset now)
    {
        UpdateTurnOnPeaks(verification);

        List<MonitoredValve> risen = [.. verification.MonitoredValves.Where(mv => HasRisen(mv, verification))];
        if (risen.Count == 0)
            return false;

        if (await IsShowerWarmingRadiatorsAsync(verification, now, "turn-ON"))
            return false;

        // A shower seen earlier in this check may have gone on the deadline's hour; a radiator still that far up once it
        // is over has hot water behind it.
        risen = [.. verification.MonitoredValves.Where(mv => HasRisen(mv, verification))];
        if (risen.Count == 0)
            return false;

        _logger.LogInformation(
            "Boiler verification PASSED: {PassedCount}/{TotalCount} valve(s) show temperature rise after {Minutes:F0} minutes - boiler is ON. Passed: {Passed}",
            risen.Count, verification.MonitoredValves.Count, (now - verification.ToggleTime).TotalMinutes,
            string.Join(", ", risen.Select(mv => $"{mv.RoomName} (+{(verification.ShowerSeen ? mv.Valve.CurrentTemperature ?? mv.BaselineTemp : mv.PeakTemp) - mv.BaselineTemp:F1}°C)")));

        _pendingVerification = null;
        return true;
    }

    /// <summary>
    /// Whether a shower may be what is warming the radiators now (HOME-ASSISTANT.md gotcha 29), so a radiator warming
    /// proves nothing about the boiler. Asked only when a radiator warming would decide the check, and remembered for the
    /// rest of the check, so the bathroom's history is read once per shower.
    /// </summary>
    private async Task<bool> IsShowerWarmingRadiatorsAsync(BoilerToggleEvent verification, DateTimeOffset now, string direction)
    {
        if (verification.ShowerWindowEndsAt is { } endsAt && now < endsAt)
            return true;
        if (_showers == null)
            return false;

        DateTimeOffset? showerStarted = await _showers.ShowerWarmingRadiatorsAsync(now);
        if (showerStarted is not { } started)
            return false;

        verification.ShowerWindowEndsAt = started + ShowerDetector.WarmsRadiatorsFor;
        verification.ShowerSeen = true;
        _logger.LogInformation(
            "Boiler verification of the turn-{Direction}: a radiator warmed, but a shower began at {Started:HH:mm} (the bathroom's humidity rose), and a shower can warm the radiators with the boiler's heating off (HOME-ASSISTANT.md gotcha 29). Not judging it until {Ends:HH:mm}, and then only from what the radiators do after",
            direction, started.ToLocalTime(), verification.ShowerWindowEndsAt.Value.ToLocalTime());
        return true;
    }

    private async Task CheckTurnOffVerificationAsync(BoilerToggleEvent verification, double minutesSinceToggle, DateTimeOffset now)
    {
        // During settling the radiators may still be warm from the water already in them: track how hot each got.
        if (minutesSinceToggle <= TurnOffSettlingMinutes)
        {
            RecordTurnOffPeaks(verification);
            return;
        }

        if (verification.State == VerificationState.Waiting)
        {
            verification.State = VerificationState.Measuring;
            verification.MeasuringStartedAt = now;

            // The first reading after settling counts as part of it. Readings only reach the check at a pass, so one that
            // arrived late in the settling, with no pass between it and the end, would otherwise look like a new high: on
            // 2026-10-27 the dining TRV's 21.5 °C came at 07:06, settling ended at 07:09, and the check failed at 07:09 on
            // "still climbing" with the boiler off (HOME-ASSISTANT.md gotcha 30), and a corrective press turned it back on.
            RecordTurnOffPeaks(verification);

            _logger.LogDebug(
                "Boiler verification: settling complete. Peak temps: {Peaks}. Now watching for {Minutes} minutes for a radiator heating up again",
                string.Join(", ", verification.MonitoredValves.Select(v => $"{v.RoomName} ({v.PeakTemp:F1}°C)")),
                TurnOffVerificationMinutes);
        }

        // While a shower may be warming the radiators, they show nothing about the boiler: it is more settling, and the
        // watch starts again once the shower can no longer be the cause.
        if (verification.ShowerWindowEndsAt is { } showerEnds && now < showerEnds)
        {
            ResettleAfterShower(verification);
            return;
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

            // Only for a radiator that had had hot water half an hour. One that had less was near room temperature at the
            // turn-off, so once it has cooled back to the room, a degree of warmth from anything else (a heater plug, the
            // sun) would take it past its turn-off reading. It is judged by the rule below alone.
            if (mv.RadiatorSettled && currentTemp.Value - mv.LowestSinceSettled.Value >= TurnOffReheatThreshold && currentTemp.Value >= mv.BaselineTemp)
            {
                reheated.Add($"{mv.RoomName} (down to {mv.LowestSinceSettled.Value:F1}°C, back up to {currentTemp.Value:F1}°C, was {mv.BaselineTemp:F1}°C at the turn-off)");
            }
            // One that never cooled at all: once settling is over it climbs a degree past anything it read up to then. On
            // 2026-10-21 den radiator 2 climbed 20.0 -> 23.5 °C through the whole settling period, so it had no low to come
            // back up from and the rule above would have waited for yet another 1 °C. This is also what catches a turn-off
            // soon after a turn-on (2026-10-23, Bedroom 3 after 14 minutes of hot water): if the boiler runs on, its
            // radiator goes on climbing; if not, the water left in it peaks and stops. A degree, not merely a new high
            // (owner, 2026-10-28): a radiator that had hot water for only minutes before the turn-off keeps warming its TRV
            // for half an hour, each 0.5 °C step slower (the dining TRV on 2026-10-27: 4, 4, 4, 6 and 10 minutes), so one
            // more step after settling is no proof. On a running boiler the next degree came in 10-20 minutes.
            else if (currentTemp.Value >= mv.PeakTemp + TurnOffReheatThreshold)
            {
                reheated.Add($"{mv.RoomName} (still climbing: {mv.BaselineTemp:F1}°C at the turn-off, {mv.PeakTemp:F1}°C by the end of settling, {currentTemp.Value:F1}°C now)");
            }
        }

        if (reheated.Count > 0 && await IsShowerWarmingRadiatorsAsync(verification, now, "OFF"))
        {
            ResettleAfterShower(verification);
            return;
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

    /// <summary>Raises each watched radiator's highest reading to what it reads now.</summary>
    private static void RecordTurnOffPeaks(BoilerToggleEvent verification)
    {
        foreach (MonitoredValve mv in verification.MonitoredValves)
        {
            double? currentTemp = mv.Valve.CurrentTemperature;
            if (currentTemp.HasValue && currentTemp.Value > mv.PeakTemp)
            {
                mv.PeakTemp = currentTemp.Value;
            }
        }
    }

    /// <summary>
    /// During a shower that may be warming the radiators: what they read counts as more settling, so a radiator must climb
    /// or heat up again from where it got to by the end of it, and the hour's watch starts again then.
    /// </summary>
    private static void ResettleAfterShower(BoilerToggleEvent verification)
    {
        RecordTurnOffPeaks(verification);
        foreach (MonitoredValve mv in verification.MonitoredValves)
        {
            mv.LowestSinceSettled = null;
        }

        verification.MeasuringStartedAt = verification.ShowerWindowEndsAt;
    }
}

internal class BoilerToggleEvent
{
    public required bool BoilerTurnedOn { get; init; }
    public required DateTimeOffset ToggleTime { get; init; }
    public required List<MonitoredValve> MonitoredValves { get; init; }
    public VerificationState State { get; set; }
    public DateTimeOffset? MeasuringStartedAt { get; set; }
    // Turn-on only: whether every radiator was cold at the press (the boiler had been off half an hour), and which
    // valves have been open since it, so a valve opening for the first time since the press is known to be cold.
    public bool RadiatorsWereColdAtToggle { get; init; }
    public HashSet<string> OpenSinceToggle { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    // Until when a shower seen during this check may still be warming the radiators; null if none has been seen.
    public DateTimeOffset? ShowerWindowEndsAt { get; set; }
    // A shower has been seen during this check, so a turn-on is passed only by a radiator still warm, not one that peaked.
    public bool ShowerSeen { get; set; }
}

internal class MonitoredValve
{
    public required string RoomName { get; init; }
    public required ICustomClimateControlEntity Valve { get; init; }
    public required double BaselineTemp { get; set; }
    // The highest reading since the press: for a turn-on, how far it has risen; for a turn-off, how hot it got by the end
    // of settling (the first check after it included) or of a shower that may have warmed it.
    public required double PeakTemp { get; set; }
    // When it began to be watched: the press, or for a turn-on a valve that opened later. A turn-on fails once one has
    // been watched TurnOnDeadlineMinutes with no rise.
    public required DateTimeOffset WatchedSince { get; init; }
    // Turn-on only: its radiator had hot water within the last half hour when it began to be watched, so it is judged
    // by cooling rather than by the deadline.
    public bool Warm { get; init; }
    // Turn-off only: the lowest reading since settling ended, from which a radiator heating up again is measured.
    public double? LowestSinceSettled { get; set; }
    // Turn-off only: its radiator had had hot water for RadiatorSettledMinutes at the turn-off, so it was well above room
    // temperature, and heating up again past that reading means hot water. One that had less is judged only by climbing.
    public bool RadiatorSettled { get; init; }
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

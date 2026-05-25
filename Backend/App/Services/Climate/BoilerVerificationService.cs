using HomeAssistant.apps;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HomeAssistant.Services.Climate;

internal interface IBoilerVerificationService
{
    void NotifyBoilerToggled(bool boilerNowOn, List<(string RoomName, ICustomClimateControlEntity Valve)> activeValves);
    void NotifyValveStartedHeating(string roomName, ICustomClimateControlEntity valve);
    void NotifyValveStopped(string roomName);
    Task CheckAsync();
    bool? GetVerifiedBoilerState();
}

internal class BoilerVerificationService : IBoilerVerificationService
{
    private readonly ILogger<BoilerVerificationService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _checkSemaphore = new(1, 1);

    internal const double TurnOnTemperatureRiseThreshold = 1.0;
    internal const int TurnOnWaitMinutes = 15;
    internal const int TurnOffSettlingMinutes = 30;
    internal const int TurnOffVerificationMinutes = 60;

    private BoilerToggleEvent? _pendingVerification;
    private readonly Dictionary<string, ValveHeatingRecord> _valveHeatingRecords = [];

    public BoilerVerificationService(
        ILogger<BoilerVerificationService> logger,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public void NotifyBoilerToggled(bool boilerNowOn, List<(string RoomName, ICustomClimateControlEntity Valve)> activeValves)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        if (boilerNowOn)
        {
            SetupTurnOnVerification(now, activeValves);
        }
        else
        {
            SetupTurnOffVerification(now, activeValves);
        }
    }

    private void SetupTurnOnVerification(DateTimeOffset now, List<(string RoomName, ICustomClimateControlEntity Valve)> activeValves)
    {
        // Find all valves that were off for at least 30 minutes (cold radiators)
        List<MonitoredValve> qualifyingValves = [];

        foreach ((string roomName, ICustomClimateControlEntity valve) in activeValves)
        {
            double? currentTemp = valve.CurrentTemperature;
            if (!currentTemp.HasValue)
                continue;

            if (_valveHeatingRecords.TryGetValue(roomName, out ValveHeatingRecord? record)
                && record.HeatingStoppedAt.HasValue
                && (now - record.HeatingStoppedAt.Value).TotalMinutes >= 30)
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
            "Boiler verification: monitoring {Count} valve(s) to verify boiler turned ON. Rooms: {Rooms}. Will check in {Minutes} minutes",
            qualifyingValves.Count,
            string.Join(", ", qualifyingValves.Select(v => $"{v.RoomName} ({v.BaselineTemp:F1}°C)")),
            TurnOnWaitMinutes);
    }

    private void SetupTurnOffVerification(DateTimeOffset now, List<(string RoomName, ICustomClimateControlEntity Valve)> activeValves)
    {
        // Find all valves that were heating for at least 30 minutes (hot radiators)
        List<MonitoredValve> qualifyingValves = [];

        foreach ((string roomName, ICustomClimateControlEntity valve) in activeValves)
        {
            double? currentTemp = valve.CurrentTemperature;
            if (!currentTemp.HasValue)
                continue;

            if (_valveHeatingRecords.TryGetValue(roomName, out ValveHeatingRecord? record)
                && !record.HeatingStoppedAt.HasValue
                && (now - record.HeatingStartedAt).TotalMinutes >= 30)
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
            "Boiler verification: monitoring {Count} valve(s) to verify boiler turned OFF. Rooms: {Rooms}. Settling for {SettlingMinutes} min, then checking for {VerificationMinutes} min",
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
            record.HeatingStoppedAt = now;
        }
        else
        {
            _valveHeatingRecords[roomName] = new ValveHeatingRecord
            {
                HeatingStartedAt = now,
                HeatingStoppedAt = now
            };
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
        if (minutesSinceToggle < TurnOnWaitMinutes)
            return;

        List<string> passedValves = [];
        List<string> failedValves = [];

        foreach (MonitoredValve mv in verification.MonitoredValves)
        {
            double? currentTemp = mv.Valve.CurrentTemperature;
            if (!currentTemp.HasValue)
                continue;

            double tempRise = currentTemp.Value - mv.BaselineTemp;

            if (tempRise >= TurnOnTemperatureRiseThreshold)
            {
                passedValves.Add($"{mv.RoomName} (+{tempRise:F1}°C)");
            }
            else
            {
                failedValves.Add($"{mv.RoomName} (+{tempRise:F1}°C)");
            }
        }

        if (passedValves.Count > 0)
        {
            _logger.LogInformation(
                "Boiler verification PASSED: {PassedCount}/{TotalCount} valve(s) show temperature rise - boiler is ON. Passed: {Passed}",
                passedValves.Count, passedValves.Count + failedValves.Count,
                string.Join(", ", passedValves));

            _pendingVerification = null;
        }
        else if (failedValves.Count > 0)
        {
            _logger.LogWarning(
                "Boiler verification FAILED: 0/{TotalCount} valve(s) show expected temperature rise after {Minutes:F0} minutes - boiler may not have turned ON. Valves: {Failed}",
                failedValves.Count, minutesSinceToggle,
                string.Join(", ", failedValves));

            verification.State = VerificationState.Failed;
        }
    }

    private void CheckTurnOffVerification(BoilerToggleEvent verification, double minutesSinceToggle, DateTimeOffset now)
    {
        // During settling period, track the peak temperature for each valve
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

        // After settling, switch to measuring state
        if (verification.State == VerificationState.Waiting)
        {
            verification.State = VerificationState.Measuring;
            verification.MeasuringStartedAt = now;

            _logger.LogDebug(
                "Boiler verification: settling complete. Peak temps: {Peaks}. Now measuring for {Minutes} minutes",
                string.Join(", ", verification.MonitoredValves.Select(v => $"{v.RoomName} ({v.PeakTemp:F1}°C)")),
                TurnOffVerificationMinutes);
        }

        double minutesSinceMeasuring = (now - verification.MeasuringStartedAt!.Value).TotalMinutes;

        if (minutesSinceMeasuring < TurnOffVerificationMinutes)
            return;

        // Verification period complete - check if any valve dropped below its peak
        List<string> passedValves = [];
        List<string> failedValves = [];

        foreach (MonitoredValve mv in verification.MonitoredValves)
        {
            double? currentTemp = mv.Valve.CurrentTemperature;
            if (!currentTemp.HasValue)
                continue;

            if (currentTemp.Value < mv.PeakTemp)
            {
                passedValves.Add($"{mv.RoomName} ({mv.PeakTemp:F1}°C -> {currentTemp.Value:F1}°C)");
            }
            else
            {
                failedValves.Add($"{mv.RoomName} (peak {mv.PeakTemp:F1}°C, now {currentTemp.Value:F1}°C)");
            }
        }

        if (passedValves.Count > 0)
        {
            _logger.LogInformation(
                "Boiler verification PASSED: {PassedCount}/{TotalCount} valve(s) show temperature drop - boiler is OFF. Passed: {Passed}",
                passedValves.Count, passedValves.Count + failedValves.Count,
                string.Join(", ", passedValves));

            _pendingVerification = null;
        }
        else if (failedValves.Count > 0)
        {
            _logger.LogWarning(
                "Boiler verification FAILED: 0/{TotalCount} valve(s) show expected temperature drop after {Minutes:F0} minutes - boiler may not have turned OFF. Valves: {Failed}",
                failedValves.Count, minutesSinceToggle,
                string.Join(", ", failedValves));

            verification.State = VerificationState.Failed;
        }
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
    public required double PeakTemp { get; set; }
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

using HomeAssistant.Shared;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HomeAssistant.Services.Energy;

internal class EnergyHistoryPushService
{
    private readonly HistoryService _historyService;
    private readonly IDeviceSettingsPersistenceService _deviceSettings;
    private readonly IEnergyHistoryApiClient _apiClient;
    private readonly WebSynchronisationConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EnergyHistoryPushService> _logger;
    private readonly IGracefulShutdownService _shutdownService;

    public EnergyHistoryPushService(
        HistoryService historyService,
        IDeviceSettingsPersistenceService deviceSettings,
        IEnergyHistoryApiClient apiClient,
        WebSynchronisationConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<EnergyHistoryPushService> logger,
        IGracefulShutdownService shutdownService)
    {
        _historyService = historyService;
        _deviceSettings = deviceSettings;
        _apiClient = apiClient;
        _configuration = configuration;
        _timeProvider = timeProvider;
        _logger = logger;
        _shutdownService = shutdownService;
    }

    public void Initialize()
    {
        _ = RunHourlyPushAsync();
    }

    private async Task RunHourlyPushAsync()
    {
        CancellationToken shutdownToken = _shutdownService.ShutdownToken;

        // Wait for initial startup
        try { await Task.Delay(TimeSpan.FromSeconds(15), shutdownToken); }
        catch (TaskCanceledException) { return; }

        while (!shutdownToken.IsCancellationRequested)
        {
            try
            {
                // Calculate delay until next hour boundary + 2 minutes (to let sensors settle)
                DateTime localNow = _timeProvider.GetLocalNow().DateTime;
                DateTime nextHour = new DateTime(localNow.Year, localNow.Month, localNow.Day, localNow.Hour, 0, 0)
                    .AddHours(1)
                    .AddMinutes(2);
                TimeSpan delay = nextHour - localNow;

                if (delay > TimeSpan.Zero)
                {
                    try { await Task.Delay(delay, shutdownToken); }
                    catch (TaskCanceledException) { break; }
                }

                // Push the previous hour's data
                DateTime now = _timeProvider.GetLocalNow().DateTime;
                int previousHour = now.Hour == 0 ? 23 : now.Hour - 1;
                DateTime targetDate = now.Hour == 0 ? now.AddDays(-1) : now;
                string date = targetDate.ToString("yyyy-MM-dd");

                await PushHourAsync(date, previousHour);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in energy history push loop, will retry next hour");
                try { await Task.Delay(TimeSpan.FromMinutes(5), shutdownToken); }
                catch (TaskCanceledException) { break; }
            }
        }

        _logger.LogInformation("Energy history push service stopped due to shutdown");
    }

    private async Task PushHourAsync(string date, int hour)
    {
        if (string.IsNullOrEmpty(_configuration.HouseId))
        {
            _logger.LogWarning("Cannot push energy history - HouseId not configured");
            return;
        }

        DeviceSettingsDto settings = await _deviceSettings.GetSettingsAsync();
        RealtimePowerSettingsDto? power = settings.RealtimePower;

        if (power == null)
        {
            _logger.LogWarning("Cannot push energy history - RealtimePower settings not configured");
            return;
        }

        string? solarId = power.SolarPowerSensorId;
        string? batteryId = power.BatteryPowerSensorId;
        string? gridImportId = power.GridImportPowerSensorId;
        string? gridExportId = power.GridExportPowerSensorId;

        if (string.IsNullOrEmpty(solarId) || string.IsNullOrEmpty(batteryId) ||
            string.IsNullOrEmpty(gridImportId) || string.IsNullOrEmpty(gridExportId))
        {
            _logger.LogWarning("Cannot push energy history - not all power sensor IDs configured");
            return;
        }

        BatterySettingsDto? battery = settings.Battery;
        string? importRateId = battery?.ElectricityRateSensorId;
        string? exportRateId = battery?.ExportRateSensorId;

        TimeZoneInfo localTimeZone = _timeProvider.LocalTimeZone;
        DateOnly localDate = DateOnly.Parse(date);
        DateTime hourStart = localDate.ToDateTime(new TimeOnly(hour, 0));
        DateTime hourEnd = hourStart.AddHours(1);

        DateTime fromUtc = TimeZoneInfo.ConvertTimeToUtc(hourStart, localTimeZone);
        DateTime toUtc = TimeZoneInfo.ConvertTimeToUtc(hourEnd, localTimeZone);

        try
        {
            Task<IReadOnlyList<NumericHistoryEntry>> solarTask = _historyService.GetEntityNumericHistory(solarId, fromUtc, toUtc);
            Task<IReadOnlyList<NumericHistoryEntry>> batteryTask = _historyService.GetEntityNumericHistory(batteryId, fromUtc, toUtc);
            Task<IReadOnlyList<NumericHistoryEntry>> gridImportTask = _historyService.GetEntityNumericHistory(gridImportId, fromUtc, toUtc);
            Task<IReadOnlyList<NumericHistoryEntry>> gridExportTask = _historyService.GetEntityNumericHistory(gridExportId, fromUtc, toUtc);

            Task<IReadOnlyList<NumericHistoryEntry>>? importRateTask = !string.IsNullOrEmpty(importRateId)
                ? _historyService.GetEntityNumericHistory(importRateId, fromUtc, toUtc)
                : null;
            Task<IReadOnlyList<NumericHistoryEntry>>? exportRateTask = !string.IsNullOrEmpty(exportRateId)
                ? _historyService.GetEntityNumericHistory(exportRateId, fromUtc, toUtc)
                : null;

            List<Task> allTasks = [solarTask, batteryTask, gridImportTask, gridExportTask];
            if (importRateTask != null) allTasks.Add(importRateTask);
            if (exportRateTask != null) allTasks.Add(exportRateTask);
            await Task.WhenAll(allTasks);

            double solarKwh = IntegrateToKwh(await solarTask, fromUtc, toUtc);
            double batteryKwh = IntegrateToKwh(await batteryTask, fromUtc, toUtc);
            double gridImportKwh = IntegrateToKwh(await gridImportTask, fromUtc, toUtc);
            double gridExportKwh = IntegrateToKwh(await gridExportTask, fromUtc, toUtc);

            double gridKwh = gridImportKwh - gridExportKwh;
            double houseKwh = Math.Max(0, solarKwh + gridKwh - batteryKwh);

            // Calculate costs by integrating power × rate over the hour
            double importCostPence = 0;
            double exportCostPence = 0;
            if (importRateTask != null)
            {
                importCostPence = IntegrateCostPence(await gridImportTask, await importRateTask, fromUtc, toUtc);
            }
            if (exportRateTask != null)
            {
                exportCostPence = IntegrateCostPence(await gridExportTask, await exportRateTask, fromUtc, toUtc);
            }

            await _apiClient.PostEnergyHourAsync(_configuration.HouseId, date, hour, gridKwh, batteryKwh, solarKwh, houseKwh, importCostPence, exportCostPence);

            _logger.LogInformation(
                "Pushed energy hour {Hour} for {Date}: solar={Solar:F3} battery={Battery:F3} grid={Grid:F3} house={House:F3} kWh, importCost={ImportCost:F2}p exportCost={ExportCost:F2}p",
                hour, date, solarKwh, batteryKwh, gridKwh, houseKwh, importCostPence, exportCostPence);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pushing energy history for hour {Hour} on {Date}", hour, date);
        }
    }

    /// <summary>
    /// Integrates power readings (watts) over a time window to produce energy (kWh).
    /// Uses trapezoidal integration between state-change entries.
    /// </summary>
    internal static double IntegrateToKwh(IReadOnlyList<NumericHistoryEntry> entries, DateTime fromUtc, DateTime toUtc)
    {
        if (entries.Count == 0)
            return 0;

        double totalWattSeconds = 0;

        for (int i = 0; i < entries.Count; i++)
        {
            DateTime entryTime = entries[i].LastChanged.ToUniversalTime();
            double watts = entries[i].State;

            // Clamp entry time to the window
            DateTime segmentStart = entryTime < fromUtc ? fromUtc : entryTime;

            // Segment ends at next entry or end of window
            DateTime segmentEnd;
            if (i + 1 < entries.Count)
            {
                DateTime nextTime = entries[i + 1].LastChanged.ToUniversalTime();
                segmentEnd = nextTime > toUtc ? toUtc : nextTime;
            }
            else
            {
                segmentEnd = toUtc;
            }

            if (segmentEnd > segmentStart)
            {
                double seconds = (segmentEnd - segmentStart).TotalSeconds;
                totalWattSeconds += watts * seconds;
            }
        }

        // Convert watt-seconds to kWh
        return totalWattSeconds / 3_600_000.0;
    }

    /// <summary>
    /// Integrates power (watts) × rate (£/kWh) over a time window to produce cost in pence.
    /// Splits power segments at rate change boundaries so mid-hour price changes are handled correctly.
    /// </summary>
    internal static double IntegrateCostPence(
        IReadOnlyList<NumericHistoryEntry> powerEntries,
        IReadOnlyList<NumericHistoryEntry> rateEntries,
        DateTime fromUtc,
        DateTime toUtc)
    {
        if (powerEntries.Count == 0 || rateEntries.Count == 0)
            return 0;

        // Build sorted list of rate change timestamps and values within [from, to]
        List<(DateTime Time, double RatePoundsPerKwh)> rateTimeline = [];
        for (int i = 0; i < rateEntries.Count; i++)
        {
            DateTime t = rateEntries[i].LastChanged.ToUniversalTime();
            DateTime clamped = t < fromUtc ? fromUtc : t;
            if (clamped >= toUtc) continue;
            rateTimeline.Add((clamped, rateEntries[i].State));
        }

        if (rateTimeline.Count == 0)
            return 0;

        double totalCostPence = 0;

        for (int i = 0; i < powerEntries.Count; i++)
        {
            DateTime entryTime = powerEntries[i].LastChanged.ToUniversalTime();
            double watts = powerEntries[i].State;

            DateTime segmentStart = entryTime < fromUtc ? fromUtc : entryTime;
            DateTime segmentEnd;
            if (i + 1 < powerEntries.Count)
            {
                DateTime nextTime = powerEntries[i + 1].LastChanged.ToUniversalTime();
                segmentEnd = nextTime > toUtc ? toUtc : nextTime;
            }
            else
            {
                segmentEnd = toUtc;
            }

            if (segmentEnd <= segmentStart)
                continue;

            // Split this power segment at rate change boundaries
            DateTime subStart = segmentStart;
            while (subStart < segmentEnd)
            {
                // Find the rate applicable at subStart
                double ratePoundsPerKwh = rateTimeline[0].RatePoundsPerKwh;
                DateTime nextRateChange = segmentEnd;

                for (int r = 0; r < rateTimeline.Count; r++)
                {
                    if (rateTimeline[r].Time <= subStart)
                    {
                        ratePoundsPerKwh = rateTimeline[r].RatePoundsPerKwh;
                    }
                    else
                    {
                        // This is the next rate change after subStart
                        nextRateChange = rateTimeline[r].Time < segmentEnd ? rateTimeline[r].Time : segmentEnd;
                        break;
                    }
                }

                DateTime subEnd = nextRateChange < segmentEnd ? nextRateChange : segmentEnd;
                double seconds = (subEnd - subStart).TotalSeconds;

                // cost_pence = watts × (£/kWh × 100) × seconds / 3,600,000
                totalCostPence += watts * (ratePoundsPerKwh * 100.0) * seconds / 3_600_000.0;

                subStart = subEnd;
            }
        }

        return totalCostPence;
    }
}

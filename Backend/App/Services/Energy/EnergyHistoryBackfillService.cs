using HomeAssistant.Services;
using HomeAssistant.Shared;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace HomeAssistant.Services.Energy;

internal class EnergyHistoryBackfillService
{
    private readonly ISignalRConnectionService _signalRConnection;
    private readonly HistoryService _historyService;
    private readonly IDeviceSettingsPersistenceService _deviceSettings;
    private readonly IEnergyHistoryApiClient _apiClient;
    private readonly WebSynchronisationConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EnergyHistoryBackfillService> _logger;

    public EnergyHistoryBackfillService(
        ISignalRConnectionService signalRConnection,
        HistoryService historyService,
        IDeviceSettingsPersistenceService deviceSettings,
        IEnergyHistoryApiClient apiClient,
        WebSynchronisationConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<EnergyHistoryBackfillService> logger)
    {
        _signalRConnection = signalRConnection;
        _historyService = historyService;
        _deviceSettings = deviceSettings;
        _apiClient = apiClient;
        _configuration = configuration;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public void Initialize()
    {
        _signalRConnection.On<JsonElement>("backfill-energy-history", HandleBackfillRequestAsync);

        _ = BackfillOnStartupAsync();
    }

    private async Task BackfillOnStartupAsync()
    {
        try
        {
            // Wait for other services to initialise
            await Task.Delay(TimeSpan.FromSeconds(15));

            if (string.IsNullOrEmpty(_configuration.HouseId))
            {
                _logger.LogWarning("Cannot backfill energy history on startup - HouseId not configured");
                return;
            }

            DateTime now = _timeProvider.GetLocalNow().DateTime;
            string fromDate = now.AddDays(-6).ToString("yyyy-MM-dd");
            string toDate = now.ToString("yyyy-MM-dd");

            _logger.LogInformation("Backfilling energy history on startup for house {HouseId} from {FromDate} to {ToDate}",
                _configuration.HouseId, fromDate, toDate);

            await BackfillDateRangeAsync(_configuration.HouseId, fromDate, toDate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error backfilling energy history on startup");
        }
    }

    private async Task HandleBackfillRequestAsync(JsonElement payload)
    {
        try
        {
            string? houseId = payload.TryGetProperty("houseId", out JsonElement houseIdElement)
                ? houseIdElement.GetString()
                : null;

            // Preferred shape: explicit list of dates. Prevents the "from-first-
            // missing to last-missing" expansion that would otherwise pull in
            // intermediate dates the caller never asked for.
            List<string> dates = [];
            if (payload.TryGetProperty("dates", out JsonElement datesElement) &&
                datesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement element in datesElement.EnumerateArray())
                {
                    string? d = element.GetString();
                    if (!string.IsNullOrWhiteSpace(d)) dates.Add(d);
                }
            }

            // Backwards-compatible fallbacks
            string? fromDate = payload.TryGetProperty("fromDate", out JsonElement fromDateElement)
                ? fromDateElement.GetString()
                : null;
            string? toDate = payload.TryGetProperty("toDate", out JsonElement toDateElement)
                ? toDateElement.GetString()
                : null;
            string? singleDate = payload.TryGetProperty("date", out JsonElement dateElement)
                ? dateElement.GetString()
                : null;

            if (string.IsNullOrEmpty(houseId))
            {
                _logger.LogWarning("Received energy backfill request with missing houseId");
                return;
            }

            if (!string.Equals(houseId, _configuration.HouseId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Ignoring energy backfill request for house {HouseId} (we are {OurHouseId})",
                    houseId, _configuration.HouseId);
                return;
            }

            if (dates.Count > 0)
            {
                await BackfillSpecificDatesAsync(houseId, dates);
            }
            else if (!string.IsNullOrEmpty(fromDate) && !string.IsNullOrEmpty(toDate))
            {
                await BackfillDateRangeAsync(houseId, fromDate, toDate);
            }
            else if (!string.IsNullOrEmpty(singleDate))
            {
                await BackfillDateRangeAsync(houseId, singleDate, singleDate);
            }
            else
            {
                _logger.LogWarning("Received energy backfill request with no dates or date range");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing energy history backfill");
        }
    }

    private async Task BackfillSpecificDatesAsync(string houseId, List<string> dateStrings)
    {
        DeviceSettingsDto settings = await _deviceSettings.GetSettingsAsync();
        if (!TryGetSensorIds(settings, out string solarId, out string batteryId,
                out string gridImportId, out string gridExportId,
                out string? importRateId, out string? exportRateId))
        {
            return;
        }

        _logger.LogInformation("Backfilling energy history for house {HouseId} ({Count} specific dates)",
            houseId, dateStrings.Count);

        List<EnergyBackfillDateEntry> allEntries = [];
        foreach (string dateStr in dateStrings)
        {
            if (!DateOnly.TryParse(dateStr, out DateOnly date))
            {
                _logger.LogWarning("Skipping unparseable backfill date '{Date}'", dateStr);
                continue;
            }
            try
            {
                List<EnergyBackfillPoint>? points = await BuildDayPointsAsync(
                    date, solarId, batteryId, gridImportId, gridExportId,
                    importRateId, exportRateId);
                if (points != null && points.Count > 0)
                {
                    allEntries.Add(new EnergyBackfillDateEntry { Date = dateStr, Points = points });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error building backfill data for {Date}", date);
            }
        }

        if (allEntries.Count == 0)
        {
            _logger.LogInformation("No energy data to backfill for house {HouseId} ({Count} dates produced no points)",
                houseId, dateStrings.Count);
            return;
        }

        try
        {
            await _apiClient.BulkReplaceEnergyHistoryAsync(houseId, allEntries);
            int totalPoints = allEntries.Sum(e => e.Points.Count);
            _logger.LogInformation(
                "Successfully backfilled energy history for house {HouseId} ({DateCount} dates, {PointCount} hours)",
                houseId, allEntries.Count, totalPoints);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading backfill data for house {HouseId}", houseId);
        }
    }

    private bool TryGetSensorIds(DeviceSettingsDto settings,
        out string solarId, out string batteryId, out string gridImportId, out string gridExportId,
        out string? importRateId, out string? exportRateId)
    {
        solarId = string.Empty;
        batteryId = string.Empty;
        gridImportId = string.Empty;
        gridExportId = string.Empty;
        importRateId = null;
        exportRateId = null;

        RealtimePowerSettingsDto? power = settings.RealtimePower;
        if (power == null)
        {
            _logger.LogWarning("Cannot backfill energy history - RealtimePower settings not configured");
            return false;
        }

        if (string.IsNullOrEmpty(power.SolarPowerSensorId) ||
            string.IsNullOrEmpty(power.BatteryPowerSensorId) ||
            string.IsNullOrEmpty(power.GridImportPowerSensorId) ||
            string.IsNullOrEmpty(power.GridExportPowerSensorId))
        {
            _logger.LogWarning("Cannot backfill energy history - not all power sensor IDs configured");
            return false;
        }

        solarId = power.SolarPowerSensorId;
        batteryId = power.BatteryPowerSensorId;
        gridImportId = power.GridImportPowerSensorId;
        gridExportId = power.GridExportPowerSensorId;
        importRateId = settings.Battery?.ElectricityRateSensorId;
        exportRateId = settings.Battery?.ExportRateSensorId;
        return true;
    }

    private async Task BackfillDateRangeAsync(string houseId, string fromDateStr, string toDateStr)
    {
        if (!DateOnly.TryParse(fromDateStr, out DateOnly fromDate) ||
            !DateOnly.TryParse(toDateStr, out DateOnly toDate))
        {
            _logger.LogWarning("Cannot parse date range '{FromDate}' to '{ToDate}' for energy backfill",
                fromDateStr, toDateStr);
            return;
        }

        DeviceSettingsDto settings = await _deviceSettings.GetSettingsAsync();
        if (!TryGetSensorIds(settings, out string solarId, out string batteryId,
                out string gridImportId, out string gridExportId,
                out string? importRateId, out string? exportRateId))
        {
            return;
        }

        _logger.LogInformation("Backfilling energy history for house {HouseId} from {FromDate} to {ToDate}",
            houseId, fromDateStr, toDateStr);

        List<EnergyBackfillDateEntry> allEntries = [];

        for (DateOnly currentDate = fromDate; currentDate <= toDate; currentDate = currentDate.AddDays(1))
        {
            try
            {
                List<EnergyBackfillPoint>? points = await BuildDayPointsAsync(
                    currentDate, solarId, batteryId, gridImportId, gridExportId,
                    importRateId, exportRateId);

                if (points != null && points.Count > 0)
                {
                    allEntries.Add(new EnergyBackfillDateEntry
                    {
                        Date = currentDate.ToString("yyyy-MM-dd"),
                        Points = points
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error building backfill data for {Date}", currentDate);
            }
        }

        if (allEntries.Count == 0)
        {
            _logger.LogInformation("No energy data to backfill for house {HouseId} from {FromDate} to {ToDate}",
                houseId, fromDateStr, toDateStr);
            return;
        }

        try
        {
            await _apiClient.BulkReplaceEnergyHistoryAsync(houseId, allEntries);

            int totalPoints = allEntries.Sum(e => e.Points.Count);
            _logger.LogInformation(
                "Successfully backfilled energy history for house {HouseId} from {FromDate} to {ToDate} ({DateCount} dates, {PointCount} hours)",
                houseId, fromDateStr, toDateStr, allEntries.Count, totalPoints);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading backfill data for house {HouseId}", houseId);
        }
    }

    private async Task<List<EnergyBackfillPoint>?> BuildDayPointsAsync(
        DateOnly localDate,
        string solarId, string batteryId, string gridImportId, string gridExportId,
        string? importRateId, string? exportRateId)
    {
        TimeZoneInfo localTimeZone = _timeProvider.LocalTimeZone;
        DateTime localMidnight = localDate.ToDateTime(TimeOnly.MinValue);
        DateTime localNextMidnight = localMidnight.AddDays(1);

        DateTime fromUtc = TimeZoneInfo.ConvertTimeToUtc(localMidnight, localTimeZone);
        DateTime toUtc = TimeZoneInfo.ConvertTimeToUtc(localNextMidnight, localTimeZone);

        // Determine how many hours to process (for today, only completed hours)
        DateTime now = _timeProvider.GetLocalNow().DateTime;
        DateOnly today = DateOnly.FromDateTime(now);
        int maxHour = localDate == today ? now.Hour : 24;

        if (maxHour == 0)
        {
            return null;
        }

        // Fetch all sensor histories for the entire day in parallel
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

        IReadOnlyList<NumericHistoryEntry> solarHistory = await solarTask;
        IReadOnlyList<NumericHistoryEntry> batteryHistory = await batteryTask;
        IReadOnlyList<NumericHistoryEntry> gridImportHistory = await gridImportTask;
        IReadOnlyList<NumericHistoryEntry> gridExportHistory = await gridExportTask;
        IReadOnlyList<NumericHistoryEntry>? importRateHistory = importRateTask != null ? await importRateTask : null;
        IReadOnlyList<NumericHistoryEntry>? exportRateHistory = exportRateTask != null ? await exportRateTask : null;

        List<EnergyBackfillPoint> points = [];
        int skippedEmptyHours = 0;

        for (int hour = 0; hour < maxHour; hour++)
        {
            DateTime hourStartLocal = localMidnight.AddHours(hour);
            DateTime hourEndLocal = hourStartLocal.AddHours(1);
            DateTime hourFromUtc = TimeZoneInfo.ConvertTimeToUtc(hourStartLocal, localTimeZone);
            DateTime hourToUtc = TimeZoneInfo.ConvertTimeToUtc(hourEndLocal, localTimeZone);

            double solarKwh = EnergyHistoryPushService.IntegrateToKwh(
                FilterToWindow(solarHistory, hourFromUtc, hourToUtc), hourFromUtc, hourToUtc);
            double batteryKwh = EnergyHistoryPushService.IntegrateToKwh(
                FilterToWindow(batteryHistory, hourFromUtc, hourToUtc), hourFromUtc, hourToUtc);
            double gridImportKwh = EnergyHistoryPushService.IntegrateToKwh(
                FilterToWindow(gridImportHistory, hourFromUtc, hourToUtc), hourFromUtc, hourToUtc);
            double gridExportKwh = EnergyHistoryPushService.IntegrateToKwh(
                FilterToWindow(gridExportHistory, hourFromUtc, hourToUtc), hourFromUtc, hourToUtc);

            // Per-hour skip: if every sensor integrates to exactly zero, HA had
            // no data for this hour. Real homes always have some idle draw on
            // at least one sensor, so all-zero means "missing", not "quiet".
            // Skipping leaves any existing stored hour untouched rather than
            // overwriting it with bogus zeros.
            if (solarKwh == 0 && batteryKwh == 0 && gridImportKwh == 0 && gridExportKwh == 0)
            {
                skippedEmptyHours++;
                continue;
            }

            double gridKwh = gridImportKwh - gridExportKwh;
            double houseKwh = Math.Max(0, solarKwh + gridKwh - batteryKwh);

            double importCostPence = 0;
            double exportCostPence = 0;
            if (importRateHistory != null)
            {
                importCostPence = EnergyHistoryPushService.IntegrateCostPence(
                    FilterToWindow(gridImportHistory, hourFromUtc, hourToUtc),
                    FilterToWindow(importRateHistory, hourFromUtc, hourToUtc),
                    hourFromUtc, hourToUtc);
            }
            if (exportRateHistory != null)
            {
                exportCostPence = EnergyHistoryPushService.IntegrateCostPence(
                    FilterToWindow(gridExportHistory, hourFromUtc, hourToUtc),
                    FilterToWindow(exportRateHistory, hourFromUtc, hourToUtc),
                    hourFromUtc, hourToUtc);
            }

            points.Add(new EnergyBackfillPoint
            {
                Hour = hour,
                GridKwh = Math.Round(gridKwh, 4),
                BatteryKwh = Math.Round(batteryKwh, 4),
                SolarKwh = Math.Round(solarKwh, 4),
                HouseKwh = Math.Round(houseKwh, 4),
                ImportCostPence = Math.Round(importCostPence, 2),
                ExportCostPence = Math.Round(exportCostPence, 2)
            });
        }

        // After per-hour filtering, if nothing survived we have nothing to
        // write. Log at info — already-detailed warnings would have logged
        // per-hour skips if anyone cares to count them.
        if (points.Count == 0)
        {
            _logger.LogWarning(
                "No backfill data for {Date}: {Skipped}/{Max} hours had no sensor activity",
                localDate, skippedEmptyHours, maxHour);
            return null;
        }

        if (skippedEmptyHours > 0)
        {
            _logger.LogInformation(
                "Backfill {Date}: {Kept} hours, {Skipped} skipped (no sensor activity)",
                localDate, points.Count, skippedEmptyHours);
        }

        return points;
    }

    /// <summary>
    /// Filters history entries to include those relevant to a time window.
    /// Includes the last entry before the window (to know the value at window start)
    /// and all entries within the window.
    /// </summary>
    private static IReadOnlyList<NumericHistoryEntry> FilterToWindow(
        IReadOnlyList<NumericHistoryEntry> history, DateTime fromUtc, DateTime toUtc)
    {
        List<NumericHistoryEntry> result = [];

        // Find the last entry before or at window start (carry-forward value)
        NumericHistoryEntry? lastBefore = null;
        foreach (NumericHistoryEntry entry in history)
        {
            DateTime entryUtc = entry.LastChanged.ToUniversalTime();
            if (entryUtc <= fromUtc)
            {
                lastBefore = entry;
            }
        }

        if (lastBefore != null)
        {
            result.Add(new NumericHistoryEntry
            {
                LastChanged = fromUtc,
                State = lastBefore.State
            });
        }

        // Add entries within the window
        foreach (NumericHistoryEntry entry in history)
        {
            DateTime entryUtc = entry.LastChanged.ToUniversalTime();
            if (entryUtc > fromUtc && entryUtc < toUtc)
            {
                result.Add(entry);
            }
        }

        return result;
    }
}

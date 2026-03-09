using HomeAssistant.Services;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace HomeAssistant.Services.Energy;

internal class BatteryHistoryBackfillService
{
    private readonly ISignalRConnectionService _signalRConnection;
    private readonly HistoryService _historyService;
    private readonly IDeviceSettingsPersistenceService _deviceSettings;
    private readonly IBatteryRulesPersistenceService _rulesPersistence;
    private readonly IBatteryHistoryApiClient _historyApiClient;
    private readonly IBatteryPricingApiClient _pricingApiClient;
    private readonly WebSynchronisationConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BatteryHistoryBackfillService> _logger;

    public BatteryHistoryBackfillService(
        ISignalRConnectionService signalRConnection,
        HistoryService historyService,
        IDeviceSettingsPersistenceService deviceSettings,
        IBatteryRulesPersistenceService rulesPersistence,
        IBatteryHistoryApiClient historyApiClient,
        IBatteryPricingApiClient pricingApiClient,
        WebSynchronisationConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<BatteryHistoryBackfillService> logger)
    {
        _signalRConnection = signalRConnection;
        _historyService = historyService;
        _deviceSettings = deviceSettings;
        _rulesPersistence = rulesPersistence;
        _historyApiClient = historyApiClient;
        _pricingApiClient = pricingApiClient;
        _configuration = configuration;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public void Initialize()
    {
        _signalRConnection.On<JsonElement>("backfill-battery-history", HandleBackfillRequestAsync);

        // Push today's full battery history on startup
        _ = BackfillTodayOnStartupAsync();
    }

    private async Task BackfillTodayOnStartupAsync()
    {
        try
        {
            // Small delay to let other services initialise (device settings, SignalR, etc.)
            await Task.Delay(TimeSpan.FromSeconds(10));

            if (string.IsNullOrEmpty(_configuration.HouseId))
            {
                _logger.LogWarning("Cannot push startup battery history - HouseId not configured");
                return;
            }

            DateTime now = _timeProvider.GetLocalNow().DateTime;
            _logger.LogInformation("Pushing battery history on startup for house {HouseId} (last 7 days)",
                _configuration.HouseId);

            // Send unsimplified data for the last 7 days so the chart has full resolution.
            // The daily Azure Function handles simplification for older data.
            for (int daysAgo = 6; daysAgo >= 0; daysAgo--)
            {
                string date = now.AddDays(-daysAgo).ToString("yyyy-MM-dd");
                await BackfillDateWithRetryAsync(_configuration.HouseId, date, simplify: false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pushing today's battery history on startup");
        }
    }

    private async Task BackfillDateWithRetryAsync(string houseId, string date, bool simplify = true, int maxRetries = 3)
    {
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                await BackfillDateAsync(houseId, date, simplify);
                return;
            }
            catch (HttpRequestException ex)
            {
                if (attempt < maxRetries)
                {
                    int delaySeconds = (int)Math.Pow(2, attempt + 1); // 2s, 4s, 8s
                    _logger.LogWarning(ex, "Backfill for {Date} failed (attempt {Attempt}/{Max}), retrying in {Delay}s",
                        date, attempt + 1, maxRetries + 1, delaySeconds);
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                }
                else
                {
                    _logger.LogError(ex, "Backfill for {Date} failed after {Max} attempts, giving up",
                        date, maxRetries + 1);
                }
            }
        }
    }

    private async Task HandleBackfillRequestAsync(JsonElement payload)
    {
        try
        {
            string? houseId = payload.TryGetProperty("houseId", out JsonElement houseIdElement)
                ? houseIdElement.GetString()
                : null;
            string? date = payload.TryGetProperty("date", out JsonElement dateElement)
                ? dateElement.GetString()
                : null;

            if (string.IsNullOrEmpty(houseId) || string.IsNullOrEmpty(date))
            {
                _logger.LogWarning("Received backfill request with missing houseId or date");
                return;
            }

            // Only process requests for our house
            if (!string.Equals(houseId, _configuration.HouseId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Ignoring backfill request for house {HouseId} (we are {OurHouseId})",
                    houseId, _configuration.HouseId);
                return;
            }

            await BackfillDateAsync(houseId, date);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing battery history backfill");
        }
    }

    private async Task BackfillDateAsync(string houseId, string date, bool simplify = true)
    {
        _logger.LogInformation("Processing backfill for house {HouseId} on {Date}", houseId, date);

        Shared.DeviceSettingsDto settings = await _deviceSettings.GetSettingsAsync();

        if (!DateOnly.TryParse(date, out DateOnly localDate))
        {
            _logger.LogWarning("Cannot parse date '{Date}' for backfill", date);
            return;
        }

        TimeZoneInfo localTimeZone = _timeProvider.LocalTimeZone;
        DateTime localMidnight = localDate.ToDateTime(TimeOnly.MinValue);
        DateTime localNextMidnight = localMidnight.AddDays(1);

        DateTime fromUtc = TimeZoneInfo.ConvertTimeToUtc(localMidnight, localTimeZone);
        DateTime toUtc = TimeZoneInfo.ConvertTimeToUtc(localNextMidnight, localTimeZone);

        // Backfill pricing for the entire month (single HA query, then upload each day)
        // Returns the rate history so we can use it to protect battery points near price changes
        (IReadOnlyList<NumericHistoryEntry> importHistory, IReadOnlyList<NumericHistoryEntry> exportHistory) =
            await BackfillMonthPricingAsync(houseId, localDate, settings);

        // Collect protected minutes: price change times + zone rule boundaries
        HashSet<int> protectedMinutes = CollectProtectedMinutes(
            importHistory, exportHistory, fromUtc, toUtc, localMidnight, localTimeZone);

        _logger.LogDebug("Collected {Count} protected minutes for {Date}", protectedMinutes.Count, date);

        // Backfill battery history for just the requested date
        // (this triggers the "battery-history-replaced" notification which makes the frontend reload)
        await BackfillBatteryHistoryAsync(houseId, date, settings, fromUtc, toUtc, localMidnight, localTimeZone, protectedMinutes, simplify);
    }

    private async Task BackfillBatteryHistoryAsync(
        string houseId, string date, Shared.DeviceSettingsDto settings,
        DateTime fromUtc, DateTime toUtc,
        DateTime localMidnight, TimeZoneInfo localTimeZone, HashSet<int> protectedMinutes,
        bool simplify = true)
    {
        string? batteryEntityId = settings.Battery?.BatteryChargePercentSensorId;

        if (string.IsNullOrEmpty(batteryEntityId))
        {
            _logger.LogWarning("Cannot backfill battery history - BatteryChargePercentSensorId not configured");
            return;
        }

        _logger.LogDebug("Querying HA battery history for entity {EntityId} from {From} to {To}",
            batteryEntityId, fromUtc, toUtc);

        // Use text history so we can skip unparseable states ("unavailable", "unknown", etc.)
        IReadOnlyList<HistoryTextEntry> haTextHistory = await _historyService.GetEntityTextHistory(
            batteryEntityId, fromUtc, toUtc);

        List<NumericHistoryEntry> haHistory = haTextHistory
            .Where(e => double.TryParse(e.State, out _))
            .Select(e => new NumericHistoryEntry
            {
                LastChanged = e.LastChanged,
                State = double.Parse(e.State!)
            })
            .ToList();

        if (haHistory.Count == 0)
        {
            _logger.LogInformation("No valid HA battery history found for entity {EntityId} on {Date} ({Count} raw entries skipped)",
                batteryEntityId, date, haTextHistory.Count);
            return;
        }

        _logger.LogInformation("Retrieved {Count} valid HA battery history points on {Date} ({Count2} raw, {Skipped} skipped)",
            haHistory.Count, date, haTextHistory.Count, haTextHistory.Count - haHistory.Count);

        List<BackfillPoint> backfillPoints;

        if (simplify)
        {
            List<(DateTimeOffset Timestamp, double BatteryPercent)> simplified =
                BatteryHistorySimplifier.Simplify(
                    haHistory,
                    tolerancePercent: 1.0,
                    protectedMinutes: protectedMinutes.Count > 0 ? protectedMinutes : null,
                    localMidnight: localMidnight,
                    localTimeZone: localTimeZone);

            _logger.LogInformation("Simplified battery history {Original} → {Simplified} points for {Date}",
                haHistory.Count, simplified.Count, date);

            backfillPoints = simplified
                .Select(p => new BackfillPoint
                {
                    BatteryPercent = p.BatteryPercent,
                    RecordedAtUtc = p.Timestamp
                })
                .ToList();
        }
        else
        {
            _logger.LogInformation("Sending unsimplified battery history ({Count} points) for {Date}",
                haHistory.Count, date);

            backfillPoints = haHistory
                .Select(e => new BackfillPoint
                {
                    BatteryPercent = e.State,
                    RecordedAtUtc = e.LastChanged
                })
                .ToList();
        }

        await _historyApiClient.ReplaceBatteryHistoryAsync(houseId, date, backfillPoints);

        _logger.LogInformation("Successfully backfilled battery history for house {HouseId} on {Date} ({Count} points)",
            houseId, date, backfillPoints.Count);
    }

    private async Task<(IReadOnlyList<NumericHistoryEntry> ImportHistory, IReadOnlyList<NumericHistoryEntry> ExportHistory)> BackfillMonthPricingAsync(
        string houseId, DateOnly requestedDate, Shared.DeviceSettingsDto settings)
    {
        IReadOnlyList<NumericHistoryEntry> emptyList = [];
        string? importRateEntityId = settings.Battery?.ElectricityRateSensorId;
        string? exportRateEntityId = settings.Battery?.ExportRateSensorId;

        if (string.IsNullOrEmpty(importRateEntityId) && string.IsNullOrEmpty(exportRateEntityId))
        {
            _logger.LogDebug("No rate sensors configured, skipping pricing backfill");
            return (emptyList, emptyList);
        }

        TimeZoneInfo localTimeZone = _timeProvider.LocalTimeZone;

        // Determine month boundaries
        DateOnly monthStart = new(requestedDate.Year, requestedDate.Month, 1);
        int daysInMonth = DateTime.DaysInMonth(requestedDate.Year, requestedDate.Month);
        DateOnly monthEnd = monthStart.AddMonths(1);

        DateTime localMonthStart = monthStart.ToDateTime(TimeOnly.MinValue);
        DateTime localMonthEnd = monthEnd.ToDateTime(TimeOnly.MinValue);

        DateTime monthStartUtc = TimeZoneInfo.ConvertTimeToUtc(localMonthStart, localTimeZone);
        DateTime monthEndUtc = TimeZoneInfo.ConvertTimeToUtc(localMonthEnd, localTimeZone);

        // Query with 24-hour padding to capture rates at midnight boundaries
        DateTime paddedFromUtc = monthStartUtc.AddHours(-24);
        DateTime paddedToUtc = monthEndUtc.AddHours(24);

        _logger.LogInformation("Querying HA rate history for month {Month}/{Year} ({Days} days)",
            requestedDate.Month, requestedDate.Year, daysInMonth);

        IReadOnlyList<NumericHistoryEntry> importHistory = [];
        IReadOnlyList<NumericHistoryEntry> exportHistory = [];

        if (!string.IsNullOrEmpty(importRateEntityId))
        {
            importHistory = await _historyService.GetEntityNumericHistory(importRateEntityId, paddedFromUtc, paddedToUtc);
            _logger.LogInformation("Retrieved {Count} HA import rate history points for month {Month}/{Year}",
                importHistory.Count, requestedDate.Month, requestedDate.Year);
        }

        if (!string.IsNullOrEmpty(exportRateEntityId))
        {
            exportHistory = await _historyService.GetEntityNumericHistory(exportRateEntityId, paddedFromUtc, paddedToUtc);
            _logger.LogInformation("Retrieved {Count} HA export rate history points for month {Month}/{Year}",
                exportHistory.Count, requestedDate.Month, requestedDate.Year);
        }

        if (importHistory.Count == 0 && exportHistory.Count == 0)
        {
            _logger.LogInformation("No HA rate history found for month {Month}/{Year}", requestedDate.Month, requestedDate.Year);
            return (emptyList, emptyList);
        }

        // Process each day in the month (skip today — ElectricityRatePushService handles today
        // with Octopus API forecasts for future slots, which sensor history doesn't have)
        DateOnly today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        int uploadedDays = 0;
        for (int day = 1; day <= daysInMonth; day++)
        {
            DateOnly date = new(requestedDate.Year, requestedDate.Month, day);
            if (date == today)
                continue;

            string dateStr = date.ToString("yyyy-MM-dd");

            DateTime dayLocalMidnight = date.ToDateTime(TimeOnly.MinValue);
            DateTime dayFromUtc = TimeZoneInfo.ConvertTimeToUtc(dayLocalMidnight, localTimeZone);
            DateTime dayToUtc = TimeZoneInfo.ConvertTimeToUtc(dayLocalMidnight.AddDays(1), localTimeZone);

            List<PricingSlot> slots = BuildPricingSlotsFromHistory(
                importHistory, exportHistory, dayLocalMidnight, dayFromUtc, dayToUtc, localTimeZone);

            if (slots.Count == 0)
            {
                _logger.LogDebug("No pricing slots for {Date}, skipping", dateStr);
                continue;
            }

            await _pricingApiClient.PostPricingAsync(houseId, dateStr, slots);
            uploadedDays++;
        }

        _logger.LogInformation("Successfully backfilled month pricing for house {HouseId} for {Month}/{Year} ({Days} days uploaded)",
            houseId, requestedDate.Month, requestedDate.Year, uploadedDays);

        return (importHistory, exportHistory);
    }

    /// <summary>
    /// Converts raw HA sensor history entries into PricingSlots for a single day.
    /// Uses padded history (before/after the day) so the rate at midnight boundaries is known.
    /// Each entry's new rate is captured directly at its change-time minute to avoid
    /// truncation errors from re-looking up rates at reconstructed timestamps.
    /// </summary>
    private static List<PricingSlot> BuildPricingSlotsFromHistory(
        IReadOnlyList<NumericHistoryEntry> importHistory,
        IReadOnlyList<NumericHistoryEntry> exportHistory,
        DateTime localMidnight,
        DateTime fromUtc,
        DateTime toUtc,
        TimeZoneInfo localTimeZone)
    {
        // Sort both histories by timestamp to guarantee chronological order
        List<NumericHistoryEntry> sortedImport = importHistory.OrderBy(e => e.LastChanged).ToList();
        List<NumericHistoryEntry> sortedExport = exportHistory.OrderBy(e => e.LastChanged).ToList();

        // Build a map of minute → (importRate, exportRate) directly from entries.
        // Each entry records its NEW rate at the truncated minute, avoiding the need to
        // re-look up rates at reconstructed timestamps (which can be off by up to 59s).
        SortedDictionary<int, (double? ImportRate, double? ExportRate)> rateChanges = new();

        // Seed minute 0 so it's always present (rates filled in below)
        rateChanges[0] = (null, null);

        // Record import rate changes within the day
        foreach (NumericHistoryEntry entry in sortedImport)
        {
            DateTime entryUtc = entry.LastChanged.ToUniversalTime();
            if (entryUtc >= fromUtc && entryUtc < toUtc)
            {
                DateTime entryLocal = TimeZoneInfo.ConvertTimeFromUtc(entryUtc, localTimeZone);
                int minutes = (int)(entryLocal - localMidnight).TotalMinutes;
                if (minutes >= 0 && minutes < 1440)
                {
                    rateChanges.TryGetValue(minutes, out (double? ImportRate, double? ExportRate) existing);
                    rateChanges[minutes] = (entry.State * 100, existing.ExportRate);
                }
            }
        }

        // Record export rate changes within the day
        foreach (NumericHistoryEntry entry in sortedExport)
        {
            DateTime entryUtc = entry.LastChanged.ToUniversalTime();
            if (entryUtc >= fromUtc && entryUtc < toUtc)
            {
                DateTime entryLocal = TimeZoneInfo.ConvertTimeFromUtc(entryUtc, localTimeZone);
                int minutes = (int)(entryLocal - localMidnight).TotalMinutes;
                if (minutes >= 0 && minutes < 1440)
                {
                    rateChanges.TryGetValue(minutes, out (double? ImportRate, double? ExportRate) existing);
                    rateChanges[minutes] = (existing.ImportRate, entry.State * 100);
                }
            }
        }

        // Walk the timeline, carrying forward the most recent known rate.
        // Rates at midnight come from FindRateAtTime (using padded history).
        double currentImport = FindRateAtTime(sortedImport, fromUtc) * 100;
        double currentExport = FindRateAtTime(sortedExport, fromUtc) * 100;

        List<PricingSlot> slots = [];

        foreach (KeyValuePair<int, (double? ImportRate, double? ExportRate)> entry in rateChanges)
        {
            if (entry.Value.ImportRate.HasValue)
                currentImport = entry.Value.ImportRate.Value;
            if (entry.Value.ExportRate.HasValue)
                currentExport = entry.Value.ExportRate.Value;

            slots.Add(new PricingSlot
            {
                TimeMinutes = entry.Key,
                ImportPrice = Math.Round(currentImport, 5),
                ExportPrice = Math.Round(currentExport, 5)
            });
        }

        return slots;
    }

    /// <summary>
    /// Collects minutes-from-midnight that represent important transition points:
    /// price changes (import/export rate changes) and zone rule boundaries.
    /// Battery history points near these times are preserved during simplification.
    /// </summary>
    private HashSet<int> CollectProtectedMinutes(
        IReadOnlyList<NumericHistoryEntry> importHistory,
        IReadOnlyList<NumericHistoryEntry> exportHistory,
        DateTime fromUtc, DateTime toUtc,
        DateTime localMidnight, TimeZoneInfo localTimeZone)
    {
        HashSet<int> protectedMinutes = [];

        // Price change times within the day
        foreach (NumericHistoryEntry entry in importHistory)
        {
            DateTime entryUtc = entry.LastChanged.ToUniversalTime();
            if (entryUtc >= fromUtc && entryUtc < toUtc)
            {
                DateTime entryLocal = TimeZoneInfo.ConvertTimeFromUtc(entryUtc, localTimeZone);
                int minutes = (int)(entryLocal - localMidnight).TotalMinutes;
                if (minutes >= 0 && minutes < 1440)
                    protectedMinutes.Add(minutes);
            }
        }

        foreach (NumericHistoryEntry entry in exportHistory)
        {
            DateTime entryUtc = entry.LastChanged.ToUniversalTime();
            if (entryUtc >= fromUtc && entryUtc < toUtc)
            {
                DateTime entryLocal = TimeZoneInfo.ConvertTimeFromUtc(entryUtc, localTimeZone);
                int minutes = (int)(entryLocal - localMidnight).TotalMinutes;
                if (minutes >= 0 && minutes < 1440)
                    protectedMinutes.Add(minutes);
            }
        }

        // Zone rule boundaries (fixed-time rules)
        try
        {
            BatteryZoneRules rules = _rulesPersistence.GetRulesAsync().GetAwaiter().GetResult();
            foreach (BatteryZoneRule rule in rules.Rules)
            {
                if (rule.StartTime.Type == TimeDefinitionType.FixedTime && rule.StartTime.FixedTimeMinutes.HasValue)
                    protectedMinutes.Add(rule.StartTime.FixedTimeMinutes.Value);

                if (rule.EndTime.Type == TimeDefinitionType.FixedTime && rule.EndTime.FixedTimeMinutes.HasValue)
                    protectedMinutes.Add(rule.EndTime.FixedTimeMinutes.Value);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not fetch zone rules for protected minutes, continuing without them");
        }

        return protectedMinutes;
    }

    /// <summary>
    /// Finds the rate that was active at a given UTC time by finding the most recent
    /// entry at or before the target time. Used only for midnight boundary lookups where
    /// the target time is an exact boundary (no sub-minute truncation).
    /// If no entry exists before the target time, falls back to the first available entry's rate
    /// (the earliest known rate is the best approximation when history doesn't extend far enough back).
    /// </summary>
    private static double FindRateAtTime(IReadOnlyList<NumericHistoryEntry> sortedHistory, DateTime targetUtc)
    {
        double rate = 0;
        bool found = false;
        foreach (NumericHistoryEntry entry in sortedHistory)
        {
            DateTime entryUtc = entry.LastChanged.ToUniversalTime();
            if (entryUtc <= targetUtc)
            {
                rate = entry.State;
                found = true;
            }
        }

        // If no entry existed at or before the target time, use the first entry's rate.
        // This handles the case where HA history doesn't extend far enough back
        // (e.g. start of month with purged history).
        if (!found && sortedHistory.Count > 0)
        {
            rate = sortedHistory[0].State;
        }

        return rate;
    }
}

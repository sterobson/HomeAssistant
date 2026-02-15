using HomeAssistant.apps.Energy;
using HomeAssistant.Devices.Meters;
using HomeAssistant.Shared;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Threading.Tasks;

namespace HomeAssistant.Services.Energy;

internal class ElectricityRatePushService
{
    private readonly IScheduler _scheduler;
    private readonly IElectricityRatesReader _ratesReader;
    private readonly IElectricityMeter _electricityMeter;
    private readonly IBatteryPricingApiClient _pricingApiClient;
    private readonly WebSynchronisationConfiguration _configuration;
    private readonly HistoryService _historyService;
    private readonly IDeviceSettingsPersistenceService _settingsPersistence;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ElectricityRatePushService> _logger;

    public ElectricityRatePushService(
        IScheduler scheduler,
        IElectricityRatesReader ratesReader,
        IElectricityMeter electricityMeter,
        IBatteryPricingApiClient pricingApiClient,
        WebSynchronisationConfiguration configuration,
        HistoryService historyService,
        IDeviceSettingsPersistenceService settingsPersistence,
        TimeProvider timeProvider,
        ILogger<ElectricityRatePushService> logger)
    {
        _scheduler = scheduler;
        _ratesReader = ratesReader;
        _electricityMeter = electricityMeter;
        _pricingApiClient = pricingApiClient;
        _configuration = configuration;
        _historyService = historyService;
        _settingsPersistence = settingsPersistence;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public void Initialize()
    {
        // Initial push after 30s delay
        Task.Delay(TimeSpan.FromSeconds(1)).ContinueWith(async _ =>
        {
            await PushScheduledPricingAsync();
        });

        // Re-push every hour
        _scheduler.SchedulePeriodic(TimeSpan.FromHours(1), async () =>
        {
            await PushScheduledPricingAsync();
        });

        // Subscribe to sensor rate changes
        _electricityMeter.OnCurrentRatePerKwhChanged(async change =>
        {
            await HandleRateChangeAsync(change.New);
        });
    }

    private async Task PushScheduledPricingAsync()
    {
        if (string.IsNullOrEmpty(_configuration.HouseId))
        {
            _logger.LogWarning("Cannot push pricing - HouseId not configured");
            return;
        }

        try
        {
            DateTime now = _timeProvider.GetLocalNow().DateTime;
            DateTime end = now.AddHours(48);

            // Calculate dates covered by now → now+48h
            List<DateTime> dates = [];
            for (DateTime date = now.Date; date <= end.Date; date = date.AddDays(1))
            {
                dates.Add(date);
            }

            foreach (DateTime date in dates)
            {
                await PushPricingForDateAsync(date);
            }

            _logger.LogInformation("Pushed pricing data for {Count} dates", dates.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during scheduled pricing push");
        }
    }

    private async Task PushPricingForDateAsync(DateTime date)
    {
        DateTime dayStart = date.Date;
        DateTime dayEnd = dayStart.AddDays(1);
        TimeZoneInfo localZone = TimeZoneInfo.Local;
        DateTime dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(dayStart, localZone);
        DateTime dayEndUtc = TimeZoneInfo.ConvertTimeToUtc(dayEnd, localZone);

        List<EnergyRate> importRates = await _ratesReader.GetElectricityImportRatesAsync(dayStartUtc, dayEndUtc);
        List<EnergyRate> exportRates = await _ratesReader.GetElectricityExportRatesAsync(dayStartUtc, dayEndUtc);

        List<PricingSlot> apiSlots = PricingSlot.FromEnergyRatesExact(importRates, exportRates, date);

        // For today, merge sensor history (past/current) with API forecasts (future)
        DateTime now = _timeProvider.GetLocalNow().DateTime;
        List<PricingSlot> slots;
        if (date.Date == now.Date)
        {
            slots = await MergeSensorHistoryWithApiSlotsAsync(apiSlots, dayStart, dayStartUtc, now);
        }
        else
        {
            slots = apiSlots;
        }

        string dateStr = date.ToString("yyyy-MM-dd");
        await _pricingApiClient.PostPricingAsync(_configuration.HouseId, dateStr, slots);

        _logger.LogDebug("Pushed {Count} pricing slots for {Date}", slots.Count, dateStr);
    }

    /// <summary>
    /// Merges sensor history (for past/current time) with API forecast slots (for future).
    /// Discards API slots at or before current time and replaces them with slots built from
    /// HA sensor history at their actual change times.
    /// </summary>
    private async Task<List<PricingSlot>> MergeSensorHistoryWithApiSlotsAsync(
        List<PricingSlot> apiSlots, DateTime dayStart, DateTime dayStartUtc, DateTime now)
    {
        try
        {
            DeviceSettingsDto settings = await _settingsPersistence.GetSettingsAsync();
            string? importSensorId = settings.Battery?.ElectricityRateSensorId;
            string? exportSensorId = settings.Battery?.ExportRateSensorId;

            if (string.IsNullOrEmpty(importSensorId) && string.IsNullOrEmpty(exportSensorId))
            {
                _logger.LogDebug("No rate sensors configured, using API rates only");
                return apiSlots;
            }

            int currentMinute = now.Hour * 60 + now.Minute;

            // Query sensor history with 24h padding before dayStart for midnight boundary lookup
            DateTime paddedFromUtc = dayStartUtc.AddHours(-24);

            IReadOnlyList<NumericHistoryEntry> importHistory = [];
            IReadOnlyList<NumericHistoryEntry> exportHistory = [];

            if (!string.IsNullOrEmpty(importSensorId))
            {
                importHistory = await _historyService.GetEntityNumericHistory(importSensorId, paddedFromUtc, now);
            }

            if (!string.IsNullOrEmpty(exportSensorId))
            {
                exportHistory = await _historyService.GetEntityNumericHistory(exportSensorId, paddedFromUtc, now);
            }

            if (importHistory.Count == 0 && exportHistory.Count == 0)
            {
                _logger.LogDebug("No sensor history available, using API rates only");
                return apiSlots;
            }

            // Build sensor slots for past/current (midnight to now)
            List<PricingSlot> sensorSlots = BuildSensorSlotsForToday(
                importHistory, exportHistory, dayStart, dayStartUtc, TimeZoneInfo.Local, currentMinute);

            // Keep only API slots after current time
            List<PricingSlot> futureApiSlots = apiSlots
                .Where(s => s.TimeMinutes > currentMinute)
                .ToList();

            // Merge sensor slots (past/current) with API slots (future)
            List<PricingSlot> merged = [.. sensorSlots, .. futureApiSlots];
            merged.Sort((a, b) => a.TimeMinutes.CompareTo(b.TimeMinutes));

            _logger.LogInformation(
                "Merged pricing: {SensorCount} sensor slots (past/current) + {ApiCount} API slots (future) = {TotalCount} total",
                sensorSlots.Count, futureApiSlots.Count, merged.Count);

            return merged;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to merge sensor history, using API rates only");
            return apiSlots;
        }
    }

    /// <summary>
    /// Builds pricing slots from sensor history for today, from midnight up to currentMinute.
    /// Each slot is placed at the actual sensor change time, with rates converted from £/kWh to p/kWh.
    /// </summary>
    private static List<PricingSlot> BuildSensorSlotsForToday(
        IReadOnlyList<NumericHistoryEntry> importHistory,
        IReadOnlyList<NumericHistoryEntry> exportHistory,
        DateTime localMidnight,
        DateTime dayStartUtc,
        TimeZoneInfo localZone,
        int currentMinute)
    {
        List<NumericHistoryEntry> sortedImport = importHistory.OrderBy(e => e.LastChanged).ToList();
        List<NumericHistoryEntry> sortedExport = exportHistory.OrderBy(e => e.LastChanged).ToList();

        // Build a map of minute → (importRate, exportRate) from sensor change times
        SortedDictionary<int, (double? ImportRate, double? ExportRate)> rateChanges = new();

        // Always seed minute 0 so midnight is present
        rateChanges[0] = (null, null);

        // Record import rate changes within today up to currentMinute
        foreach (NumericHistoryEntry entry in sortedImport)
        {
            DateTime entryUtc = entry.LastChanged.ToUniversalTime();
            if (entryUtc >= dayStartUtc)
            {
                DateTime entryLocal = TimeZoneInfo.ConvertTimeFromUtc(entryUtc, localZone);
                int minutes = (int)(entryLocal - localMidnight).TotalMinutes;
                if (minutes >= 0 && minutes <= currentMinute)
                {
                    rateChanges.TryGetValue(minutes, out (double? ImportRate, double? ExportRate) existing);
                    rateChanges[minutes] = (entry.State * 100, existing.ExportRate);
                }
            }
        }

        // Record export rate changes within today up to currentMinute
        foreach (NumericHistoryEntry entry in sortedExport)
        {
            DateTime entryUtc = entry.LastChanged.ToUniversalTime();
            if (entryUtc >= dayStartUtc)
            {
                DateTime entryLocal = TimeZoneInfo.ConvertTimeFromUtc(entryUtc, localZone);
                int minutes = (int)(entryLocal - localMidnight).TotalMinutes;
                if (minutes >= 0 && minutes <= currentMinute)
                {
                    rateChanges.TryGetValue(minutes, out (double? ImportRate, double? ExportRate) existing);
                    rateChanges[minutes] = (existing.ImportRate, entry.State * 100);
                }
            }
        }

        // Walk the timeline, carrying forward the most recent known rate.
        // Rates at midnight come from FindRateAtTime (using padded history).
        double currentImport = FindRateAtTime(sortedImport, dayStartUtc) * 100;
        double currentExport = FindRateAtTime(sortedExport, dayStartUtc) * 100;

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
    /// Finds the rate that was active at a given UTC time by finding the most recent
    /// entry at or before the target time. Used for midnight boundary lookups.
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

        if (!found && sortedHistory.Count > 0)
        {
            rate = sortedHistory[0].State;
        }

        return rate;
    }

    private async Task HandleRateChangeAsync(double? newSensorRate)
    {
        if (string.IsNullOrEmpty(_configuration.HouseId))
        {
            _logger.LogWarning("Cannot push rate override - HouseId not configured");
            return;
        }

        if (newSensorRate == null)
        {
            _logger.LogDebug("Sensor rate is null, skipping override");
            return;
        }

        try
        {
            // Sensor reports in £/kWh, Octopus API uses p/kWh — convert sensor to pence
            double sensorPencePerKwh = newSensorRate.Value * 100;

            EnergyRate octopusRate = await _ratesReader.GetCurrentElectricityImportRateAsync();
            double octopusImportPrice = octopusRate.RateIncVat;

            // Compare in p/kWh with 0.1p tolerance
            if (Math.Abs(sensorPencePerKwh - octopusImportPrice) < 0.1)
            {
                _logger.LogDebug("Sensor rate matches Octopus rate ({Rate}p/kWh), no override needed", octopusImportPrice);
                return;
            }

            _logger.LogInformation(
                "Sensor rate ({SensorRate}p/kWh) differs from Octopus rate ({OctopusRate}p/kWh), pushing override",
                sensorPencePerKwh, octopusImportPrice);

            DateTime now = _timeProvider.GetLocalNow().DateTime;
            int currentMinute = now.Hour * 60 + now.Minute;

            DateTime dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(now.Date, TimeZoneInfo.Local);
            DateTime dayEndUtc = TimeZoneInfo.ConvertTimeToUtc(now.Date.AddDays(1), TimeZoneInfo.Local);
            List<EnergyRate> importRates = await _ratesReader.GetElectricityImportRatesAsync(dayStartUtc, dayEndUtc);

            int revertMinute = PriceAnalysis.DetermineOverrideEndMinutes(
                importRates, now, sensorPencePerKwh);

            // Push today's pricing with the override
            await PushOverrideForDateAsync(now.Date, currentMinute, revertMinute, sensorPencePerKwh);

            // If the override extends past midnight, also push tomorrow
            if (revertMinute >= 1440)
            {
                int tomorrowRevertMinute = revertMinute - 1440;
                await PushOverrideForDateAsync(now.Date.AddDays(1), 0, tomorrowRevertMinute, sensorPencePerKwh);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling rate change override");
        }
    }

    private async Task PushOverrideForDateAsync(DateTime date, int overrideStartMinute, int overrideEndMinute, double sensorImportRate)
    {
        DateTime dayStart = date.Date;
        DateTime dayEnd = dayStart.AddDays(1);
        TimeZoneInfo localZone = TimeZoneInfo.Local;
        DateTime dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(dayStart, localZone);
        DateTime dayEndUtc = TimeZoneInfo.ConvertTimeToUtc(dayEnd, localZone);

        List<EnergyRate> importRates = await _ratesReader.GetElectricityImportRatesAsync(dayStartUtc, dayEndUtc);
        List<EnergyRate> exportRates = await _ratesReader.GetElectricityExportRatesAsync(dayStartUtc, dayEndUtc);

        List<PricingSlot> slots = PricingSlot.FromEnergyRatesExact(importRates, exportRates, date);

        // Clamp override end to this day
        int effectiveEnd = Math.Min(overrideEndMinute, 1439);

        // Find the export rate at the override start time for the override point
        DateTime overrideLocalTime = dayStart.AddMinutes(overrideStartMinute);
        DateTime overrideUtcTime = TimeZoneInfo.ConvertTimeToUtc(overrideLocalTime, localZone);
        EnergyRate? exportAtOverride = exportRates
            .FirstOrDefault(r => r.StartTimeUtc <= overrideUtcTime && r.EndTimeUtc > overrideUtcTime);

        // Remove any existing points within the override window
        slots.RemoveAll(s => s.TimeMinutes > overrideStartMinute && s.TimeMinutes < effectiveEnd);

        // Insert override start point
        PricingSlot? existingAtStart = slots.FirstOrDefault(s => s.TimeMinutes == overrideStartMinute);
        if (existingAtStart != null)
        {
            existingAtStart.ImportPrice = sensorImportRate;
        }
        else
        {
            slots.Add(new PricingSlot
            {
                TimeMinutes = overrideStartMinute,
                ImportPrice = sensorImportRate,
                ExportPrice = exportAtOverride?.RateIncVat ?? 0
            });
        }

        // Insert revert point (back to Octopus rate) if within this day
        if (effectiveEnd < 1440)
        {
            DateTime revertLocalTime = dayStart.AddMinutes(effectiveEnd);
            DateTime revertUtcTime = TimeZoneInfo.ConvertTimeToUtc(revertLocalTime, localZone);

            EnergyRate? importAtRevert = importRates
                .FirstOrDefault(r => r.StartTimeUtc <= revertUtcTime && r.EndTimeUtc > revertUtcTime);
            EnergyRate? exportAtRevert = exportRates
                .FirstOrDefault(r => r.StartTimeUtc <= revertUtcTime && r.EndTimeUtc > revertUtcTime);

            PricingSlot? existingAtEnd = slots.FirstOrDefault(s => s.TimeMinutes == effectiveEnd);
            if (existingAtEnd != null)
            {
                existingAtEnd.ImportPrice = importAtRevert?.RateIncVat ?? 0;
                existingAtEnd.ExportPrice = exportAtRevert?.RateIncVat ?? 0;
            }
            else
            {
                slots.Add(new PricingSlot
                {
                    TimeMinutes = effectiveEnd,
                    ImportPrice = importAtRevert?.RateIncVat ?? 0,
                    ExportPrice = exportAtRevert?.RateIncVat ?? 0
                });
            }
        }

        // Sort by time
        slots = slots.OrderBy(s => s.TimeMinutes).ToList();

        string dateStr = date.ToString("yyyy-MM-dd");
        await _pricingApiClient.PostPricingAsync(_configuration.HouseId, dateStr, slots);

        _logger.LogInformation("Pushed override pricing for {Date}: override at minute {Start}, revert at minute {End}",
            dateStr, overrideStartMinute, effectiveEnd);
    }
}

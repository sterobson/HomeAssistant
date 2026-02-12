using HomeAssistant.apps.Energy;
using HomeAssistant.Devices.Meters;
using HomeAssistant.Services;
using HomeAssistant.Services.Energy;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Threading.Tasks;
using System;

namespace HomeAssistant.apps.HassModel.Energy;

[NetDaemonApp]
internal class ElectricityRatePushService
{
    private readonly IElectricityRatesReader _ratesReader;
    private readonly IElectricityMeter _electricityMeter;
    private readonly IBatteryPricingApiClient _pricingApiClient;
    private readonly WebSynchronisationConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ElectricityRatePushService> _logger;

    public ElectricityRatePushService(
        IScheduler scheduler,
        IElectricityRatesReader ratesReader,
        IElectricityMeter electricityMeter,
        IBatteryPricingApiClient pricingApiClient,
        WebSynchronisationConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<ElectricityRatePushService> logger)
    {
        _ratesReader = ratesReader;
        _electricityMeter = electricityMeter;
        _pricingApiClient = pricingApiClient;
        _configuration = configuration;
        _timeProvider = timeProvider;
        _logger = logger;

        // Initial push after 30s delay
        Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(async _ =>
        {
            await PushScheduledPricingAsync();
        });

        // Re-push every hour
        scheduler.SchedulePeriodic(TimeSpan.FromHours(1), async () =>
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

        List<PricingSlot> slots = PricingSlot.FromEnergyRatesExact(importRates, exportRates, date);

        string dateStr = date.ToString("yyyy-MM-dd");
        await _pricingApiClient.PostPricingAsync(_configuration.HouseId, dateStr, slots);

        _logger.LogDebug("Pushed {Count} pricing slots for {Date}", slots.Count, dateStr);
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
            EnergyRate octopusRate = await _ratesReader.GetCurrentElectricityImportRateAsync();
            double octopusImportPrice = octopusRate.RateIncVat;

            // Compare with a small tolerance for floating point
            if (Math.Abs(newSensorRate.Value - octopusImportPrice) < 0.001)
            {
                _logger.LogDebug("Sensor rate matches Octopus rate ({Rate}), no override needed", octopusImportPrice);
                return;
            }

            _logger.LogInformation(
                "Sensor rate ({SensorRate}) differs from Octopus rate ({OctopusRate}), pushing override",
                newSensorRate.Value, octopusImportPrice);

            DateTime now = _timeProvider.GetLocalNow().DateTime;
            int currentMinute = now.Hour * 60 + now.Minute;

            DateTime dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(now.Date, TimeZoneInfo.Local);
            DateTime dayEndUtc = TimeZoneInfo.ConvertTimeToUtc(now.Date.AddDays(1), TimeZoneInfo.Local);
            List<EnergyRate> importRates = await _ratesReader.GetElectricityImportRatesAsync(dayStartUtc, dayEndUtc);

            int revertMinute = PriceAnalysis.DetermineOverrideEndMinutes(
                importRates, now, newSensorRate.Value);

            // Push today's pricing with the override
            await PushOverrideForDateAsync(now.Date, currentMinute, revertMinute, newSensorRate.Value);

            // If the override extends past midnight, also push tomorrow
            if (revertMinute >= 1440)
            {
                int tomorrowRevertMinute = revertMinute - 1440;
                await PushOverrideForDateAsync(now.Date.AddDays(1), 0, tomorrowRevertMinute, newSensorRate.Value);
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

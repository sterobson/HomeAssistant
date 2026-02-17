using HomeAssistant.apps.Energy;
using System.Collections.Generic;
using System.Linq;

namespace HomeAssistant.Services.Energy;

public class PricingSlot
{
    public int TimeMinutes { get; set; }
    public double ImportPrice { get; set; }
    public double ExportPrice { get; set; }

    public static List<PricingSlot> FromEnergyRatesExact(
        List<EnergyRate> importRates,
        List<EnergyRate> exportRates,
        DateTime localDate)
    {
        DateTime dayStart = localDate.Date;
        TimeZoneInfo localZone = TimeZoneInfo.Local;
        DateTime dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(dayStart, localZone);
        DateTime dayEndUtc = TimeZoneInfo.ConvertTimeToUtc(dayStart.AddDays(1), localZone);

        // Collect all unique rate-change times within the day
        SortedSet<int> changeTimes = [];

        foreach (EnergyRate rate in importRates)
        {
            if (rate.StartTimeUtc >= dayStartUtc && rate.StartTimeUtc < dayEndUtc)
            {
                DateTime localTime = TimeZoneInfo.ConvertTimeFromUtc(rate.StartTimeUtc, localZone);
                int minutes = (int)(localTime - dayStart).TotalMinutes;
                if (minutes >= 0 && minutes < 1440)
                    changeTimes.Add(minutes);
            }
        }

        foreach (EnergyRate rate in exportRates)
        {
            if (rate.StartTimeUtc >= dayStartUtc && rate.StartTimeUtc < dayEndUtc)
            {
                DateTime localTime = TimeZoneInfo.ConvertTimeFromUtc(rate.StartTimeUtc, localZone);
                int minutes = (int)(localTime - dayStart).TotalMinutes;
                if (minutes >= 0 && minutes < 1440)
                    changeTimes.Add(minutes);
            }
        }

        // Always include minute 0
        changeTimes.Add(0);

        List<PricingSlot> slots = [];

        foreach (int minutes in changeTimes)
        {
            DateTime slotLocalStart = dayStart.AddMinutes(minutes);
            DateTime slotUtcStart = TimeZoneInfo.ConvertTimeToUtc(slotLocalStart, localZone);

            EnergyRate? importRate = importRates
                .FirstOrDefault(r => r.StartTimeUtc <= slotUtcStart && r.EndTimeUtc > slotUtcStart);

            EnergyRate? exportRate = exportRates
                .FirstOrDefault(r => r.StartTimeUtc <= slotUtcStart && r.EndTimeUtc > slotUtcStart);

            slots.Add(new PricingSlot
            {
                TimeMinutes = minutes,
                ImportPrice = importRate?.RateIncVat ?? 0,
                ExportPrice = exportRate?.RateIncVat ?? 0
            });
        }

        return slots;
    }

    public static List<PricingSlot> ExtendWithPreviousDay(
        List<PricingSlot> today, List<PricingSlot> yesterday)
    {
        List<PricingSlot> extended = [];

        foreach (PricingSlot slot in yesterday)
        {
            extended.Add(new PricingSlot
            {
                TimeMinutes = slot.TimeMinutes - 1440,
                ImportPrice = slot.ImportPrice,
                ExportPrice = slot.ExportPrice
            });
        }

        extended.AddRange(today.Select(s => new PricingSlot
        {
            TimeMinutes = s.TimeMinutes,
            ImportPrice = s.ImportPrice,
            ExportPrice = s.ExportPrice
        }));

        return extended;
    }

    public static List<PricingSlot> ExtendWithNextDay(
        List<PricingSlot> today, List<PricingSlot> tomorrow)
    {
        List<PricingSlot> extended = today.Select(s => new PricingSlot
        {
            TimeMinutes = s.TimeMinutes,
            ImportPrice = s.ImportPrice,
            ExportPrice = s.ExportPrice
        }).ToList();

        foreach (PricingSlot slot in tomorrow)
        {
            extended.Add(new PricingSlot
            {
                TimeMinutes = slot.TimeMinutes + 1440,
                ImportPrice = slot.ImportPrice,
                ExportPrice = slot.ExportPrice
            });
        }

        return extended;
    }
}

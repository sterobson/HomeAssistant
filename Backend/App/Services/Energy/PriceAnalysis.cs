using HomeAssistant.apps.Energy;
using System.Collections.Generic;

namespace HomeAssistant.Services.Energy;

public class PriceBoundary
{
    public int TimeMinutes { get; set; }
    public double ImportPrice { get; set; }
    public double ExportPrice { get; set; }
    public string BoundaryType { get; set; } = string.Empty; // "start" or "end"
}

public static class PriceAnalysis
{
    internal static int DetermineOverrideEndMinutes(
        List<EnergyRate> publishedImportRates,
        DateTime localNow,
        double newImportRate,
        int alignmentToleranceMinutes = 5,
        int defaultOverrideMinutes = 60)
    {
        DateTime dayStart = localNow.Date;
        int currentMinutes = localNow.Hour * 60 + localNow.Minute;
        TimeZoneInfo localZone = TimeZoneInfo.Local;

        // Build boundaries: (minuteOfDay, rateIncVat) from published rates for today
        List<(int minuteOfDay, double rate)> boundaries = [];
        foreach (EnergyRate rate in publishedImportRates)
        {
            DateTime localStart = TimeZoneInfo.ConvertTimeFromUtc(rate.StartTimeUtc, localZone);
            if (localStart.Date == dayStart.Date)
            {
                int minuteOfDay = localStart.Hour * 60 + localStart.Minute;
                boundaries.Add((minuteOfDay, rate.RateIncVat));
            }
        }
        boundaries.Sort((a, b) => a.minuteOfDay.CompareTo(b.minuteOfDay));

        // Check if the rate change is aligned with a published boundary
        bool aligned = false;
        int alignedBoundaryIndex = -1;
        for (int i = 0; i < boundaries.Count; i++)
        {
            int diff = Math.Abs(boundaries[i].minuteOfDay - currentMinutes);
            if (diff <= alignmentToleranceMinutes && Math.Abs(boundaries[i].rate - newImportRate) < 0.001)
            {
                aligned = true;
                alignedBoundaryIndex = i;
                break;
            }
        }

        if (aligned)
        {
            // Scan forward to find when the rate changes from newImportRate
            for (int i = alignedBoundaryIndex + 1; i < boundaries.Count; i++)
            {
                if (Math.Abs(boundaries[i].rate - newImportRate) >= 0.001)
                {
                    return boundaries[i].minuteOfDay;
                }
            }
            // Rate stays the same for the rest of the day
            return 1440;
        }

        // Not aligned: default override duration, clamped to end of day
        return Math.Min(currentMinutes + defaultOverrideMinutes, 1440);
    }

    private static bool ExceedsWithThreshold(double a, double b, string? thresholdType, double? thresholdValue)
    {
        if (thresholdType == null || thresholdValue == null)
            return a > b;

        if (thresholdType == "absolute")
            return a > b + thresholdValue.Value * 100;

        if (thresholdType == "percent")
            return a > b * (1 + thresholdValue.Value / 100);

        return a > b;
    }

    public static List<PricingSlot> FindExportExceedsImportCrossovers(
        List<PricingSlot> data, string? thresholdType = null, double? thresholdValue = null)
    {
        List<PricingSlot> crossovers = [];
        for (int i = 1; i < data.Count; i++)
        {
            PricingSlot prev = data[i - 1];
            PricingSlot curr = data[i];
            if (!ExceedsWithThreshold(prev.ExportPrice, prev.ImportPrice, thresholdType, thresholdValue)
                && ExceedsWithThreshold(curr.ExportPrice, curr.ImportPrice, thresholdType, thresholdValue))
            {
                crossovers.Add(curr);
            }
        }
        return crossovers;
    }

    public static List<PricingSlot> FindImportExceedsExportCrossovers(
        List<PricingSlot> data, string? thresholdType = null, double? thresholdValue = null)
    {
        List<PricingSlot> crossovers = [];
        for (int i = 1; i < data.Count; i++)
        {
            PricingSlot prev = data[i - 1];
            PricingSlot curr = data[i];
            if (!ExceedsWithThreshold(prev.ImportPrice, prev.ExportPrice, thresholdType, thresholdValue)
                && ExceedsWithThreshold(curr.ImportPrice, curr.ExportPrice, thresholdType, thresholdValue))
            {
                crossovers.Add(curr);
            }
        }
        return crossovers;
    }

    public static List<PricingSlot> FindPeaks(List<PricingSlot> data, Func<PricingSlot, double> priceSelector)
    {
        if (data.Count < 2) return [];

        List<PricingSlot> peaks = [];
        int i = 0;

        while (i < data.Count)
        {
            int j = i;
            while (j < data.Count - 1 && priceSelector(data[j + 1]) == priceSelector(data[i]))
            {
                j++;
            }

            double currVal = priceSelector(data[i]);
            double? prevVal = i > 0 ? priceSelector(data[i - 1]) : null;
            double? nextVal = j < data.Count - 1 ? priceSelector(data[j + 1]) : null;

            if (prevVal != null && nextVal != null && currVal > prevVal && currVal > nextVal)
            {
                int midIndex = (i + j) / 2;
                peaks.Add(data[midIndex]);
            }

            i = j + 1;
        }

        return peaks;
    }

    public static List<PricingSlot> FindTroughs(List<PricingSlot> data, Func<PricingSlot, double> priceSelector)
    {
        if (data.Count < 2) return [];

        List<PricingSlot> troughs = [];
        int i = 0;

        while (i < data.Count)
        {
            int j = i;
            while (j < data.Count - 1 && priceSelector(data[j + 1]) == priceSelector(data[i]))
            {
                j++;
            }

            double currVal = priceSelector(data[i]);
            double? prevVal = i > 0 ? priceSelector(data[i - 1]) : null;
            double? nextVal = j < data.Count - 1 ? priceSelector(data[j + 1]) : null;

            if (prevVal != null && nextVal != null && currVal < prevVal && currVal < nextVal)
            {
                int midIndex = (i + j) / 2;
                troughs.Add(data[midIndex]);
            }

            i = j + 1;
        }

        return troughs;
    }

    public static List<PriceBoundary> FindMinimaRegionBoundaries(
        List<PricingSlot> data, Func<PricingSlot, double> priceSelector)
    {
        List<PriceBoundary> boundaries = [];
        int i = 0;

        while (i < data.Count)
        {
            int j = i;
            while (j < data.Count - 1 && priceSelector(data[j + 1]) == priceSelector(data[i]))
            {
                j++;
            }

            double currVal = priceSelector(data[i]);
            double? prevVal = i > 0 ? priceSelector(data[i - 1]) : null;
            double? nextVal = j < data.Count - 1 ? priceSelector(data[j + 1]) : null;

            if (prevVal != null && nextVal != null && currVal < prevVal && currVal < nextVal)
            {
                boundaries.Add(new PriceBoundary
                {
                    TimeMinutes = data[i].TimeMinutes,
                    ImportPrice = data[i].ImportPrice,
                    ExportPrice = data[i].ExportPrice,
                    BoundaryType = "start"
                });
                boundaries.Add(new PriceBoundary
                {
                    TimeMinutes = data[j + 1].TimeMinutes,
                    ImportPrice = data[j + 1].ImportPrice,
                    ExportPrice = data[j + 1].ExportPrice,
                    BoundaryType = "end"
                });
            }

            i = j + 1;
        }
        return boundaries;
    }

    public static List<PriceBoundary> FindMaximaRegionBoundaries(
        List<PricingSlot> data, Func<PricingSlot, double> priceSelector)
    {
        List<PriceBoundary> boundaries = [];
        int i = 0;

        while (i < data.Count)
        {
            int j = i;
            while (j < data.Count - 1 && priceSelector(data[j + 1]) == priceSelector(data[i]))
            {
                j++;
            }

            double currVal = priceSelector(data[i]);
            double? prevVal = i > 0 ? priceSelector(data[i - 1]) : null;
            double? nextVal = j < data.Count - 1 ? priceSelector(data[j + 1]) : null;

            if (prevVal != null && nextVal != null && currVal > prevVal && currVal > nextVal)
            {
                boundaries.Add(new PriceBoundary
                {
                    TimeMinutes = data[i].TimeMinutes,
                    ImportPrice = data[i].ImportPrice,
                    ExportPrice = data[i].ExportPrice,
                    BoundaryType = "start"
                });
                boundaries.Add(new PriceBoundary
                {
                    TimeMinutes = data[j + 1].TimeMinutes,
                    ImportPrice = data[j + 1].ImportPrice,
                    ExportPrice = data[j + 1].ExportPrice,
                    BoundaryType = "end"
                });
            }

            i = j + 1;
        }
        return boundaries;
    }
}

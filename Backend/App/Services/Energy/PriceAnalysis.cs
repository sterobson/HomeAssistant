using HomeAssistant.apps.Energy;
using System.Collections.Generic;
using System.Linq;

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

    /// <summary>
    /// Calculate duration-weighted average price across a full day (0–1440 minutes).
    /// Each slot's price applies from its timeMinutes until the next slot starts.
    /// The first slot's price back-fills to minute 0; the last forward-fills to 1440.
    /// </summary>
    public static double? CalculateAveragePrice(List<PricingSlot> data, Func<PricingSlot, double> priceSelector)
    {
        if (data == null || data.Count == 0) return null;

        List<PricingSlot> slots = data
            .Where(d => d.TimeMinutes >= 0 && d.TimeMinutes <= 1440)
            .OrderBy(d => d.TimeMinutes)
            .ToList();

        if (slots.Count == 0) return null;

        double weightedSum = 0;
        for (int i = 0; i < slots.Count; i++)
        {
            int start = i == 0 ? 0 : slots[i].TimeMinutes;
            int end = i < slots.Count - 1 ? slots[i + 1].TimeMinutes : 1440;
            int duration = end - start;
            weightedSum += priceSelector(slots[i]) * duration;
        }

        return weightedSum / 1440.0;
    }

    /// <summary>
    /// Find start/end of below-average price regions.
    /// A region is "cheap" when price drops to 25%+ below the day's average (price &lt;= average × 0.75).
    /// </summary>
    public static List<PriceBoundary> FindMinimaRegionBoundaries(
        List<PricingSlot> data, Func<PricingSlot, double> priceSelector)
    {
        List<PriceBoundary> boundaries = [];
        double? avg = CalculateAveragePrice(data, priceSelector);
        if (avg == null) return boundaries;

        double threshold = avg.Value * 0.75;
        bool inRegion = false;

        for (int i = 0; i < data.Count; i++)
        {
            bool cheap = priceSelector(data[i]) <= threshold;
            if (cheap && !inRegion)
            {
                boundaries.Add(new PriceBoundary
                {
                    TimeMinutes = data[i].TimeMinutes,
                    ImportPrice = data[i].ImportPrice,
                    ExportPrice = data[i].ExportPrice,
                    BoundaryType = "start"
                });
                inRegion = true;
            }
            else if (!cheap && inRegion)
            {
                boundaries.Add(new PriceBoundary
                {
                    TimeMinutes = data[i].TimeMinutes,
                    ImportPrice = data[i].ImportPrice,
                    ExportPrice = data[i].ExportPrice,
                    BoundaryType = "end"
                });
                inRegion = false;
            }
        }

        if (inRegion)
        {
            PricingSlot last = data[^1];
            boundaries.Add(new PriceBoundary
            {
                TimeMinutes = 1440,
                ImportPrice = last.ImportPrice,
                ExportPrice = last.ExportPrice,
                BoundaryType = "end"
            });
        }

        return boundaries;
    }

    /// <summary>
    /// Find start/end of above-average price regions.
    /// A region is "expensive" when price rises to 25%+ above the day's average (price &gt;= average × 1.25).
    /// </summary>
    public static List<PriceBoundary> FindMaximaRegionBoundaries(
        List<PricingSlot> data, Func<PricingSlot, double> priceSelector)
    {
        List<PriceBoundary> boundaries = [];
        double? avg = CalculateAveragePrice(data, priceSelector);
        if (avg == null) return boundaries;

        double threshold = avg.Value * 1.25;
        bool inRegion = false;

        for (int i = 0; i < data.Count; i++)
        {
            bool expensive = priceSelector(data[i]) >= threshold;
            if (expensive && !inRegion)
            {
                boundaries.Add(new PriceBoundary
                {
                    TimeMinutes = data[i].TimeMinutes,
                    ImportPrice = data[i].ImportPrice,
                    ExportPrice = data[i].ExportPrice,
                    BoundaryType = "start"
                });
                inRegion = true;
            }
            else if (!expensive && inRegion)
            {
                boundaries.Add(new PriceBoundary
                {
                    TimeMinutes = data[i].TimeMinutes,
                    ImportPrice = data[i].ImportPrice,
                    ExportPrice = data[i].ExportPrice,
                    BoundaryType = "end"
                });
                inRegion = false;
            }
        }

        if (inRegion)
        {
            PricingSlot last = data[^1];
            boundaries.Add(new PriceBoundary
            {
                TimeMinutes = 1440,
                ImportPrice = last.ImportPrice,
                ExportPrice = last.ExportPrice,
                BoundaryType = "end"
            });
        }

        return boundaries;
    }
}

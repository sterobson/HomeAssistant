using System.Collections.Generic;
using System.Linq;

namespace HomeAssistant.Services.Energy;

public class ResolvedZone
{
    public string RuleId { get; set; } = string.Empty;
    public int StartMinutes { get; set; }
    public int EndMinutes { get; set; }
    public BatteryZoneAction Action { get; set; }
    public int TargetPercent { get; set; }
    public bool IsSmart { get; set; }
    public bool GraduatedTarget { get; set; }
}

public static class BatteryZoneResolver
{
    /// <summary>
    /// Check if a minute value falls within a zone, handling zones where EndMinutes > 1440 (midnight wrap).
    /// </summary>
    public static bool IsMinuteInZone(int currentMinutes, int startMinutes, int endMinutes)
    {
        if (endMinutes <= 1440)
        {
            // Normal zone
            return currentMinutes >= startMinutes && currentMinutes < endMinutes;
        }

        // Wrapped zone: current is in zone if it's in the evening portion OR the morning portion
        return currentMinutes >= startMinutes || currentMinutes < (endMinutes - 1440);
    }

    /// <summary>
    /// Get elapsed minutes since zone start, handling zones where EndMinutes > 1440 (midnight wrap).
    /// </summary>
    public static int GetElapsedMinutes(int currentMinutes, int startMinutes, int endMinutes)
    {
        if (currentMinutes >= startMinutes)
        {
            return currentMinutes - startMinutes;
        }

        // Morning portion of a wrapped zone
        return (1440 - startMinutes) + currentMinutes;
    }

    public static List<int> ResolveTimeDefinition(TimeDefinition def, List<PricingSlot> slots)
    {
        switch (def.Type)
        {
            case TimeDefinitionType.FixedTime:
                return def.FixedTimeMinutes.HasValue ? [def.FixedTimeMinutes.Value] : [];

            case TimeDefinitionType.ExportExceedsImport:
            {
                List<PricingSlot> crossovers = PriceAnalysis.FindExportExceedsImportCrossovers(
                    slots, def.ThresholdType, def.ThresholdValue);
                return crossovers.Select(s => s.TimeMinutes).ToList();
            }

            case TimeDefinitionType.ImportExceedsExport:
            {
                List<PricingSlot> crossovers = PriceAnalysis.FindImportExceedsExportCrossovers(
                    slots, def.ThresholdType, def.ThresholdValue);
                return crossovers.Select(s => s.TimeMinutes).ToList();
            }

            case TimeDefinitionType.StartOfCheapImport:
            {
                List<PriceBoundary> boundaries = PriceAnalysis.FindMinimaRegionBoundaries(
                    slots, s => s.ImportPrice);
                return boundaries.Where(b => b.BoundaryType == "start").Select(b => b.TimeMinutes).ToList();
            }

            case TimeDefinitionType.EndOfCheapImport:
            {
                List<PriceBoundary> boundaries = PriceAnalysis.FindMinimaRegionBoundaries(
                    slots, s => s.ImportPrice);
                return boundaries.Where(b => b.BoundaryType == "end").Select(b => b.TimeMinutes).ToList();
            }

            case TimeDefinitionType.StartOfExpensiveImport:
            {
                List<PriceBoundary> boundaries = PriceAnalysis.FindMaximaRegionBoundaries(
                    slots, s => s.ImportPrice);
                return boundaries.Where(b => b.BoundaryType == "start").Select(b => b.TimeMinutes).ToList();
            }

            case TimeDefinitionType.EndOfExpensiveImport:
            {
                List<PriceBoundary> boundaries = PriceAnalysis.FindMaximaRegionBoundaries(
                    slots, s => s.ImportPrice);
                return boundaries.Where(b => b.BoundaryType == "end").Select(b => b.TimeMinutes).ToList();
            }

            case TimeDefinitionType.StartOfCheapExport:
            {
                List<PriceBoundary> boundaries = PriceAnalysis.FindMinimaRegionBoundaries(
                    slots, s => s.ExportPrice);
                return boundaries.Where(b => b.BoundaryType == "start").Select(b => b.TimeMinutes).ToList();
            }

            case TimeDefinitionType.EndOfCheapExport:
            {
                List<PriceBoundary> boundaries = PriceAnalysis.FindMinimaRegionBoundaries(
                    slots, s => s.ExportPrice);
                return boundaries.Where(b => b.BoundaryType == "end").Select(b => b.TimeMinutes).ToList();
            }

            case TimeDefinitionType.StartOfExpensiveExport:
            {
                List<PriceBoundary> boundaries = PriceAnalysis.FindMaximaRegionBoundaries(
                    slots, s => s.ExportPrice);
                return boundaries.Where(b => b.BoundaryType == "start").Select(b => b.TimeMinutes).ToList();
            }

            case TimeDefinitionType.EndOfExpensiveExport:
            {
                List<PriceBoundary> boundaries = PriceAnalysis.FindMaximaRegionBoundaries(
                    slots, s => s.ExportPrice);
                return boundaries.Where(b => b.BoundaryType == "end").Select(b => b.TimeMinutes).ToList();
            }

            default:
                return [];
        }
    }

    public static List<ResolvedZone> EvaluateZoneRule(BatteryZoneRule rule, List<PricingSlot> slots)
    {
        bool isSmart = rule.StartTime.Type != TimeDefinitionType.FixedTime
                    || rule.EndTime.Type != TimeDefinitionType.FixedTime;

        List<int> startTimes = ResolveTimeDefinition(rule.StartTime, slots);
        startTimes.Sort();

        List<int> endTimes = ResolveTimeDefinition(rule.EndTime, slots);
        endTimes.Sort();

        if (startTimes.Count == 0 || endTimes.Count == 0) return [];

        // Greedy pairing on raw (un-normalized) times so natural start/end pairs are preserved.
        // Boundary-detection methods return starts and ends in chronological order across
        // yesterday/today/tomorrow slots. Pairing on raw times keeps (-1440,-480) together
        // rather than letting a normalized yesterday-start steal today's end.
        HashSet<int> usedEnds = [];
        List<ResolvedZone> zones = [];

        foreach (int start in startTimes)
        {
            int? bestEnd = null;
            int bestEndIndex = -1;
            for (int i = 0; i < endTimes.Count; i++)
            {
                if (usedEnds.Contains(i)) continue;
                if (endTimes[i] > start)
                {
                    if (bestEnd == null || endTimes[i] < bestEnd)
                    {
                        bestEnd = endTimes[i];
                        bestEndIndex = i;
                    }
                }
            }
            if (bestEnd != null)
            {
                usedEnds.Add(bestEndIndex);
                zones.Add(new ResolvedZone
                {
                    RuleId = rule.Id,
                    StartMinutes = start,
                    EndMinutes = bestEnd.Value,
                    Action = rule.Action,
                    TargetPercent = rule.TargetPercent,
                    IsSmart = isSmart,
                    GraduatedTarget = rule.GraduatedTarget
                });
            }
        }

        // Second pass: wrap around midnight for unmatched starts
        // Emit a single zone with EndMinutes > 1440 — consumers use IsMinuteInZone/GetElapsedMinutes
        HashSet<int> matchedStarts = new(zones.Select(z => z.StartMinutes));
        foreach (int start in startTimes)
        {
            if (matchedStarts.Contains(start)) continue;

            int? bestEnd = null;
            int bestEndIndex = -1;
            for (int i = 0; i < endTimes.Count; i++)
            {
                if (usedEnds.Contains(i)) continue;
                int wrapped = endTimes[i] + 1440;
                if (wrapped > start)
                {
                    if (bestEnd == null || wrapped < bestEnd)
                    {
                        bestEnd = wrapped;
                        bestEndIndex = i;
                    }
                }
            }
            if (bestEnd != null)
            {
                usedEnds.Add(bestEndIndex);
                zones.Add(new ResolvedZone
                {
                    RuleId = rule.Id,
                    StartMinutes = start,
                    EndMinutes = bestEnd.Value,
                    Action = rule.Action,
                    TargetPercent = rule.TargetPercent,
                    IsSmart = isSmart,
                    GraduatedTarget = rule.GraduatedTarget
                });
            }
        }

        // Normalize zones to today's range: discard zones entirely in the past or
        // entirely in tomorrow, clip zones that started yesterday but extend into today.
        List<ResolvedZone> normalizedZones = [];
        foreach (ResolvedZone zone in zones)
        {
            if (zone.EndMinutes <= 0)
                continue; // entirely yesterday — discard
            if (zone.StartMinutes >= 1440)
                continue; // entirely tomorrow — discard

            int clippedStart = zone.StartMinutes < 0 ? 0 : zone.StartMinutes;
            normalizedZones.Add(new ResolvedZone
            {
                RuleId = zone.RuleId,
                StartMinutes = clippedStart,
                EndMinutes = zone.EndMinutes,
                Action = zone.Action,
                TargetPercent = zone.TargetPercent,
                IsSmart = zone.IsSmart,
                GraduatedTarget = zone.GraduatedTarget
            });
        }

        return normalizedZones;
    }

    public static List<ResolvedZone> ResolveAllZones(BatteryZoneRules rules, List<PricingSlot> slots)
    {
        List<ResolvedZone> allZones = [];
        foreach (BatteryZoneRule rule in rules.Rules)
        {
            List<ResolvedZone> ruleZones = EvaluateZoneRule(rule, slots);
            allZones.AddRange(ruleZones);
        }
        return allZones.OrderBy(z => z.StartMinutes).ToList();
    }
}

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
}

public static class BatteryZoneResolver
{
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

            default:
                return [];
        }
    }

    public static List<ResolvedZone> EvaluateZoneRule(BatteryZoneRule rule, List<PricingSlot> slots)
    {
        List<int> startTimes = ResolveTimeDefinition(rule.StartTime, slots);
        startTimes.Sort();

        List<int> endTimes = ResolveTimeDefinition(rule.EndTime, slots);
        endTimes.Sort();

        if (startTimes.Count == 0 || endTimes.Count == 0) return [];

        // Greedy pairing: for each start, find the nearest forward end (each end used once)
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
                    TargetPercent = rule.TargetPercent
                });
            }
        }

        // Second pass: wrap around midnight for unmatched starts
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
                int actualEnd = endTimes[bestEndIndex];

                // Evening segment: start → midnight
                zones.Add(new ResolvedZone
                {
                    RuleId = rule.Id,
                    StartMinutes = start,
                    EndMinutes = 1440,
                    Action = rule.Action,
                    TargetPercent = rule.TargetPercent
                });

                // Morning segment: midnight → end
                if (actualEnd > 0)
                {
                    zones.Add(new ResolvedZone
                    {
                        RuleId = rule.Id,
                        StartMinutes = 0,
                        EndMinutes = actualEnd,
                        Action = rule.Action,
                        TargetPercent = rule.TargetPercent
                    });
                }
            }
        }

        return zones;
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

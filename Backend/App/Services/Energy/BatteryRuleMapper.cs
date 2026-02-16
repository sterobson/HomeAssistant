using HomeAssistant.Shared.Energy;
using System.Collections.Generic;

namespace HomeAssistant.Services.Energy;

/// <summary>
/// Maps between domain models and DTOs for battery zone rules
/// </summary>
public static class BatteryRuleMapper
{
    private static readonly Dictionary<string, TimeDefinitionType> _typeMap = new()
    {
        { "fixed-time", TimeDefinitionType.FixedTime },
        { "export-exceeds-import", TimeDefinitionType.ExportExceedsImport },
        { "import-exceeds-export", TimeDefinitionType.ImportExceedsExport }
    };

    private static readonly Dictionary<TimeDefinitionType, string> _reverseTypeMap = new()
    {
        { TimeDefinitionType.FixedTime, "fixed-time" },
        { TimeDefinitionType.ExportExceedsImport, "export-exceeds-import" },
        { TimeDefinitionType.ImportExceedsExport, "import-exceeds-export" },
        { TimeDefinitionType.StartOfCheapImport, "local-extrema-boundary" },
        { TimeDefinitionType.EndOfCheapImport, "local-extrema-boundary" },
        { TimeDefinitionType.StartOfExpensiveImport, "local-extrema-boundary" },
        { TimeDefinitionType.EndOfExpensiveImport, "local-extrema-boundary" },
        { TimeDefinitionType.StartOfCheapExport, "local-extrema-boundary" },
        { TimeDefinitionType.EndOfCheapExport, "local-extrema-boundary" },
        { TimeDefinitionType.StartOfExpensiveExport, "local-extrema-boundary" },
        { TimeDefinitionType.EndOfExpensiveExport, "local-extrema-boundary" }
    };

    private static readonly Dictionary<string, BatteryZoneAction> _actionMap = new()
    {
        { "import", BatteryZoneAction.Import },
        { "export", BatteryZoneAction.Export }
    };

    private static readonly Dictionary<BatteryZoneAction, string> _reverseActionMap = new()
    {
        { BatteryZoneAction.Import, "import" },
        { BatteryZoneAction.Export, "export" }
    };

    public static BatteryZoneRules MapFromDto(BatteryZoneRulesDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        BatteryZoneRules result = new();

        foreach (BatteryZoneRuleDto ruleDto in dto.Rules)
        {
            BatteryZoneRule rule = new()
            {
                Id = ruleDto.Id,
                StartTime = MapTimeDefinitionFromDto(ruleDto.StartTime),
                EndTime = MapTimeDefinitionFromDto(ruleDto.EndTime),
                Action = _actionMap.GetValueOrDefault(ruleDto.Action, BatteryZoneAction.Import),
                TargetPercent = ruleDto.TargetPercent,
                GraduatedTarget = !string.Equals(ruleDto.RateMode, "fixed", StringComparison.OrdinalIgnoreCase)
            };

            result.Rules.Add(rule);
        }

        return result;
    }

    public static BatteryZoneRulesDto MapToDto(BatteryZoneRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        BatteryZoneRulesDto result = new();

        foreach (BatteryZoneRule rule in rules.Rules)
        {
            BatteryZoneRuleDto ruleDto = new()
            {
                Id = rule.Id,
                StartTime = MapTimeDefinitionToDto(rule.StartTime),
                EndTime = MapTimeDefinitionToDto(rule.EndTime),
                Action = _reverseActionMap.GetValueOrDefault(rule.Action, "import"),
                TargetPercent = rule.TargetPercent,
                RateMode = rule.GraduatedTarget ? "by-end" : "fixed"
            };

            result.Rules.Add(ruleDto);
        }

        return result;
    }

    private static TimeDefinition MapTimeDefinitionFromDto(TimeDefinitionDto dto)
    {
        TimeDefinitionType type;

        if (dto.Type == "local-extrema-boundary")
        {
            type = (dto.PriceType, dto.RegionType, dto.ExtremaType) switch
            {
                ("import", "minima", "start") => TimeDefinitionType.StartOfCheapImport,
                ("import", "minima", "end") => TimeDefinitionType.EndOfCheapImport,
                ("import", "maxima", "start") => TimeDefinitionType.StartOfExpensiveImport,
                ("import", "maxima", "end") => TimeDefinitionType.EndOfExpensiveImport,
                ("export", "minima", "start") => TimeDefinitionType.StartOfCheapExport,
                ("export", "minima", "end") => TimeDefinitionType.EndOfCheapExport,
                ("export", "maxima", "start") => TimeDefinitionType.StartOfExpensiveExport,
                ("export", "maxima", "end") => TimeDefinitionType.EndOfExpensiveExport,
                _ => TimeDefinitionType.FixedTime
            };
        }
        else
        {
            type = _typeMap.GetValueOrDefault(dto.Type, TimeDefinitionType.FixedTime);
        }

        return new TimeDefinition
        {
            Type = type,
            FixedTimeMinutes = dto.FixedTimeMinutes,
            PriceType = dto.PriceType,
            ExtremaType = dto.ExtremaType,
            RegionType = dto.RegionType,
            ThresholdType = dto.ThresholdType,
            ThresholdValue = dto.ThresholdValue
        };
    }

    private static TimeDefinitionDto MapTimeDefinitionToDto(TimeDefinition def)
    {
        string type = _reverseTypeMap.GetValueOrDefault(def.Type, "fixed-time");

        // Populate subfields for local-extrema-boundary types
        string? regionType = def.RegionType;
        string? extremaType = def.ExtremaType;
        string? priceType = def.PriceType;

        if (type == "local-extrema-boundary")
        {
            (regionType, extremaType, priceType) = def.Type switch
            {
                TimeDefinitionType.StartOfCheapImport => ("minima", "start", "import"),
                TimeDefinitionType.EndOfCheapImport => ("minima", "end", "import"),
                TimeDefinitionType.StartOfExpensiveImport => ("maxima", "start", "import"),
                TimeDefinitionType.EndOfExpensiveImport => ("maxima", "end", "import"),
                TimeDefinitionType.StartOfCheapExport => ("minima", "start", "export"),
                TimeDefinitionType.EndOfCheapExport => ("minima", "end", "export"),
                TimeDefinitionType.StartOfExpensiveExport => ("maxima", "start", "export"),
                TimeDefinitionType.EndOfExpensiveExport => ("maxima", "end", "export"),
                _ => (regionType, extremaType, priceType)
            };
        }

        return new TimeDefinitionDto
        {
            Type = type,
            FixedTimeMinutes = def.FixedTimeMinutes,
            PriceType = priceType,
            ExtremaType = extremaType,
            RegionType = regionType,
            ThresholdType = def.ThresholdType,
            ThresholdValue = def.ThresholdValue
        };
    }
}

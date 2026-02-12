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
        { "start-of-cheap-import:import", TimeDefinitionType.StartOfCheapImport },
        { "end-of-cheap-import:import", TimeDefinitionType.EndOfCheapImport },
        { "start-of-expensive-import:import", TimeDefinitionType.StartOfExpensiveImport },
        { "end-of-expensive-import:import", TimeDefinitionType.EndOfExpensiveImport },
        { "export-exceeds-import:export", TimeDefinitionType.ExportExceedsImport },
        { "import-exceeds-export:import", TimeDefinitionType.ImportExceedsExport }
    };

    private static readonly Dictionary<TimeDefinitionType, string> _reverseTypeMap = new()
    {
        { TimeDefinitionType.FixedTime, "fixed-time" },
        { TimeDefinitionType.StartOfCheapImport, "start-of-cheap-import:import" },
        { TimeDefinitionType.EndOfCheapImport, "end-of-cheap-import:import" },
        { TimeDefinitionType.StartOfExpensiveImport, "start-of-expensive-import:import" },
        { TimeDefinitionType.EndOfExpensiveImport, "end-of-expensive-import:import" },
        { TimeDefinitionType.ExportExceedsImport, "export-exceeds-import:export" },
        { TimeDefinitionType.ImportExceedsExport, "import-exceeds-export:import" }
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
                TargetPercent = ruleDto.TargetPercent
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
                TargetPercent = rule.TargetPercent
            };

            result.Rules.Add(ruleDto);
        }

        return result;
    }

    private static TimeDefinition MapTimeDefinitionFromDto(TimeDefinitionDto dto)
    {
        return new TimeDefinition
        {
            Type = _typeMap.GetValueOrDefault(dto.Type, TimeDefinitionType.FixedTime),
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
        return new TimeDefinitionDto
        {
            Type = _reverseTypeMap.GetValueOrDefault(def.Type, "fixed-time"),
            FixedTimeMinutes = def.FixedTimeMinutes,
            PriceType = def.PriceType,
            ExtremaType = def.ExtremaType,
            RegionType = def.RegionType,
            ThresholdType = def.ThresholdType,
            ThresholdValue = def.ThresholdValue
        };
    }
}

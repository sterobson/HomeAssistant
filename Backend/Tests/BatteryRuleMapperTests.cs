using HomeAssistant.Services.Energy;
using HomeAssistant.Shared.Energy;
using Shouldly;

namespace HomeAssistant.Tests;

[TestClass]
public sealed class BatteryRuleMapperTests
{
    // ========================================================================
    // MapFromDto - import price extrema types
    // ========================================================================

    [TestMethod]
    public void MapFromDto_StartOfCheapImport_MapsCorrectly()
    {
        BatteryZoneRulesDto dto = CreateDtoWithTimeDef(
            "local-extrema-boundary", regionType: "minima", extremaType: "start", priceType: "import");

        BatteryZoneRules result = BatteryRuleMapper.MapFromDto(dto);

        result.Rules[0].StartTime.Type.ShouldBe(TimeDefinitionType.StartOfCheapImport);
    }

    [TestMethod]
    public void MapFromDto_EndOfCheapImport_MapsCorrectly()
    {
        BatteryZoneRulesDto dto = CreateDtoWithTimeDef(
            "local-extrema-boundary", regionType: "minima", extremaType: "end", priceType: "import");

        BatteryZoneRules result = BatteryRuleMapper.MapFromDto(dto);

        result.Rules[0].StartTime.Type.ShouldBe(TimeDefinitionType.EndOfCheapImport);
    }

    [TestMethod]
    public void MapFromDto_StartOfExpensiveImport_MapsCorrectly()
    {
        BatteryZoneRulesDto dto = CreateDtoWithTimeDef(
            "local-extrema-boundary", regionType: "maxima", extremaType: "start", priceType: "import");

        BatteryZoneRules result = BatteryRuleMapper.MapFromDto(dto);

        result.Rules[0].StartTime.Type.ShouldBe(TimeDefinitionType.StartOfExpensiveImport);
    }

    [TestMethod]
    public void MapFromDto_EndOfExpensiveImport_MapsCorrectly()
    {
        BatteryZoneRulesDto dto = CreateDtoWithTimeDef(
            "local-extrema-boundary", regionType: "maxima", extremaType: "end", priceType: "import");

        BatteryZoneRules result = BatteryRuleMapper.MapFromDto(dto);

        result.Rules[0].StartTime.Type.ShouldBe(TimeDefinitionType.EndOfExpensiveImport);
    }

    // ========================================================================
    // MapFromDto - export price extrema types
    // ========================================================================

    [TestMethod]
    public void MapFromDto_StartOfCheapExport_MapsCorrectly()
    {
        BatteryZoneRulesDto dto = CreateDtoWithTimeDef(
            "local-extrema-boundary", regionType: "minima", extremaType: "start", priceType: "export");

        BatteryZoneRules result = BatteryRuleMapper.MapFromDto(dto);

        result.Rules[0].StartTime.Type.ShouldBe(TimeDefinitionType.StartOfCheapExport);
    }

    [TestMethod]
    public void MapFromDto_EndOfCheapExport_MapsCorrectly()
    {
        BatteryZoneRulesDto dto = CreateDtoWithTimeDef(
            "local-extrema-boundary", regionType: "minima", extremaType: "end", priceType: "export");

        BatteryZoneRules result = BatteryRuleMapper.MapFromDto(dto);

        result.Rules[0].StartTime.Type.ShouldBe(TimeDefinitionType.EndOfCheapExport);
    }

    [TestMethod]
    public void MapFromDto_StartOfExpensiveExport_MapsCorrectly()
    {
        BatteryZoneRulesDto dto = CreateDtoWithTimeDef(
            "local-extrema-boundary", regionType: "maxima", extremaType: "start", priceType: "export");

        BatteryZoneRules result = BatteryRuleMapper.MapFromDto(dto);

        result.Rules[0].StartTime.Type.ShouldBe(TimeDefinitionType.StartOfExpensiveExport);
    }

    [TestMethod]
    public void MapFromDto_EndOfExpensiveExport_MapsCorrectly()
    {
        BatteryZoneRulesDto dto = CreateDtoWithTimeDef(
            "local-extrema-boundary", regionType: "maxima", extremaType: "end", priceType: "export");

        BatteryZoneRules result = BatteryRuleMapper.MapFromDto(dto);

        result.Rules[0].StartTime.Type.ShouldBe(TimeDefinitionType.EndOfExpensiveExport);
    }

    // ========================================================================
    // MapFromDto - crossover types
    // ========================================================================

    [TestMethod]
    public void MapFromDto_ExportExceedsImport_MapsCorrectly()
    {
        BatteryZoneRulesDto dto = CreateDtoWithTimeDef("export-exceeds-import");

        BatteryZoneRules result = BatteryRuleMapper.MapFromDto(dto);

        result.Rules[0].StartTime.Type.ShouldBe(TimeDefinitionType.ExportExceedsImport);
    }

    [TestMethod]
    public void MapFromDto_ImportExceedsExport_MapsCorrectly()
    {
        BatteryZoneRulesDto dto = CreateDtoWithTimeDef("import-exceeds-export");

        BatteryZoneRules result = BatteryRuleMapper.MapFromDto(dto);

        result.Rules[0].StartTime.Type.ShouldBe(TimeDefinitionType.ImportExceedsExport);
    }

    // ========================================================================
    // MapFromDto - unknown/fallback
    // ========================================================================

    [TestMethod]
    public void MapFromDto_UnknownType_FallsBackToFixedTime()
    {
        BatteryZoneRulesDto dto = CreateDtoWithTimeDef("some-unknown-type");

        BatteryZoneRules result = BatteryRuleMapper.MapFromDto(dto);

        result.Rules[0].StartTime.Type.ShouldBe(TimeDefinitionType.FixedTime);
    }

    [TestMethod]
    public void MapFromDto_LocalExtremaBoundaryWithMissingSubfields_FallsBackToFixedTime()
    {
        BatteryZoneRulesDto dto = CreateDtoWithTimeDef(
            "local-extrema-boundary", regionType: null, extremaType: null, priceType: null);

        BatteryZoneRules result = BatteryRuleMapper.MapFromDto(dto);

        result.Rules[0].StartTime.Type.ShouldBe(TimeDefinitionType.FixedTime);
    }

    // ========================================================================
    // MapFromDto - RateMode / GraduatedTarget
    // ========================================================================

    [TestMethod]
    public void MapFromDto_RateModeByEnd_GraduatedTargetTrue()
    {
        BatteryZoneRulesDto dto = CreateDtoWithTimeDef("fixed-time", fixedTimeMinutes: 0);
        dto.Rules[0].RateMode = "by-end";

        BatteryZoneRules result = BatteryRuleMapper.MapFromDto(dto);

        result.Rules[0].GraduatedTarget.ShouldBeTrue();
    }

    [TestMethod]
    public void MapFromDto_RateModeFixed_GraduatedTargetFalse()
    {
        BatteryZoneRulesDto dto = CreateDtoWithTimeDef("fixed-time", fixedTimeMinutes: 0);
        dto.Rules[0].RateMode = "fixed";

        BatteryZoneRules result = BatteryRuleMapper.MapFromDto(dto);

        result.Rules[0].GraduatedTarget.ShouldBeFalse();
    }

    [TestMethod]
    public void MapFromDto_RateModeNull_GraduatedTargetTrue()
    {
        BatteryZoneRulesDto dto = CreateDtoWithTimeDef("fixed-time", fixedTimeMinutes: 0);
        dto.Rules[0].RateMode = null;

        BatteryZoneRules result = BatteryRuleMapper.MapFromDto(dto);

        // null RateMode defaults to graduated (by-end) behaviour
        result.Rules[0].GraduatedTarget.ShouldBeTrue();
    }

    // ========================================================================
    // MapToDto - reverse mapping
    // ========================================================================

    [TestMethod]
    public void MapToDto_StartOfCheapImport_ProducesCorrectFrontendFormat()
    {
        BatteryZoneRules rules = CreateRulesWithTimeDef(TimeDefinitionType.StartOfCheapImport);

        BatteryZoneRulesDto dto = BatteryRuleMapper.MapToDto(rules);

        TimeDefinitionDto timeDef = dto.Rules[0].StartTime;
        timeDef.Type.ShouldBe("local-extrema-boundary");
        timeDef.RegionType.ShouldBe("minima");
        timeDef.ExtremaType.ShouldBe("start");
        timeDef.PriceType.ShouldBe("import");
    }

    [TestMethod]
    public void MapToDto_StartOfExpensiveExport_ProducesCorrectFrontendFormat()
    {
        BatteryZoneRules rules = CreateRulesWithTimeDef(TimeDefinitionType.StartOfExpensiveExport);

        BatteryZoneRulesDto dto = BatteryRuleMapper.MapToDto(rules);

        TimeDefinitionDto timeDef = dto.Rules[0].StartTime;
        timeDef.Type.ShouldBe("local-extrema-boundary");
        timeDef.RegionType.ShouldBe("maxima");
        timeDef.ExtremaType.ShouldBe("start");
        timeDef.PriceType.ShouldBe("export");
    }

    [TestMethod]
    public void MapToDto_GraduatedTargetTrue_RateModeByEnd()
    {
        BatteryZoneRules rules = CreateRulesWithTimeDef(TimeDefinitionType.FixedTime);
        rules.Rules[0].GraduatedTarget = true;

        BatteryZoneRulesDto dto = BatteryRuleMapper.MapToDto(rules);

        dto.Rules[0].RateMode.ShouldBe("by-end");
    }

    [TestMethod]
    public void MapToDto_GraduatedTargetFalse_RateModeFixed()
    {
        BatteryZoneRules rules = CreateRulesWithTimeDef(TimeDefinitionType.FixedTime);
        rules.Rules[0].GraduatedTarget = false;

        BatteryZoneRulesDto dto = BatteryRuleMapper.MapToDto(rules);

        dto.Rules[0].RateMode.ShouldBe("fixed");
    }

    // ========================================================================
    // Round-trip
    // ========================================================================

    [TestMethod]
    public void MapFromDto_RoundTrip_PreservesImportExtremaBoundary()
    {
        BatteryZoneRulesDto originalDto = CreateDtoWithTimeDef(
            "local-extrema-boundary", regionType: "minima", extremaType: "start", priceType: "import",
            fixedTimeMinutes: 1410);

        BatteryZoneRules rules = BatteryRuleMapper.MapFromDto(originalDto);
        BatteryZoneRulesDto roundTripped = BatteryRuleMapper.MapToDto(rules);

        TimeDefinitionDto timeDef = roundTripped.Rules[0].StartTime;
        timeDef.Type.ShouldBe("local-extrema-boundary");
        timeDef.RegionType.ShouldBe("minima");
        timeDef.ExtremaType.ShouldBe("start");
        timeDef.PriceType.ShouldBe("import");
        timeDef.FixedTimeMinutes.ShouldBe(1410);
    }

    [TestMethod]
    public void MapFromDto_RoundTrip_PreservesExportExtremaBoundary()
    {
        BatteryZoneRulesDto originalDto = CreateDtoWithTimeDef(
            "local-extrema-boundary", regionType: "maxima", extremaType: "end", priceType: "export",
            fixedTimeMinutes: 960);

        BatteryZoneRules rules = BatteryRuleMapper.MapFromDto(originalDto);
        BatteryZoneRulesDto roundTripped = BatteryRuleMapper.MapToDto(rules);

        TimeDefinitionDto timeDef = roundTripped.Rules[0].StartTime;
        timeDef.Type.ShouldBe("local-extrema-boundary");
        timeDef.RegionType.ShouldBe("maxima");
        timeDef.ExtremaType.ShouldBe("end");
        timeDef.PriceType.ShouldBe("export");
        timeDef.FixedTimeMinutes.ShouldBe(960);
    }

    // ========================================================================
    // Helpers
    // ========================================================================

    private static BatteryZoneRulesDto CreateDtoWithTimeDef(
        string type,
        string? regionType = null,
        string? extremaType = null,
        string? priceType = null,
        int? fixedTimeMinutes = null)
    {
        return new BatteryZoneRulesDto
        {
            Rules =
            [
                new BatteryZoneRuleDto
                {
                    Id = "test-1",
                    StartTime = new TimeDefinitionDto
                    {
                        Type = type,
                        RegionType = regionType,
                        ExtremaType = extremaType,
                        PriceType = priceType,
                        FixedTimeMinutes = fixedTimeMinutes
                    },
                    EndTime = new TimeDefinitionDto { Type = "fixed-time", FixedTimeMinutes = 330 },
                    Action = "import",
                    TargetPercent = 80
                }
            ]
        };
    }

    private static BatteryZoneRules CreateRulesWithTimeDef(TimeDefinitionType type)
    {
        BatteryZoneRules rules = new();
        rules.Rules.Add(new BatteryZoneRule
        {
            Id = "test-1",
            StartTime = new TimeDefinition { Type = type },
            EndTime = new TimeDefinition { Type = TimeDefinitionType.FixedTime, FixedTimeMinutes = 330 },
            Action = BatteryZoneAction.Import,
            TargetPercent = 80
        });
        return rules;
    }
}

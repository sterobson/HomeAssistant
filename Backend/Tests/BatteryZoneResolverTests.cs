using HomeAssistant.apps.Energy;
using HomeAssistant.Services.Energy;
using Shouldly;

namespace HomeAssistant.Tests;

[TestClass]
public sealed class BatteryZoneResolverTests
{
    // ========================================================================
    // ResolveTimeDefinition
    // ========================================================================

    [TestMethod]
    public void ResolveTimeDefinition_FixedTime_ReturnsFixedMinutes()
    {
        TimeDefinition def = new()
        {
            Type = TimeDefinitionType.FixedTime,
            FixedTimeMinutes = 120 // 02:00
        };

        List<int> result = BatteryZoneResolver.ResolveTimeDefinition(def, []);

        result.ShouldBe([120]);
    }

    [TestMethod]
    public void ResolveTimeDefinition_FixedTime_NullMinutes_ReturnsEmpty()
    {
        TimeDefinition def = new()
        {
            Type = TimeDefinitionType.FixedTime,
            FixedTimeMinutes = null
        };

        List<int> result = BatteryZoneResolver.ResolveTimeDefinition(def, []);

        result.ShouldBeEmpty();
    }

    [TestMethod]
    public void ResolveTimeDefinition_StartOfCheapImport_FindsMinimaBoundary()
    {
        // Price pattern: high, low, high - minima region starts at 120
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 30 },
            new() { TimeMinutes = 30, ImportPrice = 30 },
            new() { TimeMinutes = 60, ImportPrice = 30 },
            new() { TimeMinutes = 90, ImportPrice = 30 },
            new() { TimeMinutes = 120, ImportPrice = 10 },
            new() { TimeMinutes = 150, ImportPrice = 10 },
            new() { TimeMinutes = 180, ImportPrice = 30 },
            new() { TimeMinutes = 210, ImportPrice = 30 },
        ];

        TimeDefinition def = new() { Type = TimeDefinitionType.StartOfCheapImport };

        List<int> result = BatteryZoneResolver.ResolveTimeDefinition(def, slots);

        result.ShouldContain(120);
    }

    [TestMethod]
    public void ResolveTimeDefinition_EndOfCheapImport_FindsMinimaBoundaryEnd()
    {
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 30 },
            new() { TimeMinutes = 30, ImportPrice = 30 },
            new() { TimeMinutes = 60, ImportPrice = 30 },
            new() { TimeMinutes = 90, ImportPrice = 30 },
            new() { TimeMinutes = 120, ImportPrice = 10 },
            new() { TimeMinutes = 150, ImportPrice = 10 },
            new() { TimeMinutes = 180, ImportPrice = 30 },
            new() { TimeMinutes = 210, ImportPrice = 30 },
        ];

        TimeDefinition def = new() { Type = TimeDefinitionType.EndOfCheapImport };

        List<int> result = BatteryZoneResolver.ResolveTimeDefinition(def, slots);

        result.ShouldContain(180);
    }

    [TestMethod]
    public void ResolveTimeDefinition_StartOfExpensiveImport_FindsMaximaBoundary()
    {
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 10 },
            new() { TimeMinutes = 30, ImportPrice = 10 },
            new() { TimeMinutes = 60, ImportPrice = 10 },
            new() { TimeMinutes = 90, ImportPrice = 10 },
            new() { TimeMinutes = 120, ImportPrice = 40 },
            new() { TimeMinutes = 150, ImportPrice = 40 },
            new() { TimeMinutes = 180, ImportPrice = 10 },
            new() { TimeMinutes = 210, ImportPrice = 10 },
        ];

        TimeDefinition def = new() { Type = TimeDefinitionType.StartOfExpensiveImport };

        List<int> result = BatteryZoneResolver.ResolveTimeDefinition(def, slots);

        result.ShouldContain(120);
    }

    [TestMethod]
    public void ResolveTimeDefinition_EndOfExpensiveImport_FindsMaximaBoundaryEnd()
    {
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 10 },
            new() { TimeMinutes = 30, ImportPrice = 10 },
            new() { TimeMinutes = 60, ImportPrice = 10 },
            new() { TimeMinutes = 90, ImportPrice = 10 },
            new() { TimeMinutes = 120, ImportPrice = 40 },
            new() { TimeMinutes = 150, ImportPrice = 40 },
            new() { TimeMinutes = 180, ImportPrice = 10 },
            new() { TimeMinutes = 210, ImportPrice = 10 },
        ];

        TimeDefinition def = new() { Type = TimeDefinitionType.EndOfExpensiveImport };

        List<int> result = BatteryZoneResolver.ResolveTimeDefinition(def, slots);

        result.ShouldContain(180);
    }

    [TestMethod]
    public void ResolveTimeDefinition_ExportExceedsImport_FindsCrossover()
    {
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 30, ExportPrice = 10 },
            new() { TimeMinutes = 30, ImportPrice = 30, ExportPrice = 10 },
            new() { TimeMinutes = 60, ImportPrice = 10, ExportPrice = 20 }, // export exceeds import
            new() { TimeMinutes = 90, ImportPrice = 10, ExportPrice = 20 },
        ];

        TimeDefinition def = new() { Type = TimeDefinitionType.ExportExceedsImport };

        List<int> result = BatteryZoneResolver.ResolveTimeDefinition(def, slots);

        result.ShouldContain(60);
    }

    [TestMethod]
    public void ResolveTimeDefinition_ImportExceedsExport_FindsCrossover()
    {
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 10, ExportPrice = 20 },
            new() { TimeMinutes = 30, ImportPrice = 10, ExportPrice = 20 },
            new() { TimeMinutes = 60, ImportPrice = 30, ExportPrice = 10 }, // import exceeds export
            new() { TimeMinutes = 90, ImportPrice = 30, ExportPrice = 10 },
        ];

        TimeDefinition def = new() { Type = TimeDefinitionType.ImportExceedsExport };

        List<int> result = BatteryZoneResolver.ResolveTimeDefinition(def, slots);

        result.ShouldContain(60);
    }

    [TestMethod]
    public void ResolveTimeDefinition_NoCrossoverFound_ReturnsEmpty()
    {
        // Import always exceeds export, so ExportExceedsImport finds nothing
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 30, ExportPrice = 10 },
            new() { TimeMinutes = 30, ImportPrice = 30, ExportPrice = 10 },
            new() { TimeMinutes = 60, ImportPrice = 30, ExportPrice = 10 },
        ];

        TimeDefinition def = new() { Type = TimeDefinitionType.ExportExceedsImport };

        List<int> result = BatteryZoneResolver.ResolveTimeDefinition(def, slots);

        result.ShouldBeEmpty();
    }

    // ========================================================================
    // EvaluateZoneRule - Fixed time zones
    // ========================================================================

    [TestMethod]
    public void EvaluateZoneRule_FixedStartAndEnd_CreatesOneZone()
    {
        BatteryZoneRule rule = new()
        {
            Id = "rule-1",
            StartTime = new TimeDefinition { Type = TimeDefinitionType.FixedTime, FixedTimeMinutes = 60 },
            EndTime = new TimeDefinition { Type = TimeDefinitionType.FixedTime, FixedTimeMinutes = 240 },
            Action = BatteryZoneAction.Import,
            TargetPercent = 80
        };

        List<ResolvedZone> zones = BatteryZoneResolver.EvaluateZoneRule(rule, []);

        zones.Count.ShouldBe(1);
        zones[0].RuleId.ShouldBe("rule-1");
        zones[0].StartMinutes.ShouldBe(60);
        zones[0].EndMinutes.ShouldBe(240);
        zones[0].Action.ShouldBe(BatteryZoneAction.Import);
        zones[0].TargetPercent.ShouldBe(80);
    }

    [TestMethod]
    public void EvaluateZoneRule_ExportAction_SetsActionCorrectly()
    {
        BatteryZoneRule rule = new()
        {
            Id = "export-1",
            StartTime = new TimeDefinition { Type = TimeDefinitionType.FixedTime, FixedTimeMinutes = 960 }, // 16:00
            EndTime = new TimeDefinition { Type = TimeDefinitionType.FixedTime, FixedTimeMinutes = 1140 }, // 19:00
            Action = BatteryZoneAction.Export,
            TargetPercent = 20
        };

        List<ResolvedZone> zones = BatteryZoneResolver.EvaluateZoneRule(rule, []);

        zones.Count.ShouldBe(1);
        zones[0].Action.ShouldBe(BatteryZoneAction.Export);
        zones[0].TargetPercent.ShouldBe(20);
    }

    [TestMethod]
    public void EvaluateZoneRule_MissingStartTime_ReturnsNoZones()
    {
        BatteryZoneRule rule = new()
        {
            Id = "rule-1",
            StartTime = new TimeDefinition { Type = TimeDefinitionType.FixedTime, FixedTimeMinutes = null },
            EndTime = new TimeDefinition { Type = TimeDefinitionType.FixedTime, FixedTimeMinutes = 240 },
            Action = BatteryZoneAction.Import,
            TargetPercent = 80
        };

        List<ResolvedZone> zones = BatteryZoneResolver.EvaluateZoneRule(rule, []);

        zones.ShouldBeEmpty();
    }

    [TestMethod]
    public void EvaluateZoneRule_MissingEndTime_ReturnsNoZones()
    {
        BatteryZoneRule rule = new()
        {
            Id = "rule-1",
            StartTime = new TimeDefinition { Type = TimeDefinitionType.FixedTime, FixedTimeMinutes = 60 },
            EndTime = new TimeDefinition { Type = TimeDefinitionType.FixedTime, FixedTimeMinutes = null },
            Action = BatteryZoneAction.Import,
            TargetPercent = 80
        };

        List<ResolvedZone> zones = BatteryZoneResolver.EvaluateZoneRule(rule, []);

        zones.ShouldBeEmpty();
    }

    [TestMethod]
    public void EvaluateZoneRule_EndBeforeStart_WrapsAroundMidnight()
    {
        // 23:00 to 05:00 should wrap around midnight as a single zone
        BatteryZoneRule rule = new()
        {
            Id = "overnight",
            StartTime = new TimeDefinition { Type = TimeDefinitionType.FixedTime, FixedTimeMinutes = 1380 }, // 23:00
            EndTime = new TimeDefinition { Type = TimeDefinitionType.FixedTime, FixedTimeMinutes = 300 }, // 05:00
            Action = BatteryZoneAction.Import,
            TargetPercent = 100
        };

        List<ResolvedZone> zones = BatteryZoneResolver.EvaluateZoneRule(rule, []);

        // Single zone with EndMinutes > 1440
        zones.Count.ShouldBe(1);
        zones[0].StartMinutes.ShouldBe(1380);
        zones[0].EndMinutes.ShouldBe(1740); // 300 + 1440
    }

    [TestMethod]
    public void EvaluateZoneRule_StartEqualsEnd_WrapsAroundFullDay()
    {
        // When start == end, the greedy pairing can't match forward,
        // so midnight wrap creates a single zone spanning the full day
        BatteryZoneRule rule = new()
        {
            Id = "wrap",
            StartTime = new TimeDefinition { Type = TimeDefinitionType.FixedTime, FixedTimeMinutes = 120 },
            EndTime = new TimeDefinition { Type = TimeDefinitionType.FixedTime, FixedTimeMinutes = 120 },
            Action = BatteryZoneAction.Import,
            TargetPercent = 80
        };

        List<ResolvedZone> zones = BatteryZoneResolver.EvaluateZoneRule(rule, []);

        // Single zone: 02:00 → 02:00 next day (120 + 1440 = 1560)
        zones.Count.ShouldBe(1);
        zones[0].StartMinutes.ShouldBe(120);
        zones[0].EndMinutes.ShouldBe(1560);
    }

    // ========================================================================
    // IsMinuteInZone
    // ========================================================================

    [TestMethod]
    // Normal zone (120-300)
    [DataRow(120, 300, 120, true, "Normal: at start")]
    [DataRow(120, 300, 200, true, "Normal: middle")]
    [DataRow(120, 300, 299, true, "Normal: before end")]
    [DataRow(120, 300, 300, false, "Normal: at end (exclusive)")]
    [DataRow(120, 300, 119, false, "Normal: before start")]
    // Wrapped zone (1410-1770)
    [DataRow(1410, 1770, 1410, true, "Wrapped: at start")]
    [DataRow(1410, 1770, 1430, true, "Wrapped: evening")]
    [DataRow(1410, 1770, 0, true, "Wrapped: midnight")]
    [DataRow(1410, 1770, 200, true, "Wrapped: morning")]
    [DataRow(1410, 1770, 330, false, "Wrapped: at end (exclusive, 1770-1440=330)")]
    [DataRow(1410, 1770, 1400, false, "Wrapped: before start")]
    [DataRow(1410, 1770, 400, false, "Wrapped: after end")]
    public void IsMinuteInZone_ReturnsExpected(int start, int end, int current, bool expected, string reason)
    {
        bool result = BatteryZoneResolver.IsMinuteInZone(current, start, end);
        result.ShouldBe(expected, reason);
    }

    // ========================================================================
    // GetElapsedMinutes
    // ========================================================================

    [TestMethod]
    // Normal zone (120-300)
    [DataRow(120, 300, 120, 0, "Normal: at start")]
    [DataRow(120, 300, 200, 80, "Normal: 80 min in")]
    // Wrapped zone (1410-1770)
    [DataRow(1410, 1770, 1410, 0, "Wrapped: at start")]
    [DataRow(1410, 1770, 1430, 20, "Wrapped: evening 20 min")]
    [DataRow(1410, 1770, 0, 30, "Wrapped: midnight")]
    [DataRow(1410, 1770, 100, 130, "Wrapped: morning 100 min")]
    [DataRow(1410, 1770, 329, 359, "Wrapped: near end")]
    public void GetElapsedMinutes_ReturnsExpected(int start, int end, int current, int expected, string reason)
    {
        int result = BatteryZoneResolver.GetElapsedMinutes(current, start, end);
        result.ShouldBe(expected, reason);
    }

    // ========================================================================
    // EvaluateZoneRule - IsSmart propagation
    // ========================================================================

    [TestMethod]
    public void EvaluateZoneRule_BothFixed_IsSmartFalse()
    {
        BatteryZoneRule rule = new()
        {
            Id = "fixed-fixed",
            StartTime = new TimeDefinition { Type = TimeDefinitionType.FixedTime, FixedTimeMinutes = 60 },
            EndTime = new TimeDefinition { Type = TimeDefinitionType.FixedTime, FixedTimeMinutes = 240 },
            Action = BatteryZoneAction.Import,
            TargetPercent = 80
        };

        List<ResolvedZone> zones = BatteryZoneResolver.EvaluateZoneRule(rule, []);

        zones.Count.ShouldBe(1);
        zones[0].IsSmart.ShouldBeFalse();
    }

    [TestMethod]
    public void EvaluateZoneRule_SmartStartFixedEnd_IsSmartTrue()
    {
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 30 },
            new() { TimeMinutes = 30, ImportPrice = 30 },
            new() { TimeMinutes = 60, ImportPrice = 30 },
            new() { TimeMinutes = 90, ImportPrice = 5 },
            new() { TimeMinutes = 120, ImportPrice = 5 },
            new() { TimeMinutes = 150, ImportPrice = 30 },
            new() { TimeMinutes = 180, ImportPrice = 30 },
        ];

        BatteryZoneRule rule = new()
        {
            Id = "smart-fixed",
            StartTime = new TimeDefinition { Type = TimeDefinitionType.StartOfCheapImport },
            EndTime = new TimeDefinition { Type = TimeDefinitionType.FixedTime, FixedTimeMinutes = 180 },
            Action = BatteryZoneAction.Import,
            TargetPercent = 100
        };

        List<ResolvedZone> zones = BatteryZoneResolver.EvaluateZoneRule(rule, slots);

        zones.Count.ShouldBe(1);
        zones[0].IsSmart.ShouldBeTrue();
    }

    [TestMethod]
    public void EvaluateZoneRule_FixedStartSmartEnd_IsSmartTrue()
    {
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 30 },
            new() { TimeMinutes = 30, ImportPrice = 30 },
            new() { TimeMinutes = 60, ImportPrice = 30 },
            new() { TimeMinutes = 90, ImportPrice = 5 },
            new() { TimeMinutes = 120, ImportPrice = 5 },
            new() { TimeMinutes = 150, ImportPrice = 30 },
            new() { TimeMinutes = 180, ImportPrice = 30 },
        ];

        BatteryZoneRule rule = new()
        {
            Id = "fixed-smart",
            StartTime = new TimeDefinition { Type = TimeDefinitionType.FixedTime, FixedTimeMinutes = 0 },
            EndTime = new TimeDefinition { Type = TimeDefinitionType.EndOfCheapImport },
            Action = BatteryZoneAction.Import,
            TargetPercent = 100
        };

        List<ResolvedZone> zones = BatteryZoneResolver.EvaluateZoneRule(rule, slots);

        zones.Count.ShouldBe(1);
        zones[0].IsSmart.ShouldBeTrue();
    }

    [TestMethod]
    public void EvaluateZoneRule_BothSmart_IsSmartTrue()
    {
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 30 },
            new() { TimeMinutes = 30, ImportPrice = 30 },
            new() { TimeMinutes = 60, ImportPrice = 30 },
            new() { TimeMinutes = 90, ImportPrice = 5 },
            new() { TimeMinutes = 120, ImportPrice = 5 },
            new() { TimeMinutes = 150, ImportPrice = 30 },
            new() { TimeMinutes = 180, ImportPrice = 30 },
        ];

        BatteryZoneRule rule = new()
        {
            Id = "smart-smart",
            StartTime = new TimeDefinition { Type = TimeDefinitionType.StartOfCheapImport },
            EndTime = new TimeDefinition { Type = TimeDefinitionType.EndOfCheapImport },
            Action = BatteryZoneAction.Import,
            TargetPercent = 100
        };

        List<ResolvedZone> zones = BatteryZoneResolver.EvaluateZoneRule(rule, slots);

        zones.Count.ShouldBe(1);
        zones[0].IsSmart.ShouldBeTrue();
    }

    // ========================================================================
    // EvaluateZoneRule - Price-driven zones
    // ========================================================================

    [TestMethod]
    public void EvaluateZoneRule_CheapImportWindow_ChargesDuringTrough()
    {
        // FindMinimaRegionBoundaries finds the flat trough where price < both neighbours.
        // The boundary starts at the first slot of the trough and ends at the first slot after.
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 25 },
            new() { TimeMinutes = 30, ImportPrice = 25 },
            new() { TimeMinutes = 60, ImportPrice = 25 },
            new() { TimeMinutes = 90, ImportPrice = 5 },   // trough (lower than 60 and 150)
            new() { TimeMinutes = 120, ImportPrice = 5 },
            new() { TimeMinutes = 150, ImportPrice = 25 },
            new() { TimeMinutes = 180, ImportPrice = 25 },
            new() { TimeMinutes = 210, ImportPrice = 25 },
        ];

        BatteryZoneRule rule = new()
        {
            Id = "cheap-charge",
            StartTime = new TimeDefinition { Type = TimeDefinitionType.StartOfCheapImport },
            EndTime = new TimeDefinition { Type = TimeDefinitionType.EndOfCheapImport },
            Action = BatteryZoneAction.Import,
            TargetPercent = 100
        };

        List<ResolvedZone> zones = BatteryZoneResolver.EvaluateZoneRule(rule, slots);

        zones.Count.ShouldBe(1);
        zones[0].StartMinutes.ShouldBe(90);
        zones[0].EndMinutes.ShouldBe(150);
        zones[0].Action.ShouldBe(BatteryZoneAction.Import);
    }

    [TestMethod]
    public void EvaluateZoneRule_ExpensiveImportWindow_DischargesDuringPeak()
    {
        // FindMaximaRegionBoundaries finds the flat peak where price > both neighbours.
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 20 },
            new() { TimeMinutes = 30, ImportPrice = 20 },
            new() { TimeMinutes = 60, ImportPrice = 20 },
            new() { TimeMinutes = 90, ImportPrice = 20 },
            new() { TimeMinutes = 120, ImportPrice = 50 }, // flat peak (higher than 90 and 180)
            new() { TimeMinutes = 150, ImportPrice = 50 },
            new() { TimeMinutes = 180, ImportPrice = 20 },
            new() { TimeMinutes = 210, ImportPrice = 20 },
            new() { TimeMinutes = 240, ImportPrice = 20 },
        ];

        BatteryZoneRule rule = new()
        {
            Id = "peak-discharge",
            StartTime = new TimeDefinition { Type = TimeDefinitionType.StartOfExpensiveImport },
            EndTime = new TimeDefinition { Type = TimeDefinitionType.EndOfExpensiveImport },
            Action = BatteryZoneAction.Export,
            TargetPercent = 10
        };

        List<ResolvedZone> zones = BatteryZoneResolver.EvaluateZoneRule(rule, slots);

        zones.Count.ShouldBe(1);
        zones[0].StartMinutes.ShouldBe(120);
        zones[0].EndMinutes.ShouldBe(180);
        zones[0].Action.ShouldBe(BatteryZoneAction.Export);
    }

    // ========================================================================
    // ResolveAllZones
    // ========================================================================

    [TestMethod]
    public void ResolveAllZones_MultipleRules_ReturnsAllZonesOrderedByStart()
    {
        BatteryZoneRules rules = new()
        {
            Rules =
            [
                new BatteryZoneRule
                {
                    Id = "afternoon-export",
                    StartTime = new TimeDefinition { Type = TimeDefinitionType.FixedTime, FixedTimeMinutes = 960 }, // 16:00
                    EndTime = new TimeDefinition { Type = TimeDefinitionType.FixedTime, FixedTimeMinutes = 1140 },  // 19:00
                    Action = BatteryZoneAction.Export,
                    TargetPercent = 20
                },
                new BatteryZoneRule
                {
                    Id = "overnight-charge",
                    StartTime = new TimeDefinition { Type = TimeDefinitionType.FixedTime, FixedTimeMinutes = 30 },  // 00:30
                    EndTime = new TimeDefinition { Type = TimeDefinitionType.FixedTime, FixedTimeMinutes = 270 },   // 04:30
                    Action = BatteryZoneAction.Import,
                    TargetPercent = 100
                }
            ]
        };

        List<ResolvedZone> zones = BatteryZoneResolver.ResolveAllZones(rules, []);

        zones.Count.ShouldBe(2);
        zones[0].RuleId.ShouldBe("overnight-charge");
        zones[0].StartMinutes.ShouldBe(30);
        zones[1].RuleId.ShouldBe("afternoon-export");
        zones[1].StartMinutes.ShouldBe(960);
    }

    [TestMethod]
    public void ResolveAllZones_NoRules_ReturnsEmpty()
    {
        BatteryZoneRules rules = new() { Rules = [] };

        List<ResolvedZone> zones = BatteryZoneResolver.ResolveAllZones(rules, []);

        zones.ShouldBeEmpty();
    }

    [TestMethod]
    public void ResolveAllZones_RuleWithNoMatchingPriceData_SkipsRule()
    {
        // Flat pricing - no minima/maxima regions
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 20 },
            new() { TimeMinutes = 30, ImportPrice = 20 },
            new() { TimeMinutes = 60, ImportPrice = 20 },
            new() { TimeMinutes = 90, ImportPrice = 20 },
        ];

        BatteryZoneRules rules = new()
        {
            Rules =
            [
                new BatteryZoneRule
                {
                    Id = "cheap-charge",
                    StartTime = new TimeDefinition { Type = TimeDefinitionType.StartOfCheapImport },
                    EndTime = new TimeDefinition { Type = TimeDefinitionType.EndOfCheapImport },
                    Action = BatteryZoneAction.Import,
                    TargetPercent = 100
                },
                new BatteryZoneRule
                {
                    Id = "fixed-fallback",
                    StartTime = new TimeDefinition { Type = TimeDefinitionType.FixedTime, FixedTimeMinutes = 0 },
                    EndTime = new TimeDefinition { Type = TimeDefinitionType.FixedTime, FixedTimeMinutes = 240 },
                    Action = BatteryZoneAction.Import,
                    TargetPercent = 80
                }
            ]
        };

        List<ResolvedZone> zones = BatteryZoneResolver.ResolveAllZones(rules, slots);

        zones.Count.ShouldBe(1);
        zones[0].RuleId.ShouldBe("fixed-fallback");
    }

    // ========================================================================
    // Typical day scenarios
    // ========================================================================

    [TestMethod]
    public void TypicalFluxDay_ChargesOvernightAndDischargesDuringPeak()
    {
        // Octopus Flux-style: cheap 02:00-05:00, expensive 16:00-19:00
        List<PricingSlot> slots = BuildFluxDaySlots();

        BatteryZoneRules rules = new()
        {
            Rules =
            [
                new BatteryZoneRule
                {
                    Id = "cheap-charge",
                    StartTime = new TimeDefinition { Type = TimeDefinitionType.StartOfCheapImport },
                    EndTime = new TimeDefinition { Type = TimeDefinitionType.EndOfCheapImport },
                    Action = BatteryZoneAction.Import,
                    TargetPercent = 100
                },
                new BatteryZoneRule
                {
                    Id = "peak-discharge",
                    StartTime = new TimeDefinition { Type = TimeDefinitionType.StartOfExpensiveImport },
                    EndTime = new TimeDefinition { Type = TimeDefinitionType.EndOfExpensiveImport },
                    Action = BatteryZoneAction.Export,
                    TargetPercent = 10
                }
            ]
        };

        List<ResolvedZone> zones = BatteryZoneResolver.ResolveAllZones(rules, slots);

        // Should have a charge zone in the cheap window and discharge zone in the expensive window
        ResolvedZone? chargeZone = zones.FirstOrDefault(z => z.Action == BatteryZoneAction.Import);
        ResolvedZone? dischargeZone = zones.FirstOrDefault(z => z.Action == BatteryZoneAction.Export);

        chargeZone.ShouldNotBeNull();
        chargeZone.StartMinutes.ShouldBeLessThan(300); // Before 05:00
        chargeZone.TargetPercent.ShouldBe(100);

        dischargeZone.ShouldNotBeNull();
        dischargeZone.StartMinutes.ShouldBeGreaterThanOrEqualTo(960); // At or after 16:00
        dischargeZone.TargetPercent.ShouldBe(10);
    }

    [TestMethod]
    public void TypicalDay_FixedOvernight_ExportDuringHighExportPrices()
    {
        // Fixed overnight charge, dynamic export when export > import
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 20, ExportPrice = 10 },
            new() { TimeMinutes = 30, ImportPrice = 20, ExportPrice = 10 },
            new() { TimeMinutes = 60, ImportPrice = 20, ExportPrice = 10 },
            new() { TimeMinutes = 90, ImportPrice = 20, ExportPrice = 10 },
            new() { TimeMinutes = 120, ImportPrice = 20, ExportPrice = 10 },
            new() { TimeMinutes = 150, ImportPrice = 20, ExportPrice = 10 },
            new() { TimeMinutes = 180, ImportPrice = 15, ExportPrice = 25 }, // export exceeds import
            new() { TimeMinutes = 210, ImportPrice = 15, ExportPrice = 25 },
            new() { TimeMinutes = 240, ImportPrice = 20, ExportPrice = 10 }, // import exceeds export again
            new() { TimeMinutes = 270, ImportPrice = 20, ExportPrice = 10 },
        ];

        BatteryZoneRules rules = new()
        {
            Rules =
            [
                new BatteryZoneRule
                {
                    Id = "fixed-charge",
                    StartTime = new TimeDefinition { Type = TimeDefinitionType.FixedTime, FixedTimeMinutes = 0 },
                    EndTime = new TimeDefinition { Type = TimeDefinitionType.FixedTime, FixedTimeMinutes = 150 },
                    Action = BatteryZoneAction.Import,
                    TargetPercent = 100
                },
                new BatteryZoneRule
                {
                    Id = "export-profitable",
                    StartTime = new TimeDefinition { Type = TimeDefinitionType.ExportExceedsImport },
                    EndTime = new TimeDefinition { Type = TimeDefinitionType.ImportExceedsExport },
                    Action = BatteryZoneAction.Export,
                    TargetPercent = 20
                }
            ]
        };

        List<ResolvedZone> zones = BatteryZoneResolver.ResolveAllZones(rules, slots);

        zones.Count.ShouldBe(2);

        ResolvedZone chargeZone = zones.First(z => z.RuleId == "fixed-charge");
        chargeZone.StartMinutes.ShouldBe(0);
        chargeZone.EndMinutes.ShouldBe(150);

        ResolvedZone exportZone = zones.First(z => z.RuleId == "export-profitable");
        exportZone.StartMinutes.ShouldBe(180);
        exportZone.EndMinutes.ShouldBe(240);
    }

    // ========================================================================
    // Cozy/3-rate tariff: no false zones at edge-of-data
    // ========================================================================

    [TestMethod]
    public void EvaluateZoneRule_CozyTariff_StartEndOfCheapImport_ProducesExactlyOneZone()
    {
        // Cozy tariff: 27.5 → 16.5 → 27.5 → 38.5 → 27.5
        // "Start of cheap import" to "End of cheap import" should only find the 02:00-05:00 trough.
        // The 19:00-00:00 return-to-standard must NOT be falsely detected as a second cheap period.
        List<PricingSlot> slots = BuildCozyTariffSlots();

        BatteryZoneRule rule = new()
        {
            Id = "cheap-charge",
            StartTime = new TimeDefinition { Type = TimeDefinitionType.StartOfCheapImport },
            EndTime = new TimeDefinition { Type = TimeDefinitionType.EndOfCheapImport },
            Action = BatteryZoneAction.Import,
            TargetPercent = 100
        };

        List<ResolvedZone> zones = BatteryZoneResolver.EvaluateZoneRule(rule, slots);

        zones.Count.ShouldBe(1);
        zones[0].StartMinutes.ShouldBe(120);  // 02:00
        zones[0].EndMinutes.ShouldBe(300);    // 05:00
        zones[0].Action.ShouldBe(BatteryZoneAction.Import);
        zones[0].TargetPercent.ShouldBe(100);
    }

    [TestMethod]
    public void EvaluateZoneRule_CozyTariff_StartEndOfExpensiveImport_ProducesExactlyOneZone()
    {
        // Same tariff: "Start of expensive import" to "End of expensive import"
        // should only find the 16:00-19:00 peak, not the 0:00-2:00 standard at the start.
        List<PricingSlot> slots = BuildCozyTariffSlots();

        BatteryZoneRule rule = new()
        {
            Id = "peak-discharge",
            StartTime = new TimeDefinition { Type = TimeDefinitionType.StartOfExpensiveImport },
            EndTime = new TimeDefinition { Type = TimeDefinitionType.EndOfExpensiveImport },
            Action = BatteryZoneAction.Export,
            TargetPercent = 10
        };

        List<ResolvedZone> zones = BatteryZoneResolver.EvaluateZoneRule(rule, slots);

        zones.Count.ShouldBe(1);
        zones[0].StartMinutes.ShouldBe(960);   // 16:00
        zones[0].EndMinutes.ShouldBe(1140);    // 19:00
        zones[0].Action.ShouldBe(BatteryZoneAction.Export);
        zones[0].TargetPercent.ShouldBe(10);
    }

    [TestMethod]
    public void ResolveAllZones_CozyTariff_BothSmartRules_ProducesTwoZones()
    {
        // Full scenario: one rule for cheap import, one for expensive export
        List<PricingSlot> slots = BuildCozyTariffSlots();

        BatteryZoneRules rules = new()
        {
            Rules =
            [
                new BatteryZoneRule
                {
                    Id = "cheap-charge",
                    StartTime = new TimeDefinition { Type = TimeDefinitionType.StartOfCheapImport },
                    EndTime = new TimeDefinition { Type = TimeDefinitionType.EndOfCheapImport },
                    Action = BatteryZoneAction.Import,
                    TargetPercent = 100
                },
                new BatteryZoneRule
                {
                    Id = "peak-discharge",
                    StartTime = new TimeDefinition { Type = TimeDefinitionType.StartOfExpensiveImport },
                    EndTime = new TimeDefinition { Type = TimeDefinitionType.EndOfExpensiveImport },
                    Action = BatteryZoneAction.Export,
                    TargetPercent = 10
                }
            ]
        };

        List<ResolvedZone> zones = BatteryZoneResolver.ResolveAllZones(rules, slots);

        zones.Count.ShouldBe(2);
        zones[0].RuleId.ShouldBe("cheap-charge");
        zones[0].StartMinutes.ShouldBe(120);   // 02:00
        zones[0].EndMinutes.ShouldBe(300);     // 05:00
        zones[1].RuleId.ShouldBe("peak-discharge");
        zones[1].StartMinutes.ShouldBe(960);   // 16:00
        zones[1].EndMinutes.ShouldBe(1140);    // 19:00
    }

    // ========================================================================
    // Cross-midnight cheap rate with extended data
    // ========================================================================

    [TestMethod]
    public void EvaluateZoneRule_CrossMidnightCheapRate_ProducesWrappedZone()
    {
        // Tariff: 23:30-05:30 at 7p, 05:30-23:30 at 29.9p
        // Extended slots: today + tomorrow's first 2 shifted by +1440
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 7, ExportPrice = 5 },
            new() { TimeMinutes = 330, ImportPrice = 29.9, ExportPrice = 15 },
            new() { TimeMinutes = 1410, ImportPrice = 7, ExportPrice = 5 },
            new() { TimeMinutes = 1440, ImportPrice = 7, ExportPrice = 5 },
            new() { TimeMinutes = 1770, ImportPrice = 29.9, ExportPrice = 15 }
        ];

        BatteryZoneRule rule = new()
        {
            Id = "cross-midnight-charge",
            StartTime = new TimeDefinition { Type = TimeDefinitionType.StartOfCheapImport },
            EndTime = new TimeDefinition { Type = TimeDefinitionType.EndOfCheapImport },
            Action = BatteryZoneAction.Import,
            TargetPercent = 80
        };

        List<ResolvedZone> zones = BatteryZoneResolver.EvaluateZoneRule(rule, slots);

        // Should produce a zone starting at 1410 and ending at 1770
        zones.Count.ShouldBe(1);
        zones[0].StartMinutes.ShouldBe(1410);
        zones[0].EndMinutes.ShouldBe(1770);
        zones[0].Action.ShouldBe(BatteryZoneAction.Import);
        zones[0].TargetPercent.ShouldBe(80);
    }

    [TestMethod]
    public void EvaluateZoneRule_FiltersStartTimesToToday()
    {
        // Extended slots where tomorrow's data would produce a start at 1440+
        // The filter should exclude start times >= 1440
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 20, ExportPrice = 10 },
            new() { TimeMinutes = 330, ImportPrice = 20, ExportPrice = 10 },
            new() { TimeMinutes = 1410, ImportPrice = 20, ExportPrice = 10 },
            // Tomorrow's data shifted +1440: has its own minima
            new() { TimeMinutes = 1440, ImportPrice = 30, ExportPrice = 15 },
            new() { TimeMinutes = 1560, ImportPrice = 5, ExportPrice = 5 },
            new() { TimeMinutes = 1680, ImportPrice = 30, ExportPrice = 15 }
        ];

        BatteryZoneRule rule = new()
        {
            Id = "tomorrow-start-filtered",
            StartTime = new TimeDefinition { Type = TimeDefinitionType.StartOfCheapImport },
            EndTime = new TimeDefinition { Type = TimeDefinitionType.EndOfCheapImport },
            Action = BatteryZoneAction.Import,
            TargetPercent = 100
        };

        List<ResolvedZone> zones = BatteryZoneResolver.EvaluateZoneRule(rule, slots);

        // The only minima region starts at 1560 (tomorrow's data) — filtered out as start >= 1440
        // No today-starting zones should be produced
        foreach (ResolvedZone zone in zones)
        {
            zone.StartMinutes.ShouldBeLessThan(1440);
        }
    }

    // ========================================================================
    // Cross-midnight with previous-day data
    // ========================================================================

    [TestMethod]
    public void EvaluateZoneRule_CrossMidnightCheapRate_WithPreviousDayData_DetectsZoneAfterMidnight()
    {
        // Scenario: cheap rate 23:30→05:30 at 7p, standard 29p elsewhere.
        // After midnight, rates refresh for the new day. Today's slots start at minute 0 with 7p.
        // Without yesterday's data, FindMinimaRegionBoundaries can't detect minute 0 as a minimum
        // because there's no preceding higher price. Prepending yesterday's trailing slots fixes this.
        List<PricingSlot> yesterdaySlots =
        [
            new() { TimeMinutes = 0, ImportPrice = 7, ExportPrice = 5 },
            new() { TimeMinutes = 330, ImportPrice = 29, ExportPrice = 15 },
            new() { TimeMinutes = 1410, ImportPrice = 7, ExportPrice = 5 },
        ];

        List<PricingSlot> todaySlots =
        [
            new() { TimeMinutes = 0, ImportPrice = 7, ExportPrice = 5 },
            new() { TimeMinutes = 330, ImportPrice = 29, ExportPrice = 15 },
            new() { TimeMinutes = 1410, ImportPrice = 7, ExportPrice = 5 },
        ];

        List<PricingSlot> tomorrowSlots =
        [
            new() { TimeMinutes = 0, ImportPrice = 7, ExportPrice = 5 },
            new() { TimeMinutes = 330, ImportPrice = 29, ExportPrice = 15 },
        ];

        // Build extended slots: yesterday (offset -1440) + today + tomorrow (offset +1440)
        List<PricingSlot> extended = PricingSlot.ExtendWithPreviousDay(todaySlots, yesterdaySlots);
        extended = PricingSlot.ExtendWithNextDay(extended, tomorrowSlots);

        BatteryZoneRule rule = new()
        {
            Id = "cross-midnight-charge",
            StartTime = new TimeDefinition { Type = TimeDefinitionType.StartOfCheapImport },
            EndTime = new TimeDefinition { Type = TimeDefinitionType.EndOfCheapImport },
            Action = BatteryZoneAction.Import,
            TargetPercent = 80
        };

        List<ResolvedZone> zones = BatteryZoneResolver.EvaluateZoneRule(rule, extended);

        // Should detect a zone covering the current cheap period that started yesterday at 23:30
        // Start at 1410 (23:30, normalized from -30 + 1440), wrapping to end at 330 tomorrow (1770)
        ResolvedZone? activeZone = zones.FirstOrDefault(z =>
            BatteryZoneResolver.IsMinuteInZone(60, z.StartMinutes, z.EndMinutes));

        activeZone.ShouldNotBeNull("Minute 60 (01:00) should be inside an active zone");
        activeZone.Action.ShouldBe(BatteryZoneAction.Import);
        activeZone.TargetPercent.ShouldBe(80);
    }

    // ========================================================================
    // Helpers
    // ========================================================================

    private static List<PricingSlot> BuildFluxDaySlots()
    {
        // Simulate Octopus Flux: cheap 02:00-05:00 (7p), peak 16:00-19:00 (34p), standard elsewhere (20p)
        List<PricingSlot> slots = [];
        for (int minutes = 0; minutes < 1440; minutes += 30)
        {
            double importPrice = minutes switch
            {
                >= 120 and < 300 => 7,   // 02:00-05:00 cheap
                >= 960 and < 1140 => 34,  // 16:00-19:00 peak
                _ => 20                    // standard
            };
            slots.Add(new PricingSlot { TimeMinutes = minutes, ImportPrice = importPrice, ExportPrice = 15 });
        }
        return slots;
    }

    private static List<PricingSlot> BuildCozyTariffSlots()
    {
        // Cozy/3-rate tariff: 0:00-2:00 27.5p, 2:00-5:00 16.5p, 5:00-16:00 27.5p, 16:00-19:00 38.5p, 19:00-0:00 27.5p
        List<PricingSlot> slots = [];
        for (int minutes = 0; minutes < 1440; minutes += 30)
        {
            double importPrice = minutes switch
            {
                >= 0 and < 120 => 27.5,      // 00:00-02:00 standard
                >= 120 and < 300 => 16.5,     // 02:00-05:00 cheap
                >= 300 and < 960 => 27.5,     // 05:00-16:00 standard
                >= 960 and < 1140 => 38.5,    // 16:00-19:00 peak
                _ => 27.5                      // 19:00-00:00 standard
            };
            slots.Add(new PricingSlot { TimeMinutes = minutes, ImportPrice = importPrice, ExportPrice = 15 });
        }
        return slots;
    }

    // ========================================================================
    // DetermineOverrideEndMinutes
    // ========================================================================

    private static List<EnergyRate> BuildPublishedRates(params (int hourStart, int hourEnd, double rate)[] periods)
    {
        // Build rates for a single day (2025-01-15) in UTC, assuming GMT (no offset)
        DateTime baseDate = new(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        List<EnergyRate> rates = [];
        foreach ((int hourStart, int hourEnd, double rate) period in periods)
        {
            rates.Add(new EnergyRate
            {
                StartTimeUtc = baseDate.AddHours(period.hourStart),
                EndTimeUtc = baseDate.AddHours(period.hourEnd),
                RateIncVat = period.rate
            });
        }
        return rates;
    }

    [TestMethod]
    public void DetermineOverrideEndMinutes_AlignedRateChange_ReturnsNextChangeTime()
    {
        // Published: 0-14:30 at 29p, 14:30-19:00 at 7p, 19:00-24:00 at 29p
        List<EnergyRate> rates = BuildPublishedRates(
            (0, 14, 29.0), (14, 19, 7.0), (19, 24, 29.0));
        // Rate change at 14:28 matches boundary at 14:00*60=840 → not within 5 min
        // Actually we need half-hour boundaries. Let's use proper minute boundaries.
        // Published: 00:00-14:30 at 29p, 14:30-19:00 at 7p, 19:00-24:00 at 29p
        rates =
        [
            new EnergyRate { StartTimeUtc = new DateTime(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc), EndTimeUtc = new DateTime(2025, 1, 15, 14, 30, 0, DateTimeKind.Utc), RateIncVat = 29.0 },
            new EnergyRate { StartTimeUtc = new DateTime(2025, 1, 15, 14, 30, 0, DateTimeKind.Utc), EndTimeUtc = new DateTime(2025, 1, 15, 19, 0, 0, DateTimeKind.Utc), RateIncVat = 7.0 },
            new EnergyRate { StartTimeUtc = new DateTime(2025, 1, 15, 19, 0, 0, DateTimeKind.Utc), EndTimeUtc = new DateTime(2025, 1, 16, 0, 0, 0, DateTimeKind.Utc), RateIncVat = 29.0 }
        ];

        // Rate change at 14:28, boundary at 14:30 with matching rate 7p → aligned
        DateTime localNow = new(2025, 1, 15, 14, 28, 0);
        int result = PriceAnalysis.DetermineOverrideEndMinutes(rates, localNow, 7.0);

        // Next change after the 14:30 boundary is at 19:00 (1140 minutes)
        result.ShouldBe(1140);
    }

    [TestMethod]
    public void DetermineOverrideEndMinutes_AlignedButDifferentRate_ReturnsDefault()
    {
        List<EnergyRate> rates =
        [
            new EnergyRate { StartTimeUtc = new DateTime(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc), EndTimeUtc = new DateTime(2025, 1, 15, 14, 30, 0, DateTimeKind.Utc), RateIncVat = 29.0 },
            new EnergyRate { StartTimeUtc = new DateTime(2025, 1, 15, 14, 30, 0, DateTimeKind.Utc), EndTimeUtc = new DateTime(2025, 1, 15, 19, 0, 0, DateTimeKind.Utc), RateIncVat = 7.0 },
            new EnergyRate { StartTimeUtc = new DateTime(2025, 1, 15, 19, 0, 0, DateTimeKind.Utc), EndTimeUtc = new DateTime(2025, 1, 16, 0, 0, 0, DateTimeKind.Utc), RateIncVat = 29.0 }
        ];

        // Rate change at 14:28, boundary at 14:30 exists but sensor reports 15p (not 7p) → not aligned
        DateTime localNow = new(2025, 1, 15, 14, 28, 0);
        int result = PriceAnalysis.DetermineOverrideEndMinutes(rates, localNow, 15.0);

        // Not aligned: currentMinutes (868) + 60 = 928
        result.ShouldBe(868 + 60);
    }

    [TestMethod]
    public void DetermineOverrideEndMinutes_NoNearbyBoundary_ReturnsDefault()
    {
        List<EnergyRate> rates =
        [
            new EnergyRate { StartTimeUtc = new DateTime(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc), EndTimeUtc = new DateTime(2025, 1, 15, 14, 30, 0, DateTimeKind.Utc), RateIncVat = 29.0 },
            new EnergyRate { StartTimeUtc = new DateTime(2025, 1, 15, 14, 30, 0, DateTimeKind.Utc), EndTimeUtc = new DateTime(2025, 1, 15, 19, 0, 0, DateTimeKind.Utc), RateIncVat = 7.0 },
            new EnergyRate { StartTimeUtc = new DateTime(2025, 1, 15, 19, 0, 0, DateTimeKind.Utc), EndTimeUtc = new DateTime(2025, 1, 16, 0, 0, 0, DateTimeKind.Utc), RateIncVat = 29.0 }
        ];

        // Rate change at 14:15 — nearest boundary is at 14:30 (15 min away, > 5 min tolerance)
        DateTime localNow = new(2025, 1, 15, 14, 15, 0);
        int result = PriceAnalysis.DetermineOverrideEndMinutes(rates, localNow, 7.0);

        // Not aligned: 855 + 60 = 915
        result.ShouldBe(855 + 60);
    }

    [TestMethod]
    public void DetermineOverrideEndMinutes_AlignedRateSameRestOfDay_Returns1440()
    {
        List<EnergyRate> rates =
        [
            new EnergyRate { StartTimeUtc = new DateTime(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc), EndTimeUtc = new DateTime(2025, 1, 15, 14, 30, 0, DateTimeKind.Utc), RateIncVat = 29.0 },
            new EnergyRate { StartTimeUtc = new DateTime(2025, 1, 15, 14, 30, 0, DateTimeKind.Utc), EndTimeUtc = new DateTime(2025, 1, 16, 0, 0, 0, DateTimeKind.Utc), RateIncVat = 7.0 }
        ];

        // Rate change at 14:28, aligned with 14:30 boundary at 7p, rate stays 7p for rest of day
        DateTime localNow = new(2025, 1, 15, 14, 28, 0);
        int result = PriceAnalysis.DetermineOverrideEndMinutes(rates, localNow, 7.0);

        result.ShouldBe(1440);
    }

    [TestMethod]
    public void DetermineOverrideEndMinutes_NotAlignedNearMidnight_ClampedTo1440()
    {
        List<EnergyRate> rates =
        [
            new EnergyRate { StartTimeUtc = new DateTime(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc), EndTimeUtc = new DateTime(2025, 1, 16, 0, 0, 0, DateTimeKind.Utc), RateIncVat = 29.0 }
        ];

        // Rate change at 23:30 (1410), not aligned, 1410 + 60 = 1470 → clamped to 1440
        DateTime localNow = new(2025, 1, 15, 23, 30, 0);
        int result = PriceAnalysis.DetermineOverrideEndMinutes(rates, localNow, 7.0);

        result.ShouldBe(1440);
    }

    [TestMethod]
    public void DetermineOverrideEndMinutes_EmptyPublishedRates_ReturnsDefault()
    {
        List<EnergyRate> rates = [];

        DateTime localNow = new(2025, 1, 15, 14, 28, 0);
        int result = PriceAnalysis.DetermineOverrideEndMinutes(rates, localNow, 7.0);

        // 868 + 60 = 928
        result.ShouldBe(928);
    }
}

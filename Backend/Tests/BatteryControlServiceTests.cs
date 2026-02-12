using HomeAssistant.apps.Energy;
using HomeAssistant.Devices.Batteries;
using HomeAssistant.Devices.CarChargers;
using HomeAssistant.Devices.Meters;
using HomeAssistant.Services;
using HomeAssistant.Services.Energy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Reactive.Concurrency;

namespace HomeAssistant.Tests;

[TestClass]
public sealed class BatteryControlServiceTests
{
    // Zone types
    private const string Zone_None = "none";
    private const string Zone_Import = "import";
    private const string Zone_Export = "export";

    // Car charging
    private const bool Car_Not_Charging = false;
    private const bool Car_Charging = true;

    // Battery states as strings (DataRow limitation)
    private const string State_NormalTOU = "NormalTOU";
    private const string State_ForceCharging = "ForceCharging";
    private const string State_ForceDischarging = "ForceDischarging";
    private const string State_Stopped = "Stopped";
    private const string State_Unknown = "Unknown";

    // Hysteresis from service
    private const int Hysteresis = BatteryControlService.HysteresisPercent;

    [TestMethod]
    // ===== Import zone =====
    // Below target, not charging -> ForceCharging
    [DataRow(Zone_Import, 80, 77.0, State_NormalTOU, Car_Not_Charging, State_ForceCharging, "Import: below target-hysteresis, not charging -> start charging")]
    // At hysteresis boundary (target-2), not charging -> ForceCharging
    [DataRow(Zone_Import, 80, 78.0, State_NormalTOU, Car_Not_Charging, State_ForceCharging, "Import: at hysteresis boundary (target-2), not charging -> start charging")]
    // Within hysteresis (target-1), not charging -> NormalTOU (don't start)
    [DataRow(Zone_Import, 80, 79.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "Import: within hysteresis (target-1), not charging -> don't start")]
    // At target, not charging -> NormalTOU
    [DataRow(Zone_Import, 80, 80.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "Import: at target, not charging -> NormalTOU")]
    // Above target, not charging -> NormalTOU
    [DataRow(Zone_Import, 80, 85.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "Import: above target, not charging -> NormalTOU")]
    // Already charging, below target -> ForceCharging (keep going)
    [DataRow(Zone_Import, 80, 75.0, State_ForceCharging, Car_Not_Charging, State_ForceCharging, "Import: already charging, below target -> keep charging")]
    // Already charging, within hysteresis -> ForceCharging (keep going)
    [DataRow(Zone_Import, 80, 79.0, State_ForceCharging, Car_Not_Charging, State_ForceCharging, "Import: already charging, within hysteresis -> keep charging")]
    // Already charging, at target -> NormalTOU (stop)
    [DataRow(Zone_Import, 80, 80.0, State_ForceCharging, Car_Not_Charging, State_NormalTOU, "Import: already charging, at target -> stop")]
    // Already charging, above target -> NormalTOU (stop)
    [DataRow(Zone_Import, 80, 85.0, State_ForceCharging, Car_Not_Charging, State_NormalTOU, "Import: already charging, above target -> stop")]
    // At target + car charging -> Stopped
    [DataRow(Zone_Import, 80, 80.0, State_NormalTOU, Car_Charging, State_Stopped, "Import: at target + car charging -> Stopped")]
    // Within hysteresis + car charging -> Stopped
    [DataRow(Zone_Import, 80, 79.0, State_NormalTOU, Car_Charging, State_Stopped, "Import: within hysteresis + car charging -> Stopped")]
    // Below target + car charging -> ForceCharging
    [DataRow(Zone_Import, 80, 77.0, State_NormalTOU, Car_Charging, State_ForceCharging, "Import: below target + car charging -> ForceCharging")]

    // ===== Export zone =====
    // Above target, not discharging -> ForceDischarging
    [DataRow(Zone_Export, 20, 23.0, State_NormalTOU, Car_Not_Charging, State_ForceDischarging, "Export: above target+hysteresis, not discharging -> start discharging")]
    // At hysteresis boundary (target+2), not discharging -> ForceDischarging
    [DataRow(Zone_Export, 20, 22.0, State_NormalTOU, Car_Not_Charging, State_ForceDischarging, "Export: at hysteresis boundary (target+2), not discharging -> start discharging")]
    // Within hysteresis (target+1), not discharging -> NormalTOU
    [DataRow(Zone_Export, 20, 21.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "Export: within hysteresis (target+1), not discharging -> don't start")]
    // At target, not discharging -> NormalTOU
    [DataRow(Zone_Export, 20, 20.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "Export: at target, not discharging -> NormalTOU")]
    // Below target, not discharging -> NormalTOU
    [DataRow(Zone_Export, 20, 15.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "Export: below target, not discharging -> NormalTOU")]
    // Already discharging, above target -> ForceDischarging
    [DataRow(Zone_Export, 20, 25.0, State_ForceDischarging, Car_Not_Charging, State_ForceDischarging, "Export: already discharging, above target -> keep discharging")]
    // Already discharging, within hysteresis -> ForceDischarging
    [DataRow(Zone_Export, 20, 21.0, State_ForceDischarging, Car_Not_Charging, State_ForceDischarging, "Export: already discharging, within hysteresis -> keep discharging")]
    // Already discharging, at target -> NormalTOU
    [DataRow(Zone_Export, 20, 20.0, State_ForceDischarging, Car_Not_Charging, State_NormalTOU, "Export: already discharging, at target -> stop")]
    // Already discharging, below target -> NormalTOU
    [DataRow(Zone_Export, 20, 15.0, State_ForceDischarging, Car_Not_Charging, State_NormalTOU, "Export: already discharging, below target -> stop")]
    // At target + car charging -> Stopped
    [DataRow(Zone_Export, 20, 20.0, State_NormalTOU, Car_Charging, State_Stopped, "Export: at target + car charging -> Stopped")]

    // ===== No active zone =====
    // No car -> NormalTOU
    [DataRow(Zone_None, 0, 50.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "No zone: no car -> NormalTOU")]
    // Was charging, no car -> NormalTOU
    [DataRow(Zone_None, 0, 50.0, State_ForceCharging, Car_Not_Charging, State_NormalTOU, "No zone: was charging, no car -> NormalTOU")]
    // Was discharging, no car -> NormalTOU
    [DataRow(Zone_None, 0, 50.0, State_ForceDischarging, Car_Not_Charging, State_NormalTOU, "No zone: was discharging, no car -> NormalTOU")]
    // Car charging -> Stopped
    [DataRow(Zone_None, 0, 50.0, State_NormalTOU, Car_Charging, State_Stopped, "No zone: car charging -> Stopped")]
    // Was charging + car -> Stopped
    [DataRow(Zone_None, 0, 50.0, State_ForceCharging, Car_Charging, State_Stopped, "No zone: was charging + car -> Stopped")]
    // Already stopped + car -> Stopped (no-op)
    [DataRow(Zone_None, 0, 50.0, State_Stopped, Car_Charging, State_Stopped, "No zone: already stopped + car -> Stopped")]

    // ===== Boundary values =====
    // Import target 100%, charge at 98/99/100
    [DataRow(Zone_Import, 100, 98.0, State_NormalTOU, Car_Not_Charging, State_ForceCharging, "Import: target 100%, charge at 98% (at boundary) -> start charging")]
    [DataRow(Zone_Import, 100, 99.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "Import: target 100%, charge at 99% (within hysteresis) -> don't start")]
    [DataRow(Zone_Import, 100, 100.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "Import: target 100%, charge at 100% -> NormalTOU")]
    // Export target 0%, charge at 2/1/0
    [DataRow(Zone_Export, 0, 2.0, State_NormalTOU, Car_Not_Charging, State_ForceDischarging, "Export: target 0%, charge at 2% (at boundary) -> start discharging")]
    [DataRow(Zone_Export, 0, 1.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "Export: target 0%, charge at 1% (within hysteresis) -> don't start")]
    [DataRow(Zone_Export, 0, 0.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "Export: target 0%, charge at 0% -> NormalTOU")]
    public async Task SetBatteryState_SetsCorrectState(
        string zoneType,
        int targetPercent,
        double chargePercent,
        string currentState,
        bool isCarCharging,
        string expectedState,
        string reason)
    {
        // Arrange
        ServiceProvider serviceProvider = GetServiceProvider();
        BatteryControlService sut = serviceProvider.GetRequiredService<BatteryControlService>();

        BatteryState currentBatteryState = Enum.Parse<BatteryState>(currentState);
        BatteryState expectedBatteryState = Enum.Parse<BatteryState>(expectedState);

        IHomeBattery homeBattery = serviceProvider.GetRequiredService<IHomeBattery>();
        homeBattery.CurrentChargePercent.Returns(chargePercent);
        homeBattery.GetHomeBatteryState().Returns(currentBatteryState);

        ICarCharger carCharger = serviceProvider.GetRequiredService<ICarCharger>();
        carCharger.ChargerCurrent.Returns(isCarCharging ? 10.0 : 0.0);

        IElectricityMeter electricityMeter = serviceProvider.GetRequiredService<IElectricityMeter>();
        electricityMeter.CurrentRatePerKwh.Returns(0.25);

        // Set up zone if applicable
        if (zoneType != Zone_None)
        {
            BatteryZoneAction action = zoneType == Zone_Import ? BatteryZoneAction.Import : BatteryZoneAction.Export;
            sut.SetResolvedZones([
                new ResolvedZone
                {
                    RuleId = "test-rule",
                    StartMinutes = 0,
                    EndMinutes = 1440,
                    Action = action,
                    TargetPercent = targetPercent
                }
            ]);
        }

        // Act
        await sut.SetBatteryState("unit test");

        // Assert
        if (expectedBatteryState != currentBatteryState)
        {
            homeBattery.Received(1).SetHomeBatteryState(expectedBatteryState);
        }
        else
        {
            homeBattery.DidNotReceive().SetHomeBatteryState(Arg.Any<BatteryState>());
        }
    }

    [TestMethod]
    [DataRow(0.0, 0, "Car not charging, headroom should be 0")]
    [DataRow(10.0, 10, "Car charging at 10A, headroom should be 10")]
    [DataRow(32.0, 32, "Car charging at 32A, headroom should be 32")]
    [DataRow(7.5, 7, "Car charging at 7.5A, headroom should be 7 (truncated)")]
    public async Task SetBatteryState_SetsMaxChargeCurrentHeadroom(
        double carChargerCurrent,
        int expectedHeadroom,
        string reason)
    {
        // Arrange
        ServiceProvider serviceProvider = GetServiceProvider();
        BatteryControlService sut = serviceProvider.GetRequiredService<BatteryControlService>();

        IHomeBattery homeBattery = serviceProvider.GetRequiredService<IHomeBattery>();
        homeBattery.CurrentChargePercent.Returns(50.0);
        homeBattery.GetHomeBatteryState().Returns(BatteryState.NormalTOU);

        ICarCharger carCharger = serviceProvider.GetRequiredService<ICarCharger>();
        carCharger.ChargerCurrent.Returns(carChargerCurrent);

        IElectricityMeter electricityMeter = serviceProvider.GetRequiredService<IElectricityMeter>();
        electricityMeter.CurrentRatePerKwh.Returns(0.25);

        // Act
        await sut.SetBatteryState("unit test");

        // Assert
        homeBattery.Received(1).SetMaxChargeCurrentHeadroom(expectedHeadroom);
    }

    [TestMethod]
    public async Task SetBatteryState_DoesNotCallSetState_WhenInputsUnchanged()
    {
        // Arrange
        ServiceProvider serviceProvider = GetServiceProvider();
        BatteryControlService sut = serviceProvider.GetRequiredService<BatteryControlService>();

        IHomeBattery homeBattery = serviceProvider.GetRequiredService<IHomeBattery>();
        homeBattery.CurrentChargePercent.Returns(50.0);
        homeBattery.GetHomeBatteryState().Returns(BatteryState.NormalTOU);

        ICarCharger carCharger = serviceProvider.GetRequiredService<ICarCharger>();
        carCharger.ChargerCurrent.Returns(0.0);

        IElectricityMeter electricityMeter = serviceProvider.GetRequiredService<IElectricityMeter>();
        electricityMeter.CurrentRatePerKwh.Returns(0.25);

        // Act - first call should evaluate
        await sut.SetBatteryState("first call");
        // Second call with identical inputs should be skipped by change detection
        await sut.SetBatteryState("second call");

        // Assert - SetHomeBatteryState should NOT have been called at all
        // (NormalTOU desired == NormalTOU current, so no state change on first call either)
        homeBattery.DidNotReceive().SetHomeBatteryState(Arg.Any<BatteryState>());

        // SetMaxChargeCurrentHeadroom IS called each time (before change detection)
        homeBattery.Received(2).SetMaxChargeCurrentHeadroom(0);
    }

    [TestMethod]
    public async Task SetBatteryState_ChangeDetection_SkipsSecondCallWithSameInputs()
    {
        // Arrange - use a scenario where SetHomeBatteryState IS called
        ServiceProvider serviceProvider = GetServiceProvider();
        BatteryControlService sut = serviceProvider.GetRequiredService<BatteryControlService>();

        IHomeBattery homeBattery = serviceProvider.GetRequiredService<IHomeBattery>();
        homeBattery.CurrentChargePercent.Returns(50.0);
        // Current state is ForceCharging but no zone -> should transition to NormalTOU
        homeBattery.GetHomeBatteryState().Returns(BatteryState.ForceCharging);

        ICarCharger carCharger = serviceProvider.GetRequiredService<ICarCharger>();
        carCharger.ChargerCurrent.Returns(0.0);

        IElectricityMeter electricityMeter = serviceProvider.GetRequiredService<IElectricityMeter>();
        electricityMeter.CurrentRatePerKwh.Returns(0.25);

        // Act
        await sut.SetBatteryState("first call");

        // Now the battery reports the new state
        homeBattery.GetHomeBatteryState().Returns(BatteryState.NormalTOU);

        await sut.SetBatteryState("second call");

        // Assert - SetHomeBatteryState called exactly once (first call), second is skipped
        homeBattery.Received(1).SetHomeBatteryState(BatteryState.NormalTOU);
    }

    // ========================================================================
    // ReactToRateChangeAsync
    // ========================================================================

    [TestMethod]
    public async Task ReactToRateChangeAsync_UnexpectedRateDrop_CreatesImportZone()
    {
        // Arrange: Octopus published 29p all day, but sensor reports 7p at 14:00
        ServiceProvider serviceProvider = GetServiceProvider();
        BatteryControlService sut = serviceProvider.GetRequiredService<BatteryControlService>();
        FakeTimeProvider timeProvider = serviceProvider.GetRequiredService<FakeTimeProvider>();
        timeProvider.SetSpecificDateTime(new DateTimeOffset(2025, 1, 15, 14, 0, 0, TimeSpan.Zero));

        // Build published rates: 29p all day in 30-min slots
        List<EnergyRate> importRates = [];
        for (int h = 0; h < 48; h++)
        {
            importRates.Add(new EnergyRate
            {
                StartTimeUtc = new DateTime(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc).AddMinutes(h * 30),
                EndTimeUtc = new DateTime(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc).AddMinutes((h + 1) * 30),
                RateIncVat = 29.0
            });
        }
        List<EnergyRate> exportRates = [];
        for (int h = 0; h < 48; h++)
        {
            exportRates.Add(new EnergyRate
            {
                StartTimeUtc = new DateTime(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc).AddMinutes(h * 30),
                EndTimeUtc = new DateTime(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc).AddMinutes((h + 1) * 30),
                RateIncVat = 15.0
            });
        }

        sut.SetCachedRates(importRates, exportRates);

        // Rule: import when price < 10p (would match the surprise 7p rate)
        sut.SetCurrentRules(new BatteryZoneRules
        {
            Rules =
            [
                new BatteryZoneRule
                {
                    Id = "cheap-import",
                    StartTime = new TimeDefinition { Type = TimeDefinitionType.StartOfCheapImport },
                    EndTime = new TimeDefinition { Type = TimeDefinitionType.EndOfCheapImport },
                    Action = BatteryZoneAction.Import,
                    TargetPercent = 100
                }
            ]
        });

        // Act
        await sut.ReactToRateChangeAsync(7.0);

        // Assert: the reactive resolution should have patched the 7p rate into slots
        // and re-resolved zones. At 14:00 (840 minutes), we should have an active zone
        // because the patched slots now show 7p (cheap) in the current time window.
        ResolvedZone? activeZone = sut.GetActiveZone(840);
        // The zone resolver looks for minima regions - with 7p patched into an otherwise 29p day,
        // it should create an import zone covering the patched slots.
        activeZone.ShouldNotBeNull();
        activeZone.Action.ShouldBe(BatteryZoneAction.Import);
    }

    [TestMethod]
    public async Task ReactToRateChangeAsync_AlignedRateChange_UsesPublishedDuration()
    {
        // Arrange: Octopus published 29p 00:00-14:30, 7p 14:30-19:00, 29p 19:00-24:00
        ServiceProvider serviceProvider = GetServiceProvider();
        BatteryControlService sut = serviceProvider.GetRequiredService<BatteryControlService>();
        FakeTimeProvider timeProvider = serviceProvider.GetRequiredService<FakeTimeProvider>();
        timeProvider.SetSpecificDateTime(new DateTimeOffset(2025, 1, 15, 14, 28, 0, TimeSpan.Zero));

        List<EnergyRate> importRates =
        [
            new EnergyRate { StartTimeUtc = new DateTime(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc), EndTimeUtc = new DateTime(2025, 1, 15, 14, 30, 0, DateTimeKind.Utc), RateIncVat = 29.0 },
            new EnergyRate { StartTimeUtc = new DateTime(2025, 1, 15, 14, 30, 0, DateTimeKind.Utc), EndTimeUtc = new DateTime(2025, 1, 15, 19, 0, 0, DateTimeKind.Utc), RateIncVat = 7.0 },
            new EnergyRate { StartTimeUtc = new DateTime(2025, 1, 15, 19, 0, 0, DateTimeKind.Utc), EndTimeUtc = new DateTime(2025, 1, 16, 0, 0, 0, DateTimeKind.Utc), RateIncVat = 29.0 }
        ];
        List<EnergyRate> exportRates =
        [
            new EnergyRate { StartTimeUtc = new DateTime(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc), EndTimeUtc = new DateTime(2025, 1, 16, 0, 0, 0, DateTimeKind.Utc), RateIncVat = 15.0 }
        ];

        sut.SetCachedRates(importRates, exportRates);
        sut.SetCurrentRules(new BatteryZoneRules
        {
            Rules =
            [
                new BatteryZoneRule
                {
                    Id = "cheap-import",
                    StartTime = new TimeDefinition { Type = TimeDefinitionType.StartOfCheapImport },
                    EndTime = new TimeDefinition { Type = TimeDefinitionType.EndOfCheapImport },
                    Action = BatteryZoneAction.Import,
                    TargetPercent = 100
                }
            ]
        });

        // Act: rate change at 14:28, aligned with 14:30 boundary (7p)
        await sut.ReactToRateChangeAsync(7.0);

        // Assert: zones should cover 14:30-19:00 (the published cheap period)
        // At 14:30 = 870 minutes, there should be an active import zone
        ResolvedZone? zoneAt870 = sut.GetActiveZone(870);
        zoneAt870.ShouldNotBeNull();
        zoneAt870.Action.ShouldBe(BatteryZoneAction.Import);
    }

    [TestMethod]
    public async Task ReactToRateChangeAsync_NoCachedRates_FallsBackToFullResolution()
    {
        // Arrange
        ServiceProvider serviceProvider = GetServiceProvider();
        BatteryControlService sut = serviceProvider.GetRequiredService<BatteryControlService>();
        FakeTimeProvider timeProvider = serviceProvider.GetRequiredService<FakeTimeProvider>();
        timeProvider.SetSpecificDateTime(new DateTimeOffset(2025, 1, 15, 14, 0, 0, TimeSpan.Zero));

        IElectricityRatesReader ratesReader = serviceProvider.GetRequiredService<IElectricityRatesReader>();
        ratesReader.GetElectricityImportRatesAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(new List<EnergyRate>());
        ratesReader.GetElectricityExportRatesAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(new List<EnergyRate>());

        // No cached rates - should fall back to full ResolveZonesAsync
        await sut.ReactToRateChangeAsync(7.0);

        // Assert: should have called the rates reader (full resolution path)
        await ratesReader.Received(1).GetElectricityImportRatesAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>());
    }

    [TestMethod]
    public async Task ReactToRateChangeAsync_NullRate_FallsBackToFullResolution()
    {
        // Arrange
        ServiceProvider serviceProvider = GetServiceProvider();
        BatteryControlService sut = serviceProvider.GetRequiredService<BatteryControlService>();
        FakeTimeProvider timeProvider = serviceProvider.GetRequiredService<FakeTimeProvider>();
        timeProvider.SetSpecificDateTime(new DateTimeOffset(2025, 1, 15, 14, 0, 0, TimeSpan.Zero));

        IElectricityRatesReader ratesReader = serviceProvider.GetRequiredService<IElectricityRatesReader>();
        ratesReader.GetElectricityImportRatesAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(new List<EnergyRate>());
        ratesReader.GetElectricityExportRatesAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(new List<EnergyRate>());

        // Null rate - should fall back to full ResolveZonesAsync
        await sut.ReactToRateChangeAsync(null);

        // Assert: should have called the rates reader (full resolution path)
        await ratesReader.Received(1).GetElectricityImportRatesAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>());
    }

    private static ServiceProvider GetServiceProvider()
    {
        ServiceCollection services = new();

        services.AddSingleton(Substitute.For<IScheduler>());
        services.AddSingleton(Substitute.For<IHomeBattery>());
        services.AddSingleton(Substitute.For<ICarCharger>());
        services.AddSingleton(Substitute.For<IElectricityMeter>());
        services.AddSingleton(Substitute.For<ILogger<BatteryControlService>>());
        services.AddSingleton(Substitute.For<IBatteryRulesPersistenceService>());
        services.AddSingleton(Substitute.For<IElectricityRatesReader>());
        services.AddSingleton(Substitute.For<IDeviceSettingsPersistenceService>());
        services.AddSingleton<FakeTimeProvider>();
        services.AddSingleton<TimeProvider>(provider => provider.GetRequiredService<FakeTimeProvider>());
        services.AddSingleton<BatteryControlService>();

        return services.BuildServiceProvider();
    }
}

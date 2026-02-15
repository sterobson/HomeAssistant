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
            sut.SetCurrentRules(CreateFixedZoneRules("test-rule", 0, 1440, action, targetPercent));
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
    public async Task SetBatteryState_SkipsUpdate_WhenNothingChanged()
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

        // Act - call twice with the same state
        await sut.SetBatteryState("first call");
        await sut.SetBatteryState("second call");

        // Assert - SetHomeBatteryState should only be called once (or not at all if already in correct state)
        // Since initial state is NormalTOU and desired is NormalTOU (no zone, no car), no calls expected
        homeBattery.DidNotReceive().SetHomeBatteryState(Arg.Any<BatteryState>());
    }

    [TestMethod]
    public async Task SetBatteryState_UpdatesAfterChange()
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

        // First call - no zone, no car, NormalTOU -> NormalTOU (no change)
        await sut.SetBatteryState("first call");
        homeBattery.DidNotReceive().SetHomeBatteryState(Arg.Any<BatteryState>());

        // Now car starts charging
        carCharger.ChargerCurrent.Returns(10.0);
        homeBattery.ClearReceivedCalls();

        // Second call - car charging, should switch to Stopped
        await sut.SetBatteryState("car started");
        homeBattery.Received(1).SetHomeBatteryState(BatteryState.Stopped);
    }

    [TestMethod]
    public async Task ReactToRateChangeAsync_AppliesOverrideToZones()
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
        await sut.ReactToRateChangeAsync(0.07);

        // Assert: zones should cover 14:30-19:00 (the published cheap period)
        // At 14:30 = 870 minutes, there should be an active import zone
        ResolvedZone? zoneAt870 = sut.GetActiveZone(870);
        zoneAt870.ShouldNotBeNull();
        zoneAt870.Action.ShouldBe(BatteryZoneAction.Import);
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
        await sut.ReactToRateChangeAsync(0.07);

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

        // No cached rates - should fall back to full RefreshRatesAsync
        await sut.ReactToRateChangeAsync(0.07);

        // Assert: should have called the rates reader (full refresh path)
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

        // Null rate - should fall back to full RefreshRatesAsync
        await sut.ReactToRateChangeAsync(null);

        // Assert: should have called the rates reader
        await ratesReader.Received(1).GetElectricityImportRatesAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>());
    }

    // ========================================================================
    // Export zone - hysteresis and timing (12:00-17:00, target 20%)
    // ========================================================================

    [TestMethod]
    // Well above target — should always start discharging
    [DataRow(14, 30, 50.0, State_NormalTOU, Car_Not_Charging, State_ForceDischarging, "50% well above target → start discharging")]
    [DataRow(12, 5, 30.0, State_NormalTOU, Car_Not_Charging, State_ForceDischarging, "30% well above target → start discharging")]
    // Hysteresis boundary (target + 2% = 22%)
    [DataRow(15, 0, 23.0, State_NormalTOU, Car_Not_Charging, State_ForceDischarging, "23% above boundary → start discharging")]
    [DataRow(15, 0, 22.0, State_NormalTOU, Car_Not_Charging, State_ForceDischarging, "22% at boundary → start discharging")]
    // Within hysteresis band (20% < charge < 22%), not discharging — don't start
    [DataRow(15, 0, 21.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "21% in hysteresis, not discharging → don't start")]
    // Within hysteresis band, already discharging — keep going
    [DataRow(15, 0, 21.0, State_ForceDischarging, Car_Not_Charging, State_ForceDischarging, "21% in hysteresis, already discharging → keep going")]
    // At target — stop
    [DataRow(16, 0, 20.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "20% at target → NormalTOU")]
    [DataRow(16, 0, 20.0, State_ForceDischarging, Car_Not_Charging, State_NormalTOU, "20% at target, was discharging → stop")]
    // Below target — stop
    [DataRow(16, 0, 15.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "15% below target → NormalTOU")]
    [DataRow(16, 0, 10.0, State_ForceDischarging, Car_Not_Charging, State_NormalTOU, "10% below target, was discharging → stop")]
    // Car charging — Stopped in hysteresis/at target, ForceDischarging above boundary
    [DataRow(14, 0, 50.0, State_NormalTOU, Car_Charging, State_ForceDischarging, "50% above boundary + car → ForceDischarging")]
    [DataRow(14, 0, 21.0, State_NormalTOU, Car_Charging, State_Stopped, "21% in hysteresis + car → Stopped")]
    [DataRow(14, 0, 20.0, State_NormalTOU, Car_Charging, State_Stopped, "20% at target + car → Stopped")]
    // Outside zone — NormalTOU regardless of charge
    [DataRow(11, 59, 50.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "Before zone start → NormalTOU")]
    [DataRow(17, 0, 50.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "At zone end (exclusive) → NormalTOU")]
    [DataRow(17, 1, 50.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "After zone end → NormalTOU")]
    // Outside zone + car charging → Stopped
    [DataRow(17, 1, 50.0, State_NormalTOU, Car_Charging, State_Stopped, "After zone + car → Stopped")]
    public async Task SetBatteryState_ExportZone_HysteresisAndTiming(
        int hour, int minute,
        double chargePercent,
        string currentState,
        bool isCarCharging,
        string expectedState,
        string reason)
    {
        // Arrange
        ServiceProvider serviceProvider = GetServiceProvider();
        BatteryControlService sut = serviceProvider.GetRequiredService<BatteryControlService>();
        FakeTimeProvider timeProvider = serviceProvider.GetRequiredService<FakeTimeProvider>();
        timeProvider.SetSpecificDateTime(new DateTimeOffset(2025, 1, 15, hour, minute, 0, TimeSpan.Zero));

        BatteryState currentBatteryState = Enum.Parse<BatteryState>(currentState);
        BatteryState expectedBatteryState = Enum.Parse<BatteryState>(expectedState);

        IHomeBattery homeBattery = serviceProvider.GetRequiredService<IHomeBattery>();
        homeBattery.CurrentChargePercent.Returns(chargePercent);
        homeBattery.GetHomeBatteryState().Returns(currentBatteryState);

        ICarCharger carCharger = serviceProvider.GetRequiredService<ICarCharger>();
        carCharger.ChargerCurrent.Returns(isCarCharging ? 10.0 : 0.0);

        IElectricityMeter electricityMeter = serviceProvider.GetRequiredService<IElectricityMeter>();
        electricityMeter.CurrentRatePerKwh.Returns(0.25);

        // Export zone 12:00-17:00, target 20%
        sut.SetCurrentRules(CreateFixedZoneRules("test-export", 720, 1020, BatteryZoneAction.Export, 20));

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

    // ========================================================================
    // Import zone - hysteresis and timing (02:00-07:00, target 80%)
    // ========================================================================

    [TestMethod]
    // Well below target — should always start charging
    [DataRow(2, 5, 10.0, State_NormalTOU, Car_Not_Charging, State_ForceCharging, "10% well below target → start charging")]
    [DataRow(3, 0, 50.0, State_NormalTOU, Car_Not_Charging, State_ForceCharging, "50% well below target → start charging")]
    [DataRow(5, 30, 70.0, State_NormalTOU, Car_Not_Charging, State_ForceCharging, "70% below target-hysteresis → start charging")]
    // Hysteresis boundary (target - 2% = 78%)
    [DataRow(4, 0, 77.0, State_NormalTOU, Car_Not_Charging, State_ForceCharging, "77% below boundary → start charging")]
    [DataRow(4, 0, 78.0, State_NormalTOU, Car_Not_Charging, State_ForceCharging, "78% at boundary → start charging")]
    // Within hysteresis band (78% < charge < 80%), not charging — don't start
    [DataRow(4, 0, 79.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "79% in hysteresis, not charging → don't start")]
    // Within hysteresis band, already charging — keep going
    [DataRow(4, 0, 79.0, State_ForceCharging, Car_Not_Charging, State_ForceCharging, "79% in hysteresis, already charging → keep going")]
    // At target — stop
    [DataRow(5, 0, 80.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "80% at target → NormalTOU")]
    [DataRow(5, 0, 80.0, State_ForceCharging, Car_Not_Charging, State_NormalTOU, "80% at target, was charging → stop")]
    // Above target — stop
    [DataRow(6, 0, 85.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "85% above target → NormalTOU")]
    [DataRow(6, 0, 90.0, State_ForceCharging, Car_Not_Charging, State_NormalTOU, "90% above target, was charging → stop")]
    // Car charging — ForceCharging still wins below boundary, Stopped in hysteresis/at target
    [DataRow(4, 0, 50.0, State_NormalTOU, Car_Charging, State_ForceCharging, "50% below boundary + car → ForceCharging")]
    [DataRow(4, 0, 79.0, State_NormalTOU, Car_Charging, State_Stopped, "79% in hysteresis + car → Stopped")]
    [DataRow(4, 0, 80.0, State_NormalTOU, Car_Charging, State_Stopped, "80% at target + car → Stopped")]
    // Outside zone — NormalTOU regardless of charge
    [DataRow(1, 59, 50.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "Before zone start → NormalTOU")]
    [DataRow(7, 0, 50.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "At zone end (exclusive) → NormalTOU")]
    [DataRow(7, 1, 50.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "After zone end → NormalTOU")]
    // Outside zone + car charging → Stopped
    [DataRow(7, 1, 50.0, State_NormalTOU, Car_Charging, State_Stopped, "After zone + car → Stopped")]
    public async Task SetBatteryState_ImportZone_HysteresisAndTiming(
        int hour, int minute,
        double chargePercent,
        string currentState,
        bool isCarCharging,
        string expectedState,
        string reason)
    {
        // Arrange
        ServiceProvider serviceProvider = GetServiceProvider();
        BatteryControlService sut = serviceProvider.GetRequiredService<BatteryControlService>();
        FakeTimeProvider timeProvider = serviceProvider.GetRequiredService<FakeTimeProvider>();
        timeProvider.SetSpecificDateTime(new DateTimeOffset(2025, 1, 15, hour, minute, 0, TimeSpan.Zero));

        BatteryState currentBatteryState = Enum.Parse<BatteryState>(currentState);
        BatteryState expectedBatteryState = Enum.Parse<BatteryState>(expectedState);

        IHomeBattery homeBattery = serviceProvider.GetRequiredService<IHomeBattery>();
        homeBattery.CurrentChargePercent.Returns(chargePercent);
        homeBattery.GetHomeBatteryState().Returns(currentBatteryState);

        ICarCharger carCharger = serviceProvider.GetRequiredService<ICarCharger>();
        carCharger.ChargerCurrent.Returns(isCarCharging ? 10.0 : 0.0);

        IElectricityMeter electricityMeter = serviceProvider.GetRequiredService<IElectricityMeter>();
        electricityMeter.CurrentRatePerKwh.Returns(0.25);

        // Import zone 02:00-07:00, target 80%
        sut.SetCurrentRules(CreateFixedZoneRules("test-import", 120, 420, BatteryZoneAction.Import, 80));

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

    // ========================================================================
    // Graduated export zone - 12:00-17:00 (300 min), initial 100%, target 20%
    //
    // effectiveTarget(t) = 100 - 80 × elapsed/300
    //   13:15 (elapsed=75):  effectiveTarget = 80, boundary = 82
    //   14:30 (elapsed=150): effectiveTarget = 60, boundary = 62
    //   15:45 (elapsed=225): effectiveTarget = 40, boundary = 42
    //   16:45 (elapsed=285): effectiveTarget = 24, boundary = 26
    // ========================================================================

    [TestMethod]
    // Early in zone (13:15, effectiveTarget=80, boundary=82)
    [DataRow(100.0, 13, 15, 90.0, State_NormalTOU, Car_Not_Charging, State_ForceDischarging, "90% above boundary → start discharging")]
    [DataRow(100.0, 13, 15, 82.0, State_NormalTOU, Car_Not_Charging, State_ForceDischarging, "82% at boundary → start discharging")]
    [DataRow(100.0, 13, 15, 81.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "81% in hysteresis → don't start")]
    [DataRow(100.0, 13, 15, 81.0, State_ForceDischarging, Car_Not_Charging, State_ForceDischarging, "81% in hysteresis, already discharging → keep going")]
    [DataRow(100.0, 13, 15, 80.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "80% at effective target → NormalTOU")]
    [DataRow(100.0, 13, 15, 80.0, State_ForceDischarging, Car_Not_Charging, State_NormalTOU, "80% at effective target, was discharging → stop")]
    [DataRow(100.0, 13, 15, 50.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "50% below effective target → NormalTOU (ahead of schedule)")]
    // Midpoint (14:30, effectiveTarget=60, boundary=62)
    [DataRow(100.0, 14, 30, 62.0, State_NormalTOU, Car_Not_Charging, State_ForceDischarging, "62% at boundary → start discharging")]
    [DataRow(100.0, 14, 30, 61.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "61% in hysteresis → don't start")]
    [DataRow(100.0, 14, 30, 61.0, State_ForceDischarging, Car_Not_Charging, State_ForceDischarging, "61% in hysteresis, already discharging → keep going")]
    [DataRow(100.0, 14, 30, 60.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "60% at effective target → NormalTOU")]
    [DataRow(100.0, 14, 30, 60.0, State_ForceDischarging, Car_Not_Charging, State_NormalTOU, "60% at effective target, was discharging → stop")]
    [DataRow(100.0, 14, 30, 80.0, State_NormalTOU, Car_Not_Charging, State_ForceDischarging, "80% above effective target → ForceDischarging (behind schedule)")]
    // Near end (16:45, effectiveTarget=24, boundary=26)
    [DataRow(100.0, 16, 45, 26.0, State_NormalTOU, Car_Not_Charging, State_ForceDischarging, "26% at boundary → start discharging")]
    [DataRow(100.0, 16, 45, 25.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "25% in hysteresis → don't start")]
    [DataRow(100.0, 16, 45, 25.0, State_ForceDischarging, Car_Not_Charging, State_ForceDischarging, "25% in hysteresis, already discharging → keep going")]
    [DataRow(100.0, 16, 45, 24.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "24% at effective target → NormalTOU")]
    [DataRow(100.0, 16, 45, 20.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "20% below effective target → NormalTOU (at final target)")]
    // Car charging at midpoint
    [DataRow(100.0, 14, 30, 62.0, State_NormalTOU, Car_Charging, State_ForceDischarging, "62% at boundary + car → ForceDischarging")]
    [DataRow(100.0, 14, 30, 61.0, State_NormalTOU, Car_Charging, State_Stopped, "61% in hysteresis + car → Stopped")]
    [DataRow(100.0, 14, 30, 60.0, State_NormalTOU, Car_Charging, State_Stopped, "60% at effective target + car → Stopped")]
    public async Task SetBatteryState_GraduatedExportZone_HysteresisAndTiming(
        double initialPercent,
        int hour, int minute,
        double chargePercent,
        string currentState,
        bool isCarCharging,
        string expectedState,
        string reason)
    {
        // Arrange
        ServiceProvider serviceProvider = GetServiceProvider();
        BatteryControlService sut = serviceProvider.GetRequiredService<BatteryControlService>();
        FakeTimeProvider timeProvider = serviceProvider.GetRequiredService<FakeTimeProvider>();
        timeProvider.SetSpecificDateTime(new DateTimeOffset(2025, 1, 15, hour, minute, 0, TimeSpan.Zero));

        BatteryState currentBatteryState = Enum.Parse<BatteryState>(currentState);
        BatteryState expectedBatteryState = Enum.Parse<BatteryState>(expectedState);

        IHomeBattery homeBattery = serviceProvider.GetRequiredService<IHomeBattery>();
        homeBattery.CurrentChargePercent.Returns(chargePercent);
        homeBattery.GetHomeBatteryState().Returns(currentBatteryState);

        ICarCharger carCharger = serviceProvider.GetRequiredService<ICarCharger>();
        carCharger.ChargerCurrent.Returns(isCarCharging ? 10.0 : 0.0);

        IElectricityMeter electricityMeter = serviceProvider.GetRequiredService<IElectricityMeter>();
        electricityMeter.CurrentRatePerKwh.Returns(0.25);

        // Graduated export zone 12:00-17:00, target 20%
        sut.SetCurrentRules(CreateFixedZoneRules("test-graduated-export", 720, 1020, BatteryZoneAction.Export, 20, graduatedTarget: true));

        // Pre-set the graduated zone state so the service uses the given initial battery %
        sut.SetGraduatedZoneState("test-graduated-export", initialPercent);

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

    // ========================================================================
    // Graduated import zone - 02:00-07:00 (300 min), initial 20%, target 80%
    //
    // effectiveTarget(t) = 20 + 60 × elapsed/300
    //   03:15 (elapsed=75):  effectiveTarget = 35, boundary = 33
    //   04:30 (elapsed=150): effectiveTarget = 50, boundary = 48
    //   05:45 (elapsed=225): effectiveTarget = 65, boundary = 63
    //   06:40 (elapsed=280): effectiveTarget = 76, boundary = 74
    // ========================================================================

    [TestMethod]
    // Early in zone (03:15, effectiveTarget=35, boundary=33)
    [DataRow(20.0, 3, 15, 10.0, State_NormalTOU, Car_Not_Charging, State_ForceCharging, "10% well below boundary → start charging")]
    [DataRow(20.0, 3, 15, 33.0, State_NormalTOU, Car_Not_Charging, State_ForceCharging, "33% at boundary → start charging")]
    [DataRow(20.0, 3, 15, 34.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "34% in hysteresis → don't start")]
    [DataRow(20.0, 3, 15, 34.0, State_ForceCharging, Car_Not_Charging, State_ForceCharging, "34% in hysteresis, already charging → keep going")]
    [DataRow(20.0, 3, 15, 35.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "35% at effective target → NormalTOU")]
    [DataRow(20.0, 3, 15, 35.0, State_ForceCharging, Car_Not_Charging, State_NormalTOU, "35% at effective target, was charging → stop")]
    [DataRow(20.0, 3, 15, 50.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "50% above effective target → NormalTOU (ahead of schedule)")]
    // Midpoint (04:30, effectiveTarget=50, boundary=48)
    [DataRow(20.0, 4, 30, 48.0, State_NormalTOU, Car_Not_Charging, State_ForceCharging, "48% at boundary → start charging")]
    [DataRow(20.0, 4, 30, 49.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "49% in hysteresis → don't start")]
    [DataRow(20.0, 4, 30, 49.0, State_ForceCharging, Car_Not_Charging, State_ForceCharging, "49% in hysteresis, already charging → keep going")]
    [DataRow(20.0, 4, 30, 50.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "50% at effective target → NormalTOU")]
    [DataRow(20.0, 4, 30, 50.0, State_ForceCharging, Car_Not_Charging, State_NormalTOU, "50% at effective target, was charging → stop")]
    [DataRow(20.0, 4, 30, 30.0, State_NormalTOU, Car_Not_Charging, State_ForceCharging, "30% below boundary → ForceCharging (behind schedule)")]
    // Three-quarter way (05:45, effectiveTarget=65, boundary=63)
    [DataRow(20.0, 5, 45, 63.0, State_NormalTOU, Car_Not_Charging, State_ForceCharging, "63% at boundary → start charging")]
    [DataRow(20.0, 5, 45, 64.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "64% in hysteresis → don't start")]
    [DataRow(20.0, 5, 45, 64.0, State_ForceCharging, Car_Not_Charging, State_ForceCharging, "64% in hysteresis, already charging → keep going")]
    [DataRow(20.0, 5, 45, 65.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "65% at effective target → NormalTOU")]
    [DataRow(20.0, 5, 45, 65.0, State_ForceCharging, Car_Not_Charging, State_NormalTOU, "65% at effective target, was charging → stop")]
    // Near end (06:40, effectiveTarget=76, boundary=74)
    [DataRow(20.0, 6, 40, 74.0, State_NormalTOU, Car_Not_Charging, State_ForceCharging, "74% at boundary → start charging")]
    [DataRow(20.0, 6, 40, 75.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "75% in hysteresis → don't start")]
    [DataRow(20.0, 6, 40, 75.0, State_ForceCharging, Car_Not_Charging, State_ForceCharging, "75% in hysteresis, already charging → keep going")]
    [DataRow(20.0, 6, 40, 76.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "76% at effective target → NormalTOU")]
    [DataRow(20.0, 6, 40, 80.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "80% above effective target → NormalTOU (at final target)")]
    // Car charging at midpoint
    [DataRow(20.0, 4, 30, 48.0, State_NormalTOU, Car_Charging, State_ForceCharging, "48% at boundary + car → ForceCharging")]
    [DataRow(20.0, 4, 30, 49.0, State_NormalTOU, Car_Charging, State_Stopped, "49% in hysteresis + car → Stopped")]
    [DataRow(20.0, 4, 30, 50.0, State_NormalTOU, Car_Charging, State_Stopped, "50% at effective target + car → Stopped")]
    public async Task SetBatteryState_GraduatedImportZone_HysteresisAndTiming(
        double initialPercent,
        int hour, int minute,
        double chargePercent,
        string currentState,
        bool isCarCharging,
        string expectedState,
        string reason)
    {
        // Arrange
        ServiceProvider serviceProvider = GetServiceProvider();
        BatteryControlService sut = serviceProvider.GetRequiredService<BatteryControlService>();
        FakeTimeProvider timeProvider = serviceProvider.GetRequiredService<FakeTimeProvider>();
        timeProvider.SetSpecificDateTime(new DateTimeOffset(2025, 1, 15, hour, minute, 0, TimeSpan.Zero));

        BatteryState currentBatteryState = Enum.Parse<BatteryState>(currentState);
        BatteryState expectedBatteryState = Enum.Parse<BatteryState>(expectedState);

        IHomeBattery homeBattery = serviceProvider.GetRequiredService<IHomeBattery>();
        homeBattery.CurrentChargePercent.Returns(chargePercent);
        homeBattery.GetHomeBatteryState().Returns(currentBatteryState);

        ICarCharger carCharger = serviceProvider.GetRequiredService<ICarCharger>();
        carCharger.ChargerCurrent.Returns(isCarCharging ? 10.0 : 0.0);

        IElectricityMeter electricityMeter = serviceProvider.GetRequiredService<IElectricityMeter>();
        electricityMeter.CurrentRatePerKwh.Returns(0.25);

        // Graduated import zone 02:00-07:00, target 80%
        sut.SetCurrentRules(CreateFixedZoneRules("test-graduated-import", 120, 420, BatteryZoneAction.Import, 80, graduatedTarget: true));

        // Pre-set the graduated zone state so the service uses the given initial battery %
        sut.SetGraduatedZoneState("test-graduated-import", initialPercent);

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

    // ========================================================================
    // FindBestZone - Overlap priority resolution
    // ========================================================================

    [TestMethod]
    public void FindBestZone_SmartOverridesFixed()
    {
        List<ResolvedZone> zones =
        [
            new ResolvedZone { RuleId = "fixed", StartMinutes = 0, EndMinutes = 1440, Action = BatteryZoneAction.Export, TargetPercent = 20, IsSmart = false },
            new ResolvedZone { RuleId = "smart", StartMinutes = 0, EndMinutes = 1440, Action = BatteryZoneAction.Export, TargetPercent = 20, IsSmart = true }
        ];

        ResolvedZone? active = BatteryControlService.FindBestZone(zones, 720);
        active.ShouldNotBeNull();
        active.RuleId.ShouldBe("smart");
    }

    [TestMethod]
    public void FindBestZone_ImportOverridesExport_SameSmartness()
    {
        List<ResolvedZone> zones =
        [
            new ResolvedZone { RuleId = "export", StartMinutes = 0, EndMinutes = 1440, Action = BatteryZoneAction.Export, TargetPercent = 20, IsSmart = false },
            new ResolvedZone { RuleId = "import", StartMinutes = 0, EndMinutes = 1440, Action = BatteryZoneAction.Import, TargetPercent = 80, IsSmart = false }
        ];

        ResolvedZone? active = BatteryControlService.FindBestZone(zones, 720);
        active.ShouldNotBeNull();
        active.RuleId.ShouldBe("import");
    }

    [TestMethod]
    public void FindBestZone_HigherTargetPercentWins_SameSmartnessAndAction()
    {
        List<ResolvedZone> zones =
        [
            new ResolvedZone { RuleId = "low", StartMinutes = 0, EndMinutes = 1440, Action = BatteryZoneAction.Import, TargetPercent = 60, IsSmart = false },
            new ResolvedZone { RuleId = "high", StartMinutes = 0, EndMinutes = 1440, Action = BatteryZoneAction.Import, TargetPercent = 100, IsSmart = false }
        ];

        ResolvedZone? active = BatteryControlService.FindBestZone(zones, 720);
        active.ShouldNotBeNull();
        active.RuleId.ShouldBe("high");
    }

    [TestMethod]
    public void FindBestZone_SmartImportOverridesSmartExport()
    {
        List<ResolvedZone> zones =
        [
            new ResolvedZone { RuleId = "smart-export", StartMinutes = 0, EndMinutes = 1440, Action = BatteryZoneAction.Export, TargetPercent = 20, IsSmart = true },
            new ResolvedZone { RuleId = "smart-import", StartMinutes = 0, EndMinutes = 1440, Action = BatteryZoneAction.Import, TargetPercent = 80, IsSmart = true }
        ];

        ResolvedZone? active = BatteryControlService.FindBestZone(zones, 720);
        active.ShouldNotBeNull();
        active.RuleId.ShouldBe("smart-import");
    }

    [TestMethod]
    public void FindBestZone_NonOverlappingZones_SelectedByTime()
    {
        List<ResolvedZone> zones =
        [
            new ResolvedZone { RuleId = "morning", StartMinutes = 0, EndMinutes = 360, Action = BatteryZoneAction.Import, TargetPercent = 100, IsSmart = false },
            new ResolvedZone { RuleId = "evening", StartMinutes = 960, EndMinutes = 1200, Action = BatteryZoneAction.Export, TargetPercent = 20, IsSmart = false }
        ];

        ResolvedZone? morning = BatteryControlService.FindBestZone(zones, 180);
        morning.ShouldNotBeNull();
        morning.RuleId.ShouldBe("morning");

        ResolvedZone? evening = BatteryControlService.FindBestZone(zones, 1000);
        evening.ShouldNotBeNull();
        evening.RuleId.ShouldBe("evening");

        ResolvedZone? gap = BatteryControlService.FindBestZone(zones, 600);
        gap.ShouldBeNull();
    }

    // ========================================================================
    // FindBestZone - Wrapped zone (EndMinutes > 1440)
    // ========================================================================

    [TestMethod]
    public void FindBestZone_WrappedZone_EveningPortion_Matches()
    {
        List<ResolvedZone> zones =
        [
            new ResolvedZone { RuleId = "overnight", StartMinutes = 1410, EndMinutes = 1770, Action = BatteryZoneAction.Import, TargetPercent = 100, IsSmart = false }
        ];

        ResolvedZone? active = BatteryControlService.FindBestZone(zones, 1420);
        active.ShouldNotBeNull();
        active.RuleId.ShouldBe("overnight");
    }

    [TestMethod]
    public void FindBestZone_WrappedZone_MorningPortion_Matches()
    {
        List<ResolvedZone> zones =
        [
            new ResolvedZone { RuleId = "overnight", StartMinutes = 1410, EndMinutes = 1770, Action = BatteryZoneAction.Import, TargetPercent = 100, IsSmart = false }
        ];

        ResolvedZone? active = BatteryControlService.FindBestZone(zones, 200);
        active.ShouldNotBeNull();
        active.RuleId.ShouldBe("overnight");
    }

    [TestMethod]
    public void FindBestZone_WrappedZone_OutsideZone_ReturnsNull()
    {
        List<ResolvedZone> zones =
        [
            new ResolvedZone { RuleId = "overnight", StartMinutes = 1410, EndMinutes = 1770, Action = BatteryZoneAction.Import, TargetPercent = 100, IsSmart = false }
        ];

        ResolvedZone? active = BatteryControlService.FindBestZone(zones, 400);
        active.ShouldBeNull();
    }

    // ========================================================================
    // Graduated target - overnight zone spanning midnight
    //
    // Zone: 23:00-05:00 (1380-1740), 360 min duration, import to 80%
    // No preceding zone → initial percent defaults to 0
    // effectiveTarget(t) = 0 + 80 × elapsed/360
    //   23:30 (1410): elapsed=30,  progress=0.0833, effective=6.67,  boundary=4.67
    //   01:00 (60):   elapsed=120, progress=0.3333, effective=26.67, boundary=24.67
    //   03:00 (180):  elapsed=240, progress=0.6667, effective=53.33
    // ========================================================================

    [TestMethod]
    [DataRow(0.0, 23, 30, 3.0, State_NormalTOU, Car_Not_Charging, State_ForceCharging, "23:30 effective≈6.7, battery at 3% < boundary 4.67 → ForceCharging")]
    [DataRow(0.0, 1, 0, 20.0, State_NormalTOU, Car_Not_Charging, State_ForceCharging, "01:00 effective≈26.7, battery at 20% < boundary 24.67 → ForceCharging")]
    [DataRow(0.0, 3, 0, 70.0, State_NormalTOU, Car_Not_Charging, State_NormalTOU, "03:00 effective≈53.3, battery at 70% > 53.3 → NormalTOU")]
    public async Task SetBatteryState_GraduatedImportZone_Overnight_HysteresisAndTiming(
        double initialPercent,
        int hour, int minute,
        double chargePercent,
        string currentState,
        bool isCarCharging,
        string expectedState,
        string reason)
    {
        // Arrange
        ServiceProvider serviceProvider = GetServiceProvider();
        BatteryControlService sut = serviceProvider.GetRequiredService<BatteryControlService>();
        FakeTimeProvider timeProvider = serviceProvider.GetRequiredService<FakeTimeProvider>();
        timeProvider.SetSpecificDateTime(new DateTimeOffset(2025, 1, 15, hour, minute, 0, TimeSpan.Zero));

        BatteryState currentBatteryState = Enum.Parse<BatteryState>(currentState);
        BatteryState expectedBatteryState = Enum.Parse<BatteryState>(expectedState);

        IHomeBattery homeBattery = serviceProvider.GetRequiredService<IHomeBattery>();
        homeBattery.CurrentChargePercent.Returns(chargePercent);
        homeBattery.GetHomeBatteryState().Returns(currentBatteryState);

        ICarCharger carCharger = serviceProvider.GetRequiredService<ICarCharger>();
        carCharger.ChargerCurrent.Returns(isCarCharging ? 10.0 : 0.0);

        IElectricityMeter electricityMeter = serviceProvider.GetRequiredService<IElectricityMeter>();
        electricityMeter.CurrentRatePerKwh.Returns(0.25);

        // Graduated import zone 23:00-05:00 (wraps midnight), target 80%
        sut.SetCurrentRules(CreateFixedZoneRules("overnight", 1380, 300, BatteryZoneAction.Import, 80, graduatedTarget: true));

        // Pre-set the graduated zone state so the service uses the given initial battery %
        sut.SetGraduatedZoneState("overnight", initialPercent);

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

    private static BatteryZoneRules CreateFixedZoneRules(
        string ruleId, int startMinutes, int endMinutes,
        BatteryZoneAction action, int targetPercent,
        bool graduatedTarget = false)
    {
        return new BatteryZoneRules
        {
            Rules =
            [
                new BatteryZoneRule
                {
                    Id = ruleId,
                    StartTime = new TimeDefinition { Type = TimeDefinitionType.FixedTime, FixedTimeMinutes = startMinutes },
                    EndTime = new TimeDefinition { Type = TimeDefinitionType.FixedTime, FixedTimeMinutes = endMinutes },
                    Action = action,
                    TargetPercent = targetPercent,
                    GraduatedTarget = graduatedTarget
                }
            ]
        };
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

        ServiceProvider provider = services.BuildServiceProvider();
        provider.GetRequiredService<BatteryControlService>().MarkAsInitialized();
        return provider;
    }
}

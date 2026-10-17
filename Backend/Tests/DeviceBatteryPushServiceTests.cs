using HomeAssistant.Services;
using Shouldly;

namespace HomeAssistant.Tests;

/// <summary>
/// The daily battery push selects which Home Assistant sensors count as replaceable cells.
/// Both ways of getting that wrong are silent: too narrow and a device quietly never appears,
/// too broad and the list fills with phones and the car. The entity IDs below are the real
/// ones from this house, read from /api/states on 2026-10-17.
/// </summary>
[TestClass]
public sealed class DeviceBatteryPushServiceTests
{
    [TestMethod]
    [DataRow("sensor.bedroom_1_radiator_battery", "a TRV's AAs")]
    [DataRow("sensor.kitchen_temperature_and_humidity_battery", "a room thermometer")]
    [DataRow("sensor.ewelink_snzb_01p_battery", "a button")]
    [DataRow("sensor.sonoff_snzb_05p_battery", "the flood detector")]
    [DataRow("sensor.tz3210_j4pdtz9v_ts0001_battery", "the boiler fingerbot")]
    [DataRow("sensor.bedroom_3_temperature_and_humidity_battery", "the bathroom sensor, despite its stale name")]
    public void IncludesReplaceableCells(string entityId, string what)
    {
        DeviceBatteryPushService.IsReplaceableBatterySensor(entityId, "battery")
            .ShouldBeTrue($"should include {what}");
    }

    [TestMethod]
    [DataRow("sensor.sonoff_snzb_02d_battery")]
    [DataRow("sensor.sonoff_snzb_02d_battery_2")]
    [DataRow("sensor.sonoff_snzb_02d_battery_3")]
    public void IncludesNumberSuffixedDuplicates(string entityId)
    {
        // The living room and dining room thermometers are _2 and _3. A plain "ends with
        // _battery" test drops both, leaving five of seven room thermometers monitored and
        // nothing to say the other two are missing.
        DeviceBatteryPushService.IsReplaceableBatterySensor(entityId, "battery").ShouldBeTrue();
    }

    [TestMethod]
    [DataRow("sensor.kevins_iphone_battery_level", "a phone")]
    [DataRow("sensor.ellie_s_phone_battery_level", "a phone")]
    [DataRow("sensor.solax_inverter_battery_capacity", "the home battery")]
    [DataRow("sensor.solax_inverter_battery_total_capacity", "the home battery")]
    [DataRow("sensor.mg_mg4_electric_state_of_charge", "the car")]
    [DataRow("sensor.mg_mg4_electric_target_soc", "the car")]
    public void ExcludesThingsNobodyPutsACellIn(string entityId, string what)
    {
        // All of these carry device_class=battery, which is why device_class alone is not
        // enough to decide this.
        DeviceBatteryPushService.IsReplaceableBatterySensor(entityId, "battery")
            .ShouldBeFalse($"should exclude {what}");
    }

    [TestMethod]
    public void ExcludesNonSensorDomains()
    {
        // The Octopus intelligent charge targets are number entities with device_class=battery.
        DeviceBatteryPushService.IsReplaceableBatterySensor(
            "number.octopus_energy_00000000_intelligent_charge_target", "battery").ShouldBeFalse();
    }

    [TestMethod]
    public void ExcludesSensorsThatAreNotBatteries()
    {
        DeviceBatteryPushService.IsReplaceableBatterySensor("sensor.kitchen_temperature", "temperature").ShouldBeFalse();
        DeviceBatteryPushService.IsReplaceableBatterySensor("sensor.some_battery", null).ShouldBeFalse();
    }

    [TestMethod]
    public void HandlesMissingEntityId()
    {
        DeviceBatteryPushService.IsReplaceableBatterySensor(null, "battery").ShouldBeFalse();
        DeviceBatteryPushService.IsReplaceableBatterySensor("", "battery").ShouldBeFalse();
    }

    [TestMethod]
    [DataRow("100.0", 100.0)]
    [DataRow("22.0", 22.0)]
    [DataRow("95", 95.0)]
    public void ParsesReportedPercentages(string state, double expected)
    {
        DeviceBatteryPushService.ParsePercent(state).ShouldBe(expected);
        DeviceBatteryPushService.IsAvailable(state).ShouldBeTrue();
    }

    [TestMethod]
    [DataRow("unavailable")]
    [DataRow("unknown")]
    [DataRow("")]
    [DataRow(null)]
    public void TreatsNonReadingsAsUnavailableRatherThanFlat(string? state)
    {
        // This is the distinction the whole record depends on. The fingerbot's sensor reads
        // "unavailable" because its cell is dead and the device is off the mesh - recording
        // that as 0% would be a reading the device never gave, and a run of unavailable days
        // is the actual signal worth seeing.
        DeviceBatteryPushService.ParsePercent(state).ShouldBeNull();
        DeviceBatteryPushService.IsAvailable(state).ShouldBeFalse();
    }

    [TestMethod]
    public void DoesNotReadAPercentageOutOfAnArbitraryString()
    {
        DeviceBatteryPushService.ParsePercent("low").ShouldBeNull();
        DeviceBatteryPushService.ParsePercent("100%").ShouldBeNull();
    }
}

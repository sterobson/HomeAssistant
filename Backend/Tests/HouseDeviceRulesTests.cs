using HomeAssistant.Services.HouseDevices;
using HomeAssistant.Shared;
using Shouldly;

namespace HomeAssistant.Tests;

/// <summary>
/// What the 3D house view shows and may control. The view can switch real plugs from a phone, so these pin that it
/// can only reach the house's own plugs, lights and buttons, and never a device the app drives by itself.
/// </summary>
[TestClass]
public class HouseDeviceRulesTests
{
    private static readonly Dictionary<string, string> NoOwners = new();

    private static HouseEntityReading Entity(string entityId, string? state = "on", string? platform = "zha") =>
        new(entityId, entityId, state, null, null, platform);

    private static HouseDeviceReading Device(string id, string? area, params HouseEntityReading[] entities) =>
        new(id, id, "SONOFF", "S60ZBTPG", area, entities);

    [TestMethod]
    public void AZigbeePlug_CanBeSwitched()
    {
        List<HouseDeviceDto> devices = HouseDeviceRules.Describe(
            [Device("plug", "Games Room", Entity("switch.den_plug_tv_switch"), Entity("sensor.den_plug_tv_power", "35"))], NoOwners);

        HouseDeviceEntityDto plug = devices.Single().Entities.Single(e => e.EntityId == "switch.den_plug_tv_switch");
        plug.Actions.ShouldBe([HouseDeviceActions.TurnOn, HouseDeviceActions.TurnOff, HouseDeviceActions.Toggle]);
        plug.ControlledBy.ShouldBeNull();
        devices.Single().Entities.Single(e => e.Domain == "sensor").Actions.ShouldBeEmpty();
    }

    [TestMethod]
    public void AButton_CanOnlyBePressed()
    {
        HouseDeviceEntityDto button = HouseDeviceRules.Describe(
            [Device("plug", "Kitchen", Entity("button.kitchen_plug_identify", "unknown"))], NoOwners).Single().Entities.Single();

        button.Actions.ShouldBe([HouseDeviceActions.Press]);
    }

    [TestMethod]
    public void ADeviceTheAppDrives_IsShownButNotControllable_EvenItsOtherEntities()
    {
        Dictionary<string, string> owners = new() { ["switch.games_room_plug_heater_switch"] = "Switched by the heating." };

        HouseDeviceDto heater = HouseDeviceRules.Describe(
            [Device("heater", "Games Room", Entity("switch.games_room_plug_heater_switch"), Entity("button.games_room_plug_heater_identify", "unknown"))],
            owners).Single();

        heater.Entities.ShouldAllBe(e => e.Actions.Count == 0);
        heater.Entities.ShouldAllBe(e => e.ControlledBy == "Switched by the heating.");
    }

    [TestMethod]
    public void ARadiatorValve_BelongsToTheHeating()
    {
        HouseDeviceDto valve = HouseDeviceRules.Describe(
            [Device("trv", "Games Room", Entity("climate.den_radiator_1_thermostat", "heat"), Entity("switch.den_radiator_1_child_lock", "off"))],
            NoOwners).Single();

        HouseDeviceEntityDto childLock = valve.Entities.Single(e => e.Domain == "switch");
        childLock.Actions.ShouldBeEmpty();
        childLock.ControlledBy.ShouldBe(HouseDeviceRules.ValveReason);
    }

    [TestMethod]
    public void OtherIntegrations_CanOnlyBeViewed()
    {
        HouseDeviceDto car = HouseDeviceRules.Describe(
            [Device("car", "Porch", Entity("switch.mg4_charging", "on", platform: "saic_ismart"), Entity("button.router_reboot", "unknown", platform: "linksys_velop"))],
            NoOwners).Single();

        car.Entities.ShouldAllBe(e => e.Actions.Count == 0 && e.ControlledBy != null);
    }

    [TestMethod]
    public void UpdatesAndUnavailableEntities_AreLeftOut_AndADeviceWithNothingElseIsNotShown()
    {
        List<HouseDeviceDto> devices = HouseDeviceRules.Describe(
        [
            Device("addon", null, Entity("update.netdaemon", "off")),
            Device("plug", "Kitchen", Entity("update.kitchen_plug_firmware", "off"), Entity("switch.kitchen_plug", "off"), Entity("sensor.disabled", state: null))
        ], NoOwners);

        devices.Single().Id.ShouldBe("plug");
        devices.Single().Entities.Select(e => e.EntityId).ShouldBe(["switch.kitchen_plug"]);
    }

    [TestMethod]
    public void ACommand_IsRefusedUnlessTheSnapshotOffersIt()
    {
        Dictionary<string, string> owners = new() { ["switch.tz3210_j4pdtz9v_ts0001"] = "Pressed only by the heating." };
        List<HouseDeviceDto> devices = HouseDeviceRules.Describe(
        [
            Device("tv", "Games Room", Entity("switch.den_plug_tv_switch")),
            Device("fingerbot", null, Entity("switch.tz3210_j4pdtz9v_ts0001"))
        ], owners);

        HouseDeviceRules.RefuseCommand(devices, "switch.den_plug_tv_switch", HouseDeviceActions.Toggle).ShouldBeNull();
        HouseDeviceRules.RefuseCommand(devices, "switch.den_plug_tv_switch", HouseDeviceActions.Press).ShouldNotBeNull();
        HouseDeviceRules.RefuseCommand(devices, "switch.tz3210_j4pdtz9v_ts0001", HouseDeviceActions.TurnOn).ShouldBe("Pressed only by the heating.");
        HouseDeviceRules.RefuseCommand(devices, "switch.not_there", HouseDeviceActions.TurnOn).ShouldNotBeNull();
    }
}

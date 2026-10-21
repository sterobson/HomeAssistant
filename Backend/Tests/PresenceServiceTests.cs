using HomeAssistant.apps;
using HomeAssistant.Services;
using HomeAssistant.Shared.Climate;
using Microsoft.Extensions.Logging;
using NetDaemon.HassModel;
using NetDaemon.HassModel.Entities;
using NSubstitute;
using Shouldly;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;

namespace HomeAssistant.Tests;

/// <summary>
/// Room presence comes from plug power draw: the desk plugs for the two desk zones, and the TV for
/// the den (since 2026-10-19). These drive the real <see cref="PresenceService"/> against a faked
/// Home Assistant, because the heating tests all use <see cref="MockPresenceService"/> and so never
/// exercise the thresholds themselves.
/// </summary>
[TestClass]
public sealed class PresenceServiceTests
{
    private const string DenTvSwitch = "switch.den_plug_tv_switch";
    private const string DenTvPower = "sensor.den_plug_tv_power";
    private const string GamesDeskSwitch = "switch.games_room_desk_plug";
    private const string GamesDeskPower = "sensor.games_room_desk_plug_power";

    [TestMethod]
    [DataRow(45.0, true, DisplayName = "TV on")]
    [DataRow(30.0, true, DisplayName = "exactly the threshold counts")]
    [DataRow(29.0, false, DisplayName = "just under")]
    [DataRow(4.0, false, DisplayName = "standby")]
    public async Task Den_IsInUseWhileTheTvDrawsAtLeast30Watts(double watts, bool expected)
    {
        PresenceTestContext context = PresenceTestContext.Create();
        context.History[DenTvPower] = [watts];

        bool inUse = await context.Service.IsRoomInUse("den");

        inUse.ShouldBe(expected);
    }

    [TestMethod]
    public async Task Den_StaysInUseForTheLookBackAfterTheTvGoesToStandby()
    {
        // The history window still holds the earlier on-reading, which is the deliberate hold.
        PresenceTestContext context = PresenceTestContext.Create();
        context.History[DenTvPower] = [45, 4];

        (await context.Service.IsRoomInUse("den")).ShouldBeTrue();
    }

    [TestMethod]
    public async Task Den_GoesByPowerAlone_NotTheTvPlugSwitch()
    {
        // Only the draw matters (owner, 2026-10-19). A switched-off plug reads 0 W anyway, so the switch
        // state is not read at all, and is not listed as a presence device.
        PresenceTestContext context = PresenceTestContext.Create();
        context.SetSwitch(DenTvSwitch, "off");
        context.History[DenTvPower] = [45];

        (await context.Service.IsRoomInUse("den")).ShouldBeTrue();
    }

    [TestMethod]
    public async Task Den_IsNotInUseWithNoPowerHistory()
    {
        PresenceTestContext context = PresenceTestContext.Create();

        (await context.Service.IsRoomInUse("den")).ShouldBeFalse();
    }

    [TestMethod]
    public async Task GamesDesk_StillNeeds40Watts()
    {
        // The TVs' and Ruth's desk's lower threshold must not leak into Ste's desk.
        PresenceTestContext context = PresenceTestContext.Create();
        context.SetSwitch(GamesDeskSwitch, "on");
        context.History[GamesDeskPower] = [35];

        (await context.Service.IsRoomInUse("games desk")).ShouldBeFalse();

        context.History[GamesDeskPower] = [40];
        (await context.Service.IsRoomInUse("games desk")).ShouldBeTrue();
    }

    [TestMethod]
    public void Den_CanDetectPresence()
    {
        PresenceTestContext context = PresenceTestContext.Create();

        context.Service.CanDetectIfRoomInUse("den").ShouldBeTrue();
        context.Service.CanDetectIfRoomInUse(" Den ").ShouldBeTrue("room names are matched trimmed and case-insensitively");
    }

    // Ruth's desk plug gives two detectors (owner, 2026-10-19): her desk zone is in use while the plug is on and
    // drawing 30 W for the whole of the last 5 s, and the dining room zone while the plug is merely switched on.
    // The 5 s is measured from live readings (SustainedCondition), never from the history API, which lags (gotcha 21).

    private const string DiningDeskSwitch = "switch.dining_room_desk_plug";
    private const string DiningDeskPower = "sensor.dining_room_desk_plug_power";

    [TestMethod]
    public async Task DiningDesk_IsInUseOnceThePlugIsOnAndHasDrawn30WattsFor5Seconds()
    {
        PresenceTestContext context = PresenceTestContext.Create();
        context.SetPlug(DiningDeskSwitch, on: true);
        context.SetPower(DiningDeskPower, 45);

        (await context.Service.IsRoomInUse("dining desk")).ShouldBeFalse("only just started drawing");
        context.Advance(TimeSpan.FromSeconds(4.9));
        (await context.Service.IsRoomInUse("dining desk")).ShouldBeFalse("not yet 5 s");
        context.Advance(TimeSpan.FromSeconds(0.1));
        (await context.Service.IsRoomInUse("dining desk")).ShouldBeTrue();
    }

    [TestMethod]
    [DataRow(30.0, true, DisplayName = "exactly the threshold counts")]
    [DataRow(29.0, false, DisplayName = "just under")]
    [DataRow(4.0, false, DisplayName = "on but idle")]
    public async Task DiningDesk_NeedsAtLeast30Watts(double watts, bool expected)
    {
        PresenceTestContext context = PresenceTestContext.Create();
        context.SetPlug(DiningDeskSwitch, on: true);
        context.SetPower(DiningDeskPower, watts);
        context.Advance(TimeSpan.FromSeconds(10));

        (await context.Service.IsRoomInUse("dining desk")).ShouldBe(expected);
    }

    [TestMethod]
    public async Task DiningDesk_EndsAsSoonAsTheDrawDrops_WithNoHold()
    {
        PresenceTestContext context = PresenceTestContext.Create();
        context.SetPlug(DiningDeskSwitch, on: true);
        context.SetPower(DiningDeskPower, 45);
        context.Advance(TimeSpan.FromSeconds(10));
        (await context.Service.IsRoomInUse("dining desk")).ShouldBeTrue();

        context.SetPower(DiningDeskPower, 4);

        (await context.Service.IsRoomInUse("dining desk")).ShouldBeFalse();
    }

    [TestMethod]
    public async Task DiningDesk_SwitchedOffIsNotInUse_WhateverTheLastReadingWas()
    {
        PresenceTestContext context = PresenceTestContext.Create();
        context.SetPlug(DiningDeskSwitch, on: true);
        context.SetPower(DiningDeskPower, 45);
        context.Advance(TimeSpan.FromSeconds(10));

        context.SetPlug(DiningDeskSwitch, on: false);

        (await context.Service.IsRoomInUse("dining desk")).ShouldBeFalse();
    }

    [TestMethod]
    public async Task DiningDesk_AStaleReadingFromBeforeThePlugWasSwitchedOnDoesNotCount()
    {
        // The plug keeps its last power reading while it is off (HOME-ASSISTANT.md, gotcha 21): 32 W sat there for
        // hours, then the plug was switched on and the heater came on at once on the strength of it.
        PresenceTestContext context = PresenceTestContext.Create();
        context.SetPower(DiningDeskPower, 32);
        context.Advance(TimeSpan.FromHours(3));

        context.SetPlug(DiningDeskSwitch, on: true);

        (await context.Service.IsRoomInUse("dining desk")).ShouldBeFalse("the plug has only just come on");

        context.SetPower(DiningDeskPower, 0);
        context.Advance(TimeSpan.FromSeconds(10));
        (await context.Service.IsRoomInUse("dining desk")).ShouldBeFalse("and it reports nothing is drawing");
    }

    [TestMethod]
    public async Task DiningDesk_ALoadPulsingAroundThe30WattMark_IsNeverInUse()
    {
        // Ruth's desk on 2026-10-20 at 12:00 BST: about 450 W for 4 s, then 5 W for 5 s, over and over. Each burst was
        // judged just after it began (the history API had not yet recorded the drop that followed), so the heater
        // went on and off five times in two minutes.
        PresenceTestContext context = PresenceTestContext.Create();
        context.SetPlug(DiningDeskSwitch, on: true);

        for (int cycle = 0; cycle < 6; cycle++)
        {
            context.SetPower(DiningDeskPower, 450);
            context.Advance(TimeSpan.FromSeconds(4));
            (await context.Service.IsRoomInUse("dining desk")).ShouldBeFalse($"burst {cycle}, still on after 4 s");

            context.SetPower(DiningDeskPower, 5);
            context.Advance(TimeSpan.FromSeconds(5));
            (await context.Service.IsRoomInUse("dining desk")).ShouldBeFalse($"gap {cycle}");
        }
    }

    [TestMethod]
    public async Task DiningDesk_ADipBetweenTwoChecksBreaksTheRun()
    {
        // 45 W, a 1 s dip to 4 W, 45 W again: 8 s in all, but never 5 s unbroken, and nothing asked in between.
        PresenceTestContext context = PresenceTestContext.Create();
        context.SetPlug(DiningDeskSwitch, on: true);
        context.SetPower(DiningDeskPower, 45);
        context.Advance(TimeSpan.FromSeconds(3));
        context.SetPower(DiningDeskPower, 4);
        context.Advance(TimeSpan.FromSeconds(1));
        context.SetPower(DiningDeskPower, 45);
        context.Advance(TimeSpan.FromSeconds(4));

        (await context.Service.IsRoomInUse("dining desk")).ShouldBeFalse();

        context.Advance(TimeSpan.FromSeconds(1));
        (await context.Service.IsRoomInUse("dining desk")).ShouldBeTrue();
    }

    [TestMethod]
    public async Task DiningDesk_AnUnavailableReadingBreaksTheRun()
    {
        PresenceTestContext context = PresenceTestContext.Create();
        context.SetPlug(DiningDeskSwitch, on: true);
        context.SetPower(DiningDeskPower, 45);
        context.Advance(TimeSpan.FromSeconds(10));

        context.SetPowerState(DiningDeskPower, "unavailable");

        (await context.Service.IsRoomInUse("dining desk")).ShouldBeFalse();
    }

    [TestMethod]
    [DataRow("on", true)]
    [DataRow("off", false)]
    public async Task DiningRoom_IsInUseWhileRuthsDeskPlugIsSwitchedOn_WhateverItDraws(string plug, bool expected)
    {
        PresenceTestContext context = PresenceTestContext.Create();
        context.SetSwitch(DiningDeskSwitch, plug);
        context.SetPower(DiningDeskPower, 0);

        (await context.Service.IsRoomInUse("dining room")).ShouldBe(expected);
    }

    [TestMethod]
    public void DiningRoom_ListsOnlyTheDeskPlugSwitchAsItsPresenceDevice()
    {
        PresenceTestContext context = PresenceTestContext.Create();

        context.Service.CanDetectIfRoomInUse("dining room").ShouldBeTrue();
        context.Service.GetPresenceDevices("dining room").ShouldBe([(DiningDeskSwitch, RoomDeviceRoles.DeskPlug)]);
    }

    [TestMethod]
    public async Task House_ADeskSwitchedOnIsNotSomeoneHome()
    {
        // Ruth's desk plug on but idle: the dining room zone is in use, but nobody need be home.
        PresenceTestContext context = PresenceTestContext.Create();
        context.SetSwitch(DiningDeskSwitch, "on");
        context.SetPower(DiningDeskPower, 3);

        PresenceSnapshot snapshot = await context.Service.GetSnapshotAsync();

        snapshot.Zones["dining room"].ShouldBeTrue();
        snapshot.Zones["dining desk"].ShouldBeFalse();
        snapshot.SomeoneHome.ShouldBeFalse();
    }

    [TestMethod]
    public async Task House_IsAnyDetectorThatShowsSomeoneIsThere_WhicheverZoneHasIt()
    {
        // Even with no zone using the den TV's detector, the TV being on means someone is home.
        PresenceTestContext context = PresenceTestContext.Create();
        context.Service.UseZoneOccupancy(new Dictionary<string, List<string>> { ["den"] = [] });
        context.SetPower(DenTvPower, 45);
        context.Advance(TimeSpan.FromSeconds(6));

        PresenceSnapshot snapshot = await context.Service.GetSnapshotAsync();

        snapshot.Zones["den"].ShouldBeFalse("the den was configured with no detectors");
        snapshot.SomeoneHome.ShouldBeTrue();
    }

    // The den TV has two detectors (owner, 2026-10-19): on recently (5-minute hold), and on now (5 s, no hold).

    [TestMethod]
    public async Task DenTv_OnRecentlyAndOnNow()
    {
        // "Recently" reads the 5-minute history, "now" the live readings.
        PresenceTestContext context = PresenceTestContext.Create();
        context.History[DenTvPower] = [45];
        context.SetPower(DenTvPower, 45);

        PresenceSnapshot justSwitchedOn = await context.Service.GetSnapshotAsync();
        justSwitchedOn.Detectors[PresenceService.DenTvOn].ShouldBeTrue();
        justSwitchedOn.Detectors[PresenceService.DenTvOnNow].ShouldBeFalse("only just switched on: not yet 5 s");

        context.Advance(TimeSpan.FromSeconds(5));
        PresenceSnapshot on = await context.Service.GetSnapshotAsync();
        on.Detectors[PresenceService.DenTvOn].ShouldBeTrue();
        on.Detectors[PresenceService.DenTvOnNow].ShouldBeTrue();

        context.History[DenTvPower] = [45, 4];
        context.SetPower(DenTvPower, 4);
        PresenceSnapshot justOff = await context.Service.GetSnapshotAsync();
        justOff.Detectors[PresenceService.DenTvOn].ShouldBeTrue("the 5-minute hold");
        justOff.Detectors[PresenceService.DenTvOnNow].ShouldBeFalse("no hold");
    }

    [TestMethod]
    public async Task DenTv_Standby_IsNeitherOnRecentlyNorOnNow()
    {
        PresenceTestContext context = PresenceTestContext.Create();
        context.History[DenTvPower] = [4];
        context.SetPower(DenTvPower, 4);
        context.Advance(TimeSpan.FromSeconds(10));

        PresenceSnapshot snapshot = await context.Service.GetSnapshotAsync();

        snapshot.Detectors[PresenceService.DenTvOn].ShouldBeFalse();
        snapshot.Detectors[PresenceService.DenTvOnNow].ShouldBeFalse();
    }

    [TestMethod]
    public void TheDen_UsesOnRecentlyForTheHeatingByDefault()
    {
        PresenceTestContext context = PresenceTestContext.Create();

        context.Service.DetectorsFor("den").ShouldBe([PresenceService.DenTvOn]);
    }

    [TestMethod]
    public async Task House_CountsTheDenTvOnlyWhileItIsOnNow_NotForItsHold()
    {
        // Turned off a moment ago: the den is still in use for its hold, but that is not someone being home.
        PresenceTestContext context = PresenceTestContext.Create();
        context.History[DenTvPower] = [45, 4];
        context.SetPower(DenTvPower, 4);

        PresenceSnapshot snapshot = await context.Service.GetSnapshotAsync();

        snapshot.Zones["den"].ShouldBeTrue();
        snapshot.SomeoneHome.ShouldBeFalse();
        context.Service.Detectors.Single(d => d.Id == PresenceService.DenTvOn).MeansSomeoneHome.ShouldBeFalse();
        context.Service.Detectors.Single(d => d.Id == PresenceService.DenTvOnNow).MeansSomeoneHome.ShouldBeTrue();
    }

    // Which detectors count for each zone is configurable (owner, 2026-10-19).

    [TestMethod]
    public void TheDefaults_AreWhatEachZoneHadBeforeItWasConfigurable()
    {
        PresenceTestContext context = PresenceTestContext.Create();

        context.Service.ZoneOccupancyIsDefault.ShouldBeTrue();
        context.Service.DetectorsFor("games desk").ShouldBe([PresenceService.GamesDeskInUse]);
        context.Service.DetectorsFor("dining desk").ShouldBe([PresenceService.DiningDeskInUse]);
        context.Service.DetectorsFor("dining room").ShouldBe([PresenceService.DiningDeskSwitchedOn]);
        context.Service.DetectorsFor("den").ShouldBe([PresenceService.DenTvOn]);
        context.Service.DetectorsFor("living room").ShouldBe([PresenceService.LivingRoomTvOn, PresenceService.LivingRoomLightsOn]);
        context.Service.DetectorsFor("kitchen").ShouldBeEmpty();
    }

    [TestMethod]
    public async Task AZoneCanBeGivenAnotherZonesDetectors()
    {
        // The kitchen counts as in use while the living room's lights are on, once configured so.
        PresenceTestContext context = PresenceTestContext.Create();
        context.SetSwitch(StickLightsSwitch, "on");
        context.Service.UseZoneOccupancy(new Dictionary<string, List<string>> { ["kitchen"] = [PresenceService.LivingRoomLightsOn] });

        (await context.Service.IsRoomInUse("kitchen")).ShouldBeTrue();
        context.Service.CanDetectIfRoomInUse("kitchen").ShouldBeTrue();
        context.Service.GetPresenceDevices("kitchen").ShouldBe([(StickLightsSwitch, RoomDeviceRoles.LightsPlug)]);
    }

    [TestMethod]
    public async Task OnceSaved_AZoneLeftOutHasNoPresence()
    {
        PresenceTestContext context = PresenceTestContext.Create();
        context.History[DenTvPower] = [45];
        context.Service.UseZoneOccupancy(new Dictionary<string, List<string>> { ["kitchen"] = [PresenceService.DenTvOn] });

        context.Service.ZoneOccupancyIsDefault.ShouldBeFalse();
        (await context.Service.IsRoomInUse("den")).ShouldBeFalse("the saved choice is the whole truth");
        context.Service.CanDetectIfRoomInUse("den").ShouldBeFalse();
        context.Service.PresenceZones.ShouldBe(["kitchen"]);
    }

    [TestMethod]
    public void UnknownDetectorIds_AreIgnored()
    {
        PresenceTestContext context = PresenceTestContext.Create();
        context.Service.UseZoneOccupancy(new Dictionary<string, List<string>> { ["den"] = ["no-such-detector", PresenceService.DenTvOn] });

        context.Service.DetectorsFor("den").ShouldBe([PresenceService.DenTvOn]);
    }

    [TestMethod]
    public void EveryDetector_IsOfferedWithItsLabel()
    {
        PresenceTestContext context = PresenceTestContext.Create();

        context.Service.Detectors.Select(d => d.Id).ShouldBe([
            PresenceService.GamesDeskInUse, PresenceService.DiningDeskInUse, PresenceService.DiningDeskSwitchedOn,
            PresenceService.DenTvOn, PresenceService.DenTvOnNow, PresenceService.LivingRoomTvOn, PresenceService.LivingRoomLightsOn,
            PresenceService.StePhoneHome, PresenceService.RuthPhoneHome, PresenceService.EllePhoneHome
        ]);
        context.Service.Detectors.Single(d => d.Id == PresenceService.DiningDeskSwitchedOn).MeansSomeoneHome.ShouldBeFalse();
        context.Service.Detectors.Single(d => d.Id == PresenceService.StePhoneHome).MeansSomeoneHome.ShouldBeFalse();
        context.Service.Detectors.Single(d => d.Id == PresenceService.RuthPhoneHome).MeansSomeoneHome.ShouldBeFalse();
        context.Service.Detectors.Single(d => d.Id == PresenceService.EllePhoneHome).MeansSomeoneHome.ShouldBeFalse();
        context.Service.Detectors.ShouldAllBe(d => d.Label.Length > 0);
    }

    [TestMethod]
    public void Den_ListsOnlyTheTvPowerAsItsPresenceDevice()
    {
        PresenceTestContext context = PresenceTestContext.Create();

        context.Service.GetPresenceDevices("den").ShouldBe([(DenTvPower, RoomDeviceRoles.TvPower)]);
    }

    private const string LivingTvSwitch = "switch.living_room_plug_tv_switch";
    private const string LivingTvPower = "sensor.living_room_plug_tv_power";
    private const string StickLightsSwitch = "switch.livingroomlighttreeplug";

    [TestMethod]
    public async Task LivingRoom_IsInUseWhileTheStickLightsAreOn_WhateverTheTvDoes()
    {
        PresenceTestContext context = PresenceTestContext.Create();
        context.SetSwitch(StickLightsSwitch, "on");
        context.SetPower(LivingTvPower, 0);

        (await context.Service.IsRoomInUse("living room")).ShouldBeTrue();
    }

    [TestMethod]
    public async Task LivingRoom_TvMustDraw30WattsForTheWholeLast5Seconds()
    {
        PresenceTestContext context = PresenceTestContext.Create();
        context.SetSwitch(StickLightsSwitch, "off");
        context.SetPower(LivingTvPower, 45);

        (await context.Service.IsRoomInUse("living room")).ShouldBeFalse("only just switched on: not yet 5 s");
        context.Advance(TimeSpan.FromSeconds(5));
        (await context.Service.IsRoomInUse("living room")).ShouldBeTrue();

        context.SetPower(LivingTvPower, 4);
        (await context.Service.IsRoomInUse("living room")).ShouldBeFalse("just turned off: no hold");
    }

    [TestMethod]
    [DataRow(30.0, true, DisplayName = "exactly the threshold counts")]
    [DataRow(29.0, false, DisplayName = "just under")]
    [DataRow(4.0, false, DisplayName = "standby")]
    public async Task LivingRoom_TvThresholdIs30Watts(double watts, bool expected)
    {
        PresenceTestContext context = PresenceTestContext.Create();
        context.SetSwitch(StickLightsSwitch, "off");
        context.SetPower(LivingTvPower, watts);
        context.Advance(TimeSpan.FromSeconds(10));

        (await context.Service.IsRoomInUse("living room")).ShouldBe(expected);
    }

    [TestMethod]
    public async Task LivingRoom_GoesByTvPowerAlone_NotTheTvPlugSwitch()
    {
        PresenceTestContext context = PresenceTestContext.Create();
        context.SetSwitch(StickLightsSwitch, "off");
        context.SetSwitch(LivingTvSwitch, "off");
        context.SetPower(LivingTvPower, 45);
        context.Advance(TimeSpan.FromSeconds(10));

        (await context.Service.IsRoomInUse("living room")).ShouldBeTrue("the switch is not read; the 45 W draw is");

        context.SetPower(LivingTvPower, 4);
        (await context.Service.IsRoomInUse("living room")).ShouldBeFalse("standby, and the lights are off");
    }

    [TestMethod]
    public void LivingRoom_ListsTheTvPowerAndTheStickLightsAsPresenceDevices()
    {
        PresenceTestContext context = PresenceTestContext.Create();

        context.Service.CanDetectIfRoomInUse("living room").ShouldBeTrue();
        context.Service.GetPresenceDevices("living room").ShouldBe(
        [
            (LivingTvPower, RoomDeviceRoles.TvPower),
            (StickLightsSwitch, RoomDeviceRoles.LightsPlug)
        ]);
    }

    [TestMethod]
    public async Task APhone_IsHomeWhileTheRouterListsItOnline_WhicheverUnitItIsOn()
    {
        PresenceTestContext context = PresenceTestContext.Create();
        context.SetOnlineDevices(
            """{ "name": "unknown", "id": "CE8B9C19-DD29-4250-8B90-6FCCF8CEF4B9", "ip": null, "parent_name": "Child node 1" }""");

        PresenceSnapshot snapshot = await context.Service.GetSnapshotAsync();

        snapshot.Detectors[PresenceService.StePhoneHome].ShouldBeTrue("listed, even with no IP, on a secondary unit, and in another case");
        snapshot.Detectors[PresenceService.EllePhoneHome].ShouldBeFalse("not listed");
    }

    [TestMethod]
    public async Task APhone_IsAway_WhenTheRouterListIsMissingOrUnreadable()
    {
        PresenceTestContext context = PresenceTestContext.Create();
        (await context.Service.GetSnapshotAsync()).Detectors[PresenceService.StePhoneHome].ShouldBeFalse("no list at all");

        context.Ha.GetState(PresenceService.RouterOnlineDevices).Returns(new EntityState
        {
            EntityId = PresenceService.RouterOnlineDevices,
            State = "unavailable",
            AttributesJson = JsonDocument.Parse("""{ "friendly_name": "Mesh Online Devices" }""").RootElement
        });
        (await context.Service.GetSnapshotAsync()).Detectors[PresenceService.StePhoneHome].ShouldBeFalse("a list with no devices");
    }

    [TestMethod]
    public async Task Phones_DoNotCountTowardsHouse_AndNoZoneUsesThemUntilChosen()
    {
        PresenceTestContext context = PresenceTestContext.Create();
        context.SetOnlineDevices(
            $$"""{ "name": "unknown", "id": "{{PresenceService.StePhoneRouterId}}" }""",
            $$"""{ "name": "Android_SBFUAFP4", "id": "{{PresenceService.RuthPhoneRouterId}}" }""",
            $$"""{ "name": "Android_17YGONY5", "id": "{{PresenceService.EllePhoneRouterId}}" }""");

        PresenceSnapshot snapshot = await context.Service.GetSnapshotAsync();

        snapshot.Detectors[PresenceService.StePhoneHome].ShouldBeTrue();
        snapshot.Detectors[PresenceService.RuthPhoneHome].ShouldBeTrue();
        snapshot.Detectors[PresenceService.EllePhoneHome].ShouldBeTrue();
        snapshot.SomeoneHome.ShouldBeFalse("owner: phones are for individual zones, not the house rule");
        string[] phones = [PresenceService.StePhoneHome, PresenceService.RuthPhoneHome, PresenceService.EllePhoneHome];
        PresenceService.DefaultZoneDetectors.Values.SelectMany(ids => ids).ShouldNotContain(id => phones.Contains(id));

        context.Service.UseZoneOccupancy(new Dictionary<string, List<string>> { ["den"] = [PresenceService.StePhoneHome] });
        (await context.Service.IsRoomInUse("den")).ShouldBeTrue("chosen for the den");
        context.Service.GetPresenceDevices("den").ShouldBe([("phone.ste", RoomDeviceRoles.Phone)], "listed as the phone, not the router (owner, 2026-10-21)");

        RoomDeviceDto ste = context.Service.DescribeOwnDevice("phone.ste").ShouldNotBeNull();
        ste.Name.ShouldBe("Ste's phone");
        ste.State.ShouldBe("home");
        context.Service.DescribeOwnDevice("switch.dining_room_desk_plug").ShouldBeNull("a Home Assistant entity is looked up as usual");

        context.SetOnlineDevices($$"""{ "name": "unknown", "id": "{{PresenceService.StePhoneRouterId}}" }""");
        context.Service.DescribeOwnDevice("phone.ellie").ShouldNotBeNull().State.ShouldBe("away");
    }

    private sealed class PresenceTestContext
    {
        public required PresenceService Service { get; init; }
        public required IHaContext Ha { get; init; }
        public required Dictionary<string, double[]> History { get; init; }
        public required FakeTimeProvider Clock { get; init; }
        public required Subject<StateChange> Changes { get; init; }

        public void Advance(TimeSpan by) => Clock.SetSpecificDateTime(Clock.GetUtcNow() + by);

        /// <summary>A plug's switch changing state, announced to the app as Home Assistant would.</summary>
        public void SetPlug(string entityId, bool on) => Publish(entityId, on ? "on" : "off");

        /// <summary>A power sensor taking a new reading, announced to the app as Home Assistant would.</summary>
        public void SetPower(string entityId, double watts) => Publish(entityId, watts.ToString(System.Globalization.CultureInfo.InvariantCulture));

        public void SetPowerState(string entityId, string state) => Publish(entityId, state);

        private void Publish(string entityId, string state)
        {
            EntityState? previous = Ha.GetState(entityId);
            EntityState current = new() { EntityId = entityId, State = state };
            Ha.GetState(entityId).Returns(current);
            Changes.OnNext(new StateChange(new Entity(Ha, entityId), previous, current));
        }

        public void SetSwitch(string entityId, string state)
        {
            Ha.GetState(entityId).Returns(new EntityState { EntityId = entityId, State = state });
        }

        /// <summary>The router's online list, as the Linksys Velop integration publishes it, holding these devices.</summary>
        public void SetOnlineDevices(params string[] devicesJson)
        {
            Ha.GetState(PresenceService.RouterOnlineDevices).Returns(new EntityState
            {
                EntityId = PresenceService.RouterOnlineDevices,
                State = devicesJson.Length.ToString(),
                AttributesJson = JsonDocument.Parse($$"""{ "devices": [{{string.Join(",", devicesJson)}}] }""").RootElement
            });
        }

        public static PresenceTestContext Create()
        {
            IHaContext ha = Substitute.For<IHaContext>();
            ha.GetState(Arg.Any<string>()).Returns((EntityState?)null);
            ha.StateAllChanges().Returns(Observable.Empty<StateChange>());
            ha.Events.Returns(Observable.Empty<Event>());

            Subject<StateChange> changes = new();
            ha.StateAllChanges().Returns(changes);

            FakeTimeProvider clock = new();
            clock.SetSpecificDateTime(new DateTimeOffset(2026, 10, 20, 11, 0, 0, TimeSpan.Zero));

            Dictionary<string, double[]> history = [];
            ScriptedHistoryService historyService = new(history);
            PresenceService service = new(ha, new NamedEntities(ha), historyService, clock, Substitute.For<ILogger<PresenceService>>());

            return new PresenceTestContext { Service = service, Ha = ha, History = history, Clock = clock, Changes = changes };
        }
    }

    /// <summary>
    /// Returns the given power readings for an entity, whatever window is asked for. Only the detectors with a hold
    /// (Ste's desk, the den TV "on recently") read history; the "for 5 seconds" ones read live readings.
    /// </summary>
    private sealed class ScriptedHistoryService(Dictionary<string, double[]> readings)
        : HistoryService(new HomeAssistantConfiguration(), Substitute.For<ILogger<HistoryService>>())
    {
        public override Task<IReadOnlyList<NumericHistoryEntry>> GetEntityNumericHistory(string entityId, DateTime from, DateTime to)
        {
            IReadOnlyList<NumericHistoryEntry> entries = readings.TryGetValue(entityId, out double[]? values)
                ? [.. values.Select(v => new NumericHistoryEntry { State = v, LastChanged = to })]
                : [];
            return Task.FromResult(entries);
        }
    }
}

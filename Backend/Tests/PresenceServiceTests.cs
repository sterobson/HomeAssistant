using HomeAssistant.apps;
using HomeAssistant.Services;
using HomeAssistant.Shared.Climate;
using Microsoft.Extensions.Logging;
using NetDaemon.HassModel;
using NetDaemon.HassModel.Entities;
using NSubstitute;
using Shouldly;
using System.Reactive.Linq;

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

    private const string DiningDeskSwitch = "switch.dining_room_desk_plug";
    private const string DiningDeskPower = "sensor.dining_room_desk_plug_power";

    [TestMethod]
    [DataRow("on", new[] { 35.0 }, true, DisplayName = "on and drawing steadily")]
    [DataRow("on", new[] { 30.0, 31.0 }, true, DisplayName = "exactly the threshold counts")]
    [DataRow("on", new[] { 4.0, 45.0 }, false, DisplayName = "only just started drawing: not yet 5 s")]
    [DataRow("on", new[] { 45.0, 4.0 }, false, DisplayName = "just stopped: no hold")]
    [DataRow("on", new[] { 4.0 }, false, DisplayName = "on but idle")]
    [DataRow("off", new[] { 45.0 }, false, DisplayName = "switched off")]
    public async Task DiningDesk_IsInUseWhileOnAndDrawing30WattsForTheLast5Seconds(string plug, double[] watts, bool expected)
    {
        PresenceTestContext context = PresenceTestContext.Create();
        context.SetSwitch(DiningDeskSwitch, plug);
        context.History[DiningDeskPower] = watts;

        (await context.Service.IsRoomInUse("dining desk")).ShouldBe(expected);
    }

    [TestMethod]
    public async Task DiningDesk_AsksForExactlyTheLast5SecondsOfPower()
    {
        PresenceTestContext context = PresenceTestContext.Create();
        context.SetSwitch(DiningDeskSwitch, "on");
        context.History[DiningDeskPower] = [45];

        await context.Service.IsRoomInUse("dining desk");

        context.RequestedWindows[DiningDeskPower].ShouldBe(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    [DataRow("on", true)]
    [DataRow("off", false)]
    public async Task DiningRoom_IsInUseWhileRuthsDeskPlugIsSwitchedOn_WhateverItDraws(string plug, bool expected)
    {
        PresenceTestContext context = PresenceTestContext.Create();
        context.SetSwitch(DiningDeskSwitch, plug);
        context.History[DiningDeskPower] = [0];

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
        context.History[DiningDeskPower] = [3];

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
        context.History[DenTvPower] = [45];

        PresenceSnapshot snapshot = await context.Service.GetSnapshotAsync();

        snapshot.Zones["den"].ShouldBeFalse("the den was configured with no detectors");
        snapshot.SomeoneHome.ShouldBeTrue();
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
            PresenceService.DenTvOn, PresenceService.LivingRoomTvOn, PresenceService.LivingRoomLightsOn
        ]);
        context.Service.Detectors.Single(d => d.Id == PresenceService.DiningDeskSwitchedOn).MeansSomeoneHome.ShouldBeFalse();
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
        context.History[LivingTvPower] = [0];

        (await context.Service.IsRoomInUse("living room")).ShouldBeTrue();
    }

    [TestMethod]
    [DataRow(new[] { 45.0 }, true, DisplayName = "steady 45 W for the whole window")]
    [DataRow(new[] { 30.0, 31.0 }, true, DisplayName = "exactly the threshold counts")]
    [DataRow(new[] { 4.0, 45.0 }, false, DisplayName = "only just switched on: not yet 5 s")]
    [DataRow(new[] { 45.0, 4.0 }, false, DisplayName = "just turned off: no hold")]
    [DataRow(new[] { 4.0 }, false, DisplayName = "standby")]
    public async Task LivingRoom_TvMustDraw30WattsForTheWholeLast5Seconds(double[] watts, bool expected)
    {
        PresenceTestContext context = PresenceTestContext.Create();
        context.SetSwitch(StickLightsSwitch, "off");
        context.History[LivingTvPower] = watts;

        (await context.Service.IsRoomInUse("living room")).ShouldBe(expected);
    }

    [TestMethod]
    public async Task LivingRoom_AsksForExactlyTheLast5SecondsOfTvPower()
    {
        PresenceTestContext context = PresenceTestContext.Create();
        context.SetSwitch(StickLightsSwitch, "off");
        context.History[LivingTvPower] = [45];

        await context.Service.IsRoomInUse("living room");

        context.RequestedWindows[LivingTvPower].ShouldBe(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task LivingRoom_GoesByTvPowerAlone_NotTheTvPlugSwitch()
    {
        PresenceTestContext context = PresenceTestContext.Create();
        context.SetSwitch(StickLightsSwitch, "off");
        context.SetSwitch(LivingTvSwitch, "off");
        context.History[LivingTvPower] = [45];

        (await context.Service.IsRoomInUse("living room")).ShouldBeTrue("the switch is not read; the 45 W draw is");

        context.History[LivingTvPower] = [4];
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

    private sealed class PresenceTestContext
    {
        public required PresenceService Service { get; init; }
        public required IHaContext Ha { get; init; }
        public required Dictionary<string, double[]> History { get; init; }
        public required Dictionary<string, TimeSpan> RequestedWindows { get; init; }

        public void SetSwitch(string entityId, string state)
        {
            Ha.GetState(entityId).Returns(new EntityState { EntityId = entityId, State = state });
        }

        public static PresenceTestContext Create()
        {
            IHaContext ha = Substitute.For<IHaContext>();
            ha.GetState(Arg.Any<string>()).Returns((EntityState?)null);
            ha.StateAllChanges().Returns(Observable.Empty<StateChange>());
            ha.Events.Returns(Observable.Empty<Event>());

            Dictionary<string, double[]> history = [];
            Dictionary<string, TimeSpan> windows = [];
            ScriptedHistoryService historyService = new(history, windows);
            PresenceService service = new(ha, new NamedEntities(ha), historyService, Substitute.For<ILogger<PresenceService>>());

            return new PresenceTestContext { Service = service, Ha = ha, History = history, RequestedWindows = windows };
        }
    }

    /// <summary>
    /// Returns the given power readings for an entity, whatever window is asked for, and records how
    /// long a window was asked for.
    /// </summary>
    private sealed class ScriptedHistoryService(Dictionary<string, double[]> readings, Dictionary<string, TimeSpan> windows)
        : HistoryService(new HomeAssistantConfiguration(), Substitute.For<ILogger<HistoryService>>())
    {
        public override Task<IReadOnlyList<NumericHistoryEntry>> GetEntityNumericHistory(string entityId, DateTime from, DateTime to)
        {
            windows[entityId] = to - from;
            IReadOnlyList<NumericHistoryEntry> entries = readings.TryGetValue(entityId, out double[]? values)
                ? [.. values.Select(v => new NumericHistoryEntry { State = v, LastChanged = to })]
                : [];
            return Task.FromResult(entries);
        }
    }
}

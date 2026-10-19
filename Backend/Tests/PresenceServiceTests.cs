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
    public async Task Desks_StillNeed40Watts()
    {
        // The TV's lower threshold must not leak into the desks.
        PresenceTestContext context = PresenceTestContext.Create();
        context.SetSwitch(GamesDeskSwitch, "on");
        context.History[GamesDeskPower] = [35];

        (await context.Service.IsRoomInUse("games desk")).ShouldBeFalse();

        context.History[GamesDeskPower] = [40];
        (await context.Service.IsRoomInUse("games desk")).ShouldBeTrue();
    }

    [TestMethod]
    public void Den_CanNowDetectPresence_DiningRoomStillCannot()
    {
        PresenceTestContext context = PresenceTestContext.Create();

        context.Service.CanDetectIfRoomInUse("den").ShouldBeTrue();
        context.Service.CanDetectIfRoomInUse(" Den ").ShouldBeTrue("room names are matched trimmed and case-insensitively");
        context.Service.CanDetectIfRoomInUse("dining room").ShouldBeFalse("the dining radiator zone is deliberately presence-blind");
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

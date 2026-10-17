using HomeAssistant.apps;
using HomeAssistant.Devices.Batteries;
using HomeAssistant.Devices.Meters;
using HomeAssistant.Services;
using HomeAssistant.Services.Climate;
using HomeAssistant.Shared;
using HomeAssistant.Shared.Climate;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using System.Reactive.Concurrency;

namespace HomeAssistant.Tests;

/// <summary>
/// The dining room and the den each hold two independently scheduled zones that share one
/// thermometer: a desk (heater plug) and the room itself (radiator valve). Room names are the
/// join key to that hardware, and a name that matches no switch arm falls through to an empty
/// device list with no error at all - the room simply stops being heated. These tests pin each
/// zone to the hardware it is supposed to own, so a rename cannot unbind one silently.
/// </summary>
[TestClass]
public sealed class RoomZoneBindingTests
{
    [TestMethod]
    public async Task DiningDesk_DrivesTheHeaterPlugAndNotTheRadiator()
    {
        ZoneTestContext context = ZoneTestContext.Create("dining desk");

        await context.Service.EvaluateAllSchedules("unit test");

        context.DiningRoomHeaterPlug.TurnOnCount.ShouldBeGreaterThan(0, "the desk zone owns the dining heater plug");
        context.DiningRoomRadiator.TargetTemperature.ShouldBe(ZoneTestContext.IdleRadiatorTarget, "the radiator belongs to the dining room zone, not the desk, so it should not have moved");
    }

    [TestMethod]
    public async Task DiningRoom_DrivesTheRadiatorAndNotTheHeaterPlug()
    {
        ZoneTestContext context = ZoneTestContext.Create("dining room");

        await context.Service.EvaluateAllSchedules("unit test");

        context.DiningRoomRadiator.TargetTemperature.ShouldBe(ZoneTestContext.HeatingRadiatorTarget, "the dining room zone owns the radiator valve and should have driven it up");
        context.DiningRoomHeaterPlug.TurnOnCount.ShouldBe(0, "the heater plug moved to the desk zone in the split");
    }

    [TestMethod]
    public async Task GamesDesk_StillDrivesTheGamesHeaterPlug()
    {
        // "games room" was renamed to "games desk" when the den was split out. If that rename
        // missed a switch arm the plug would simply stop being driven, with nothing logged.
        ZoneTestContext context = ZoneTestContext.Create("games desk");

        await context.Service.EvaluateAllSchedules("unit test");

        context.GamesRoomHeaterPlug.TurnOnCount.ShouldBeGreaterThan(0);
    }

    [TestMethod]
    public async Task SplitZonesShareOneThermometer()
    {
        ZoneTestContext context = ZoneTestContext.Create("dining desk", "dining room");

        await context.Service.EvaluateAllSchedules("unit test");

        IReadOnlyDictionary<int, RoomState> states = context.CapturedStates();
        states[1].CurrentTemperature.ShouldBe(ZoneTestContext.RoomTemperature);
        states[2].CurrentTemperature.ShouldBe(ZoneTestContext.RoomTemperature);
    }

    [TestMethod]
    public async Task BoundZonesReportTheyCanSetTemperature()
    {
        ZoneTestContext context = ZoneTestContext.Create("dining desk", "dining room", "games desk");

        await context.Service.EvaluateAllSchedules("unit test");

        IReadOnlyDictionary<int, RoomState> states = context.CapturedStates();
        foreach (KeyValuePair<int, RoomState> state in states)
        {
            state.Value.Capabilities.HasFlag(RoomCapabilities.CanSetTemperature)
                .ShouldBeTrue($"room {state.Key} should be bound to hardware");
        }
    }

    [TestMethod]
    public async Task DenReportsNoControlsUntilItsValvesAreFitted()
    {
        // The den has no TRVs yet. It should read as "no controls" rather than disappear, and
        // crucially this must not look any different from a room whose name stopped matching -
        // which is why every other zone above is pinned explicitly.
        ZoneTestContext context = ZoneTestContext.Create("den");

        await context.Service.EvaluateAllSchedules("unit test");

        IReadOnlyDictionary<int, RoomState> states = context.CapturedStates();
        states[1].Capabilities.HasFlag(RoomCapabilities.CanSetTemperature).ShouldBeFalse();
    }

    [TestMethod]
    public async Task AnUnknownRoomNameBindsToNothing()
    {
        // Guards the failure mode itself: this is what a mistyped or un-migrated name looks
        // like, and it is indistinguishable from the den above except by the name.
        ZoneTestContext context = ZoneTestContext.Create("dining rooom");

        await context.Service.EvaluateAllSchedules("unit test");

        IReadOnlyDictionary<int, RoomState> states = context.CapturedStates();
        states[1].Capabilities.HasFlag(RoomCapabilities.CanSetTemperature).ShouldBeFalse();
        context.DiningRoomHeaterPlug.TurnOnCount.ShouldBe(0);
        context.DiningRoomRadiator.TargetTemperature.ShouldBe(ZoneTestContext.IdleRadiatorTarget);
    }

    [TestMethod]
    [DataRow("Kitchen", 1)]
    [DataRow("Living room", 2)]
    [DataRow("Dining room", 2)]
    [DataRow("dining desk", 2)]
    [DataRow("Games desk", 1)]
    [DataRow("den", 1)]
    [DataRow("Bedroom 1", 2)]
    [DataRow("Bedroom 2", 2)]
    [DataRow("Bedroom 3", 1)]
    [DataRow("Upstairs bathroom", 1)]
    [DataRow("Downstairs bathroom", 1)]
    public void EveryKnownRoomHasABoilerScore(string roomName, int expectedScore)
    {
        // A name that matches no arm scores 0 with no error, so a typo in the switch quietly
        // takes that room out of the boiler's decision. "upstairsbathroom" (no space) did exactly
        // that: the real room is "Upstairs bathroom" and it scored 0 instead of 1.
        RoomSchedule room = new RoomSchedule { Name = roomName, ScheduleTracks = [] };

        HeatingControlService.GetRoomBoilerPriority(room).ShouldBe(expectedScore);
    }

    [TestMethod]
    public void AnUnknownRoomScoresZeroForTheBoiler()
    {
        // Guards the failure mode itself, so the test above cannot pass by everything scoring > 0.
        RoomSchedule room = new RoomSchedule { Name = "upstairsbathroom", ScheduleTracks = [] };

        HeatingControlService.GetRoomBoilerPriority(room).ShouldBe(0);
    }

    private sealed class ZoneTestContext
    {
        public const double RoomTemperature = 15;

        /// <summary>
        /// A real valve always has a target set, and the control logic only moves one that is
        /// already below the current temperature. Starting it at null would make every "did the
        /// valve move?" assertion pass for the wrong reason.
        /// </summary>
        public const double IdleRadiatorTarget = RoomTemperature - 5;

        /// <summary>What the valve is driven to when its zone calls for heat.</summary>
        public const double HeatingRadiatorTarget = RoomTemperature + 5;

        private const double TargetTemperature = 21;

        public required HeatingControlService Service { get; init; }
        public required FakeNamedEntities Entities { get; init; }
        public required IRoomStatePersistenceService StatePersistence { get; init; }

        public FakeCustomSwitchEntity DiningRoomHeaterPlug => (FakeCustomSwitchEntity)Entities.DiningRoomHeaterSmartPlugOnOff;
        public FakeCustomClimateControlEntity DiningRoomRadiator => (FakeCustomClimateControlEntity)Entities.DiningRoomRadiatorThermostat;
        public FakeCustomSwitchEntity GamesRoomHeaterPlug => (FakeCustomSwitchEntity)Entities.GamesRoomHeaterSmartPlugOnOff;

        /// <summary>
        /// The room states the service last published. Capabilities are recomputed on every
        /// publish, so this is where a zone that has lost its hardware shows up.
        /// </summary>
        public IReadOnlyDictionary<int, RoomState> CapturedStates()
        {
            List<Dictionary<int, RoomState>> calls = [.. StatePersistence.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name == nameof(IRoomStatePersistenceService.SetStatesAsync))
                .Select(call => (Dictionary<int, RoomState>)call.GetArguments()[0]!)];

            calls.ShouldNotBeEmpty("the service should have published room states");
            return calls[^1];
        }

        /// <summary>
        /// Builds a house of the given zones, each wanting heat right now, at a time of day when
        /// the time-restricted heater plugs are allowed to run.
        /// </summary>
        public static ZoneTestContext Create(params string[] roomNames)
        {
            ServiceCollection services = new();

            ISchedulePersistenceService schedulePersistence = Substitute.For<ISchedulePersistenceService>();
            IRoomStatePersistenceService statePersistence = Substitute.For<IRoomStatePersistenceService>();

            services.AddSingleton<HistoryService, MockHistoryService>();
            services.AddSingleton(Substitute.For<IScheduler>());
            services.AddSingleton(Substitute.For<IHomeBattery>());
            services.AddSingleton(Substitute.For<ISolarPanels>());
            services.AddSingleton(Substitute.For<IElectricityMeter>());
            services.AddSingleton<IPresenceService, MockPresenceService>();
            services.AddSingleton(Substitute.For<ILogger<HeatingControlService>>());
            services.AddSingleton(Substitute.For<ILogger<HistoryService>>());
            services.AddSingleton(Substitute.For<NetDaemon.HassModel.IHaContext>());
            services.AddSingleton<FakeNamedEntities>();
            services.AddSingleton<INamedEntities>(provider => provider.GetRequiredService<FakeNamedEntities>());
            services.AddSingleton<HomeAssistantConfiguration>();
            services.AddSingleton<FakeTimeProvider>();
            services.AddSingleton<TimeProvider>(provider => provider.GetRequiredService<FakeTimeProvider>());
            services.AddSingleton(Substitute.For<ILogger<ISchedulePersistenceService>>());
            services.AddSingleton(schedulePersistence);
            services.AddSingleton(statePersistence);
            services.AddSingleton(Substitute.For<IBoilerVerificationService>());
            services.AddSingleton<FakeDeviceSettingsPersistenceService>();
            services.AddSingleton<IDeviceSettingsPersistenceService>(provider => provider.GetRequiredService<FakeDeviceSettingsPersistenceService>());

            ServiceProvider provider = services.BuildServiceProvider();

            // Midday, so the plugs' 07:00-21:00 window is open.
            provider.GetRequiredService<FakeTimeProvider>()
                .SetSpecificDateTime(new DateTimeOffset(new DateTime(2026, 10, 19, 12, 0, 0)));

            FakeNamedEntities entities = provider.GetRequiredService<FakeNamedEntities>();

            // Every zone reads below target, so each one calls for heat.
            ((FakeCustomNumericSensorEntity)entities.DiningRoomClimateTemperature).State = RoomTemperature;
            ((FakeCustomNumericSensorEntity)entities.GamesRoomDeskTemperature).State = RoomTemperature;
            FakeCustomClimateControlEntity diningRadiator = (FakeCustomClimateControlEntity)entities.DiningRoomRadiatorThermostat;
            diningRadiator.CurrentTemperature = RoomTemperature;
            diningRadiator.SetTargetTemperature(IdleRadiatorTarget);

            // House presence is inferred from desk plug power draw.
            ((FakeCustomSwitchEntity)entities.DiningRoomDeskPlugOnOff).TurnOn();
            ((FakeCustomNumericSensorEntity)entities.DiningRoomDeskPlugPower).State = 50;

            List<RoomSchedule> rooms = [];
            for (int i = 0; i < roomNames.Length; i++)
            {
                rooms.Add(new RoomSchedule
                {
                    Id = i + 1,
                    Name = roomNames[i],
                    ScheduleTracks =
                    [
                        new HeatingScheduleTrack
                        {
                            TargetTime = new TimeOnly(0, 0),
                            Temperature = TargetTemperature
                        }
                    ]
                });
            }

            schedulePersistence.GetSchedulesAsync().Returns(Task.FromResult(new RoomSchedules { Rooms = rooms }));

            return new ZoneTestContext
            {
                Service = ActivatorUtilities.CreateInstance<HeatingControlService>(provider),
                Entities = entities,
                StatePersistence = statePersistence
            };
        }
    }
}

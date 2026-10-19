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
/// The dining room and the den each hold two independently scheduled zones: a desk (heater plug)
/// and the room itself (radiator valve). The dining zones share one thermometer; the den and Ste's
/// desk each have their own. Room names are the
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
    public async Task GamesDesk_HeaterRunsAtNightWhenSomeoneIsAtADesk()
    {
        // The desk heaters used to carry a daytime window as well as the desk-power check. They
        // now follow desk power, presence and the schedule alone.
        ZoneTestContext context = ZoneTestContext.CreateAt(new TimeSpan(23, 30, 0), "games desk");

        await context.Service.EvaluateAllSchedules("unit test");

        context.GamesRoomHeaterPlug.TurnOnCount.ShouldBeGreaterThan(0);
    }

    [TestMethod]
    public async Task DiningDesk_HeaterRunsAtNightWhenSomeoneIsAtADesk()
    {
        ZoneTestContext context = ZoneTestContext.CreateAt(new TimeSpan(23, 30, 0), "dining desk");

        await context.Service.EvaluateAllSchedules("unit test");

        context.DiningRoomHeaterPlug.TurnOnCount.ShouldBeGreaterThan(0);
    }

    [TestMethod]
    public async Task GamesDesk_HeaterStaysOffWithNobodyAtEitherDesk()
    {
        ZoneTestContext context = ZoneTestContext.CreateAt(new TimeSpan(12, 0, 0), "games desk");
        context.Presence.SetRoomValue("games desk", false);
        context.Presence.SetRoomValue("dining desk", false);

        await context.Service.EvaluateAllSchedules("unit test");

        context.GamesRoomHeaterPlug.TurnOnCount.ShouldBe(0, "nobody is at Ste's desk, which its default rule requires");
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
    public async Task Den_DrivesBothRadiatorsAndNoHeaterPlug()
    {
        ZoneTestContext context = ZoneTestContext.Create("den");

        await context.Service.EvaluateAllSchedules("unit test");

        context.DenRadiator1.TargetTemperature.ShouldBe(ZoneTestContext.HeatingRadiatorTarget);
        context.DenRadiator2.TargetTemperature.ShouldBe(ZoneTestContext.HeatingRadiatorTarget);
        context.GamesRoomHeaterPlug.TurnOnCount.ShouldBe(0, "the heater plug belongs to the games desk zone");
        context.CapturedStates()[1].Capabilities.HasFlag(RoomCapabilities.CanSetTemperature).ShouldBeTrue();
    }

    [TestMethod]
    public async Task DenAndGamesDesk_EachReadTheirOwnThermometer()
    {
        // Until 2026-10-19 the den borrowed Ste's desk thermometer. It now has its own room
        // thermometer, so the two zones must report different readings when the sensors differ.
        ZoneTestContext context = ZoneTestContext.Create("den", "games desk");
        ((FakeCustomNumericSensorEntity)context.Entities.DenClimateTemperature).State = 16;
        ((FakeCustomNumericSensorEntity)context.Entities.GamesRoomDeskTemperature).State = 17;

        await context.Service.EvaluateAllSchedules("unit test");

        IReadOnlyDictionary<int, RoomState> states = context.CapturedStates();
        states[1].CurrentTemperature.ShouldBe(16, "the den reads the den room thermometer");
        states[2].CurrentTemperature.ShouldBe(17, "the games desk reads the desk thermometer");
    }

    [TestMethod]
    public async Task Den_WithNoThermometerReadingIsNotControlled()
    {
        // The desk thermometer reading must not stand in for a missing den reading.
        ZoneTestContext context = ZoneTestContext.Create("den");
        ((FakeCustomNumericSensorEntity)context.Entities.DenClimateTemperature).State = null;

        await context.Service.EvaluateAllSchedules("unit test");

        context.DenRadiator1.TargetTemperature.ShouldBe(ZoneTestContext.IdleRadiatorTarget);
        context.DenRadiator2.TargetTemperature.ShouldBe(ZoneTestContext.IdleRadiatorTarget);
    }

    [TestMethod]
    public async Task Den_ListsItsThermometerAndBothRadiators()
    {
        ZoneTestContext context = ZoneTestContext.Create("den");

        await context.Service.EvaluateAllSchedules("unit test");

        List<RoomDeviceDto> devices = context.CapturedStates()[1].Devices;
        devices.Select(d => (d.EntityId, d.Role)).ShouldBe(
        [
            (nameof(FakeNamedEntities.DenClimateTemperature), RoomDeviceRoles.Thermometer),
            (nameof(FakeNamedEntities.DenRadiator1Thermostat), RoomDeviceRoles.RadiatorValve),
            (nameof(FakeNamedEntities.DenRadiator2Thermostat), RoomDeviceRoles.RadiatorValve)
        ]);
        devices[1].TargetTemperature.ShouldBe(ZoneTestContext.HeatingRadiatorTarget, "a valve carries its live target");
    }

    [TestMethod]
    public async Task GamesDesk_ListsItsThermometerHeaterPlugAndPresencePlug()
    {
        ZoneTestContext context = ZoneTestContext.Create("games desk");
        context.Presence.SetRoomValue("games desk", true);

        await context.Service.EvaluateAllSchedules("unit test");

        context.CapturedStates()[1].Devices.Select(d => (d.EntityId, d.Role)).ShouldBe(
        [
            (nameof(FakeNamedEntities.GamesRoomDeskTemperature), RoomDeviceRoles.Thermometer),
            (nameof(FakeNamedEntities.GamesRoomHeaterSmartPlugOnOff), RoomDeviceRoles.HeaterPlug),
            ("games desk desk plug", RoomDeviceRoles.DeskPlug)
        ]);
    }

    [TestMethod]
    public async Task UpstairsBathroom_IsAThermometerWithNoControls()
    {
        // It has no heater or valve. It used to be "driven" by a dummy switch, which made the UI offer a
        // boost, schedules and a share link that did nothing (owner, 2026-10-19).
        ZoneTestContext context = ZoneTestContext.Create("upstairs bathroom");

        await context.Service.EvaluateAllSchedules("unit test");

        context.CapturedStates()[1].Devices.Select(d => d.Role).ShouldBe([RoomDeviceRoles.Thermometer]);
        context.CapturedStates()[1].Capabilities.ShouldBe(RoomCapabilities.None, "the UI shows controls only for a zone that can set a temperature");
    }

    [TestMethod]
    public async Task AnUnknownRoomNameListsNoDevices()
    {
        ZoneTestContext context = ZoneTestContext.Create("dining rooom");

        await context.Service.EvaluateAllSchedules("unit test");

        context.CapturedStates()[1].Devices.ShouldBeEmpty();
    }

    [TestMethod]
    public async Task GamesDesk_DoesNotDriveTheDenRadiators()
    {
        ZoneTestContext context = ZoneTestContext.Create("games desk");

        await context.Service.EvaluateAllSchedules("unit test");

        context.DenRadiator1.TargetTemperature.ShouldBe(ZoneTestContext.IdleRadiatorTarget);
        context.DenRadiator2.TargetTemperature.ShouldBe(ZoneTestContext.IdleRadiatorTarget);
    }

    [TestMethod]
    public async Task AnUnknownRoomNameBindsToNothing()
    {
        // Guards the failure mode itself: this is what a mistyped or un-migrated name looks
        // like, and it is indistinguishable from a working zone except by the name.
        ZoneTestContext context = ZoneTestContext.Create("dining rooom");

        await context.Service.EvaluateAllSchedules("unit test");

        IReadOnlyDictionary<int, RoomState> states = context.CapturedStates();
        states[1].Capabilities.HasFlag(RoomCapabilities.CanSetTemperature).ShouldBeFalse();
        context.DiningRoomHeaterPlug.TurnOnCount.ShouldBe(0);
        context.DiningRoomRadiator.TargetTemperature.ShouldBe(ZoneTestContext.IdleRadiatorTarget);
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
        public required MockPresenceService Presence { get; init; }

        public FakeCustomSwitchEntity DiningRoomHeaterPlug => (FakeCustomSwitchEntity)Entities.DiningRoomHeaterSmartPlugOnOff;
        public FakeCustomClimateControlEntity DiningRoomRadiator => (FakeCustomClimateControlEntity)Entities.DiningRoomRadiatorThermostat;
        public FakeCustomClimateControlEntity DenRadiator1 => (FakeCustomClimateControlEntity)Entities.DenRadiator1Thermostat;
        public FakeCustomClimateControlEntity DenRadiator2 => (FakeCustomClimateControlEntity)Entities.DenRadiator2Thermostat;
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
        /// Builds a house of the given zones, each wanting heat right now. The clock defaults to
        /// midday, when every time-restricted heater plug is allowed to run.
        /// </summary>
        public static ZoneTestContext Create(params string[] roomNames)
        {
            return CreateAt(new TimeSpan(12, 0, 0), roomNames);
        }

        public static ZoneTestContext CreateAt(TimeSpan timeOfDay, params string[] roomNames)
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
            services.AddSingleton(Substitute.For<IBoilerStatePersistence>());
            services.AddSingleton<FakeDeviceSettingsPersistenceService>();
            services.AddSingleton<IDeviceSettingsPersistenceService>(provider => provider.GetRequiredService<FakeDeviceSettingsPersistenceService>());

            ServiceProvider provider = services.BuildServiceProvider();

            provider.GetRequiredService<FakeTimeProvider>()
                .SetSpecificDateTime(new DateTimeOffset(new DateTime(2026, 10, 19).Add(timeOfDay)));

            FakeNamedEntities entities = provider.GetRequiredService<FakeNamedEntities>();
            MockPresenceService presence = (MockPresenceService)provider.GetRequiredService<IPresenceService>();

            // Every zone reads below target, so each one calls for heat.
            ((FakeCustomNumericSensorEntity)entities.DiningRoomClimateTemperature).State = RoomTemperature;
            ((FakeCustomNumericSensorEntity)entities.GamesRoomDeskTemperature).State = RoomTemperature;
            ((FakeCustomNumericSensorEntity)entities.DenClimateTemperature).State = RoomTemperature;
            FakeCustomClimateControlEntity diningRadiator = (FakeCustomClimateControlEntity)entities.DiningRoomRadiatorThermostat;
            diningRadiator.CurrentTemperature = RoomTemperature;
            diningRadiator.SetTargetTemperature(IdleRadiatorTarget);

            foreach (FakeCustomClimateControlEntity denRadiator in new[] { (FakeCustomClimateControlEntity)entities.DenRadiator1Thermostat, (FakeCustomClimateControlEntity)entities.DenRadiator2Thermostat })
            {
                denRadiator.CurrentTemperature = RoomTemperature;
                denRadiator.SetTargetTemperature(IdleRadiatorTarget);
            }

            // Someone at each desk, so the desk heaters' default device rules (presence at that desk)
            // let them run. Tests about nobody being present switch these off.
            presence.SetRoomValue("games desk", true);
            presence.SetRoomValue("dining desk", true);

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
                StatePersistence = statePersistence,
                Presence = presence
            };
        }
    }
}

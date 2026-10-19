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
/// Per-device rules working through HeatingControlService: a device runs only while its zone wants heat
/// AND its rules pass, is switched off when they stop passing, and the built-in defaults (set by the owner
/// on 2026-10-19) apply until rules are saved. Every zone here is at 15 °C with a 21 °C target, so every
/// zone wants heat; whether a device actually runs is down to its rules.
/// </summary>
[TestClass]
public sealed class DeviceRulesHeatingTests
{
    [TestMethod]
    public async Task Kitchen_RunsWithSomeoneHomeInsideItsWindow()
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(12, 0, 0), "kitchen");
        context.Presence.SetRoomValue("games desk", true);

        await context.Service.EvaluateAllSchedules("unit test");

        context.KitchenHeater.IsOn().ShouldBeTrue();
    }

    [TestMethod]
    public async Task Kitchen_StaysOffWithNobodyHome()
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(12, 0, 0), "kitchen");
        context.Presence.SetRoomValue("games desk", false);

        await context.Service.EvaluateAllSchedules("unit test");

        context.KitchenHeater.TurnOnCount.ShouldBe(0);
    }

    [TestMethod]
    [DataRow(5, 29, false)]
    [DataRow(5, 30, true)]
    [DataRow(21, 59, true)]
    [DataRow(22, 0, false)]
    public async Task Kitchen_OnlyBetween0530And2200(int hour, int minute, bool expectedOn)
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(hour, minute, 0), "kitchen");
        context.Presence.SetRoomValue("games desk", true);

        await context.Service.EvaluateAllSchedules("unit test");

        context.KitchenHeater.IsOn().ShouldBe(expectedOn);
    }

    [TestMethod]
    public async Task House_IsAnyPresenceZone_NotJustTheDesks()
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(12, 0, 0), "kitchen");
        context.Presence.SetRoomValue("games desk", false);
        context.Presence.SetRoomValue("den", true);

        await context.Service.EvaluateAllSchedules("unit test");

        context.KitchenHeater.IsOn().ShouldBeTrue("someone watching the den TV counts as someone home");
    }

    [TestMethod]
    public async Task Bedroom1_HeaterWaitsForItsWindow_ButTheRadiatorDoesNot()
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(12, 0, 0), "bedroom 1");
        context.Presence.SetRoomValue("games desk", true);

        await context.Service.EvaluateAllSchedules("unit test");

        context.Bedroom1Heater.TurnOnCount.ShouldBe(0, "12:00 is outside 08:00-08:30 and 21:00-21:15");
        context.Bedroom1Radiator.TargetTemperature.ShouldBe(RulesTestContext.HeatingValveTarget, "the radiator has no rules, so the zone still heats");
    }

    [TestMethod]
    [DataRow(8, 10)]
    [DataRow(21, 5)]
    public async Task Bedroom1_HeaterRunsInItsWindowsWithSomeoneHome(int hour, int minute)
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(hour, minute, 0), "bedroom 1");
        context.Presence.SetRoomValue("dining desk", true);

        await context.Service.EvaluateAllSchedules("unit test");

        context.Bedroom1Heater.IsOn().ShouldBeTrue();
    }

    [TestMethod]
    public async Task StesDeskHeater_NeedsSomeoneAtThatDesk_NotJustSomeoneHome()
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(23, 30, 0), "games desk");
        context.Presence.SetRoomValue("games desk", false);
        context.Presence.SetRoomValue("dining desk", true);

        await context.Service.EvaluateAllSchedules("unit test");

        context.GamesHeater.TurnOnCount.ShouldBe(0, "Ruth at her desk does not warm Ste's");

        context.Presence.SetRoomValue("games desk", true);
        await context.Service.EvaluateAllSchedules("Ste sits down");

        context.GamesHeater.IsOn().ShouldBeTrue("any time of day once Ste is at the desk");
    }

    [TestMethod]
    public async Task RuthsDeskHeater_NeedsSomeoneAtThatDesk()
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(3, 0, 0), "dining desk");
        context.Presence.SetRoomValue("dining desk", true);

        await context.Service.EvaluateAllSchedules("unit test");

        context.DiningHeater.IsOn().ShouldBeTrue();
    }

    [TestMethod]
    public async Task ARunningHeaterIsSwitchedOff_WhenItsRulesStopPassing()
    {
        // The owner's choice: a heater must not keep running after everyone has gone, even though the
        // room is still below its target.
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(12, 0, 0), "kitchen");
        context.Presence.SetRoomValue("games desk", true);
        await context.Service.EvaluateAllSchedules("someone home");
        context.KitchenHeater.IsOn().ShouldBeTrue();

        context.Presence.SetRoomValue("games desk", false);
        await context.Service.EvaluateAllSchedules("everyone left");

        context.KitchenHeater.IsOff().ShouldBeTrue();
        context.CapturedStates()[1].HeatingActive.ShouldBeFalse("with its only device held off the zone is not heating");

        context.Presence.SetRoomValue("games desk", true);
        await context.Service.EvaluateAllSchedules("back home");

        context.KitchenHeater.IsOn().ShouldBeTrue("and back on once the rules pass while the room is still cold");
    }

    [TestMethod]
    public async Task ARadiatorValveCanHaveRulesToo()
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(12, 0, 0), "den");
        context.SaveRules(new()
        {
            [context.Entities.DenRadiator1Thermostat.EntityId] = new DeviceRulesDto { Presence = new PresenceRuleDto { Sources = ["den"] } }
        });
        context.Presence.SetRoomValue("den", false);

        await context.Service.EvaluateAllSchedules("unit test");

        context.DenRadiator1.TargetTemperature.ShouldBe(RulesTestContext.IdleValveTarget, "nobody in the den, so this valve is held off");
        context.DenRadiator2.TargetTemperature.ShouldBe(RulesTestContext.HeatingValveTarget, "the other valve has no rules");
    }

    [TestMethod]
    public async Task PresenceRulesCanRequireAll()
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(12, 0, 0), "kitchen");
        context.SaveRules(new()
        {
            [context.Entities.KitchenHeaterSmartPlugOnOff.EntityId] = new DeviceRulesDto
            {
                Presence = new PresenceRuleDto { Match = PresenceMatch.All, Sources = ["games desk", "dining desk"] }
            }
        });
        context.Presence.SetRoomValue("games desk", true);
        context.Presence.SetRoomValue("dining desk", false);

        await context.Service.EvaluateAllSchedules("one of two");
        context.KitchenHeater.TurnOnCount.ShouldBe(0);

        context.Presence.SetRoomValue("dining desk", true);
        await context.Service.EvaluateAllSchedules("both");
        context.KitchenHeater.IsOn().ShouldBeTrue();
    }

    [TestMethod]
    public async Task SavedRulesReplaceTheDefaults_AndAMissingEntryMeansNoRules()
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(23, 30, 0), "kitchen");
        context.SaveRules([]);
        context.Presence.SetRoomValue("games desk", false);

        await context.Service.EvaluateAllSchedules("unit test");

        context.KitchenHeater.IsOn().ShouldBeTrue("once rules are saved, a device with no entry has no restrictions");
    }

    [TestMethod]
    public async Task UnreadableSettings_FallBackToTheDefaults()
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(12, 0, 0), "kitchen");
        context.Settings.ThrowOnGet = true;
        context.Presence.SetRoomValue("games desk", false);

        await context.Service.EvaluateAllSchedules("unit test");

        context.KitchenHeater.TurnOnCount.ShouldBe(0, "the defaults still need someone home");
    }

    [TestMethod]
    public async Task PublishedDevicesCarryTheirEffectiveRules()
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(12, 0, 0), "kitchen", "den");

        await context.Service.EvaluateAllSchedules("unit test");

        RoomDeviceDto heater = context.CapturedStates()[1].Devices.Single(d => d.Role == RoomDeviceRoles.HeaterPlug);
        heater.RulesAreDefault.ShouldBeTrue();
        heater.Rules.ShouldNotBeNull();
        heater.Rules.TimeWindows.Single().Start.ShouldBe("05:30");
        heater.Rules.Presence!.Sources.ShouldBe([PresenceSources.House]);

        context.CapturedStates()[2].Devices.Where(d => d.Role == RoomDeviceRoles.RadiatorValve)
            .ShouldAllBe(valve => valve.Rules == null, "radiator valves have no default rules");

        context.SaveRules([]);
        await context.Service.EvaluateAllSchedules("rules saved");
        context.CapturedStates()[1].Devices.Single(d => d.Role == RoomDeviceRoles.HeaterPlug).RulesAreDefault.ShouldBeFalse();
    }

    [TestMethod]
    [DataRow(20.0, DisplayName = "below the schedule's 21 °C: the boost lowers the target")]
    [DataRow(23.0, DisplayName = "above the schedule: the boost raises the target")]
    public async Task ABoostSetsTheExactTemperature_UpOrDown(double boostTemperature)
    {
        // Every boost (owner's, group, or a guest's QR override) replaces the schedule until it ends
        // (owner, 2026-10-19): Downstairs boosted to 20 °C takes Ste's 21 °C desk down to 20 °C.
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(12, 0, 0), "games desk");
        context.Presence.SetRoomValue("games desk", true);
        context.Rooms[0].Boost = new Boost
        {
            StartTime = new DateTimeOffset(2026, 10, 19, 11, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 10, 19, 14, 0, 0, TimeSpan.Zero),
            Temperature = boostTemperature
        };

        await context.Service.EvaluateAllSchedules("unit test");

        context.CapturedStates()[1].TargetTemperature.ShouldBe(boostTemperature);
    }

    [TestMethod]
    [DataRow(true, DisplayName = "someone home: the boost runs the heater plug")]
    [DataRow(false, DisplayName = "nobody home: the plug's rules still hold it off")]
    public async Task ABoostRunsAHeaterPlug_OnlyIfItsRulesAllow(bool someoneHome)
    {
        // The kitchen is scheduled below the room's 15 °C, so only the boost can call for heat. Its plug's
        // default rules (05:30-22:00 and someone in the house) still apply: a boost does not bypass them.
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(12, 0, 0), "kitchen");
        context.Presence.SetRoomValue("games desk", someoneHome);
        context.Rooms[0].ScheduleTracks = [new HeatingScheduleTrack { TargetTime = new TimeOnly(0, 0), Temperature = 14 }];
        context.Rooms[0].Boost = new Boost
        {
            StartTime = new DateTimeOffset(2026, 10, 19, 11, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 10, 19, 14, 0, 0, TimeSpan.Zero),
            Temperature = 20
        };

        await context.Service.EvaluateAllSchedules("unit test");

        context.KitchenHeater.IsOn().ShouldBe(someoneHome);
        context.KitchenHeater.TurnOnCount.ShouldBe(someoneHome ? 1 : 0);
    }

    private sealed class RulesTestContext
    {
        private const double RoomTemperature = 15;
        public const double IdleValveTarget = RoomTemperature - 5;
        public const double HeatingValveTarget = RoomTemperature + 5;

        public required HeatingControlService Service { get; init; }
        public required FakeNamedEntities Entities { get; init; }
        public required MockPresenceService Presence { get; init; }
        public required FakeDeviceSettingsPersistenceService Settings { get; init; }
        public required IRoomStatePersistenceService StatePersistence { get; init; }

        /// <summary>The zones the service evaluates (each scheduled at 21 °C); tests may set a boost on one.</summary>
        public required List<RoomSchedule> Rooms { get; init; }

        public FakeCustomSwitchEntity KitchenHeater => (FakeCustomSwitchEntity)Entities.KitchenHeaterSmartPlugOnOff;
        public FakeCustomSwitchEntity Bedroom1Heater => (FakeCustomSwitchEntity)Entities.Bedroom1HeaterSmartPlugOnOff;
        public FakeCustomSwitchEntity GamesHeater => (FakeCustomSwitchEntity)Entities.GamesRoomHeaterSmartPlugOnOff;
        public FakeCustomSwitchEntity DiningHeater => (FakeCustomSwitchEntity)Entities.DiningRoomHeaterSmartPlugOnOff;
        public FakeCustomClimateControlEntity Bedroom1Radiator => (FakeCustomClimateControlEntity)Entities.Bedroom1RadiatorThermostat;
        public FakeCustomClimateControlEntity DenRadiator1 => (FakeCustomClimateControlEntity)Entities.DenRadiator1Thermostat;
        public FakeCustomClimateControlEntity DenRadiator2 => (FakeCustomClimateControlEntity)Entities.DenRadiator2Thermostat;

        public void SaveRules(Dictionary<string, DeviceRulesDto> rules)
        {
            Settings.Settings = new DeviceSettingsDto { Heating = new HeatingSettingsDto { DeviceRules = rules } };
        }

        public IReadOnlyDictionary<int, RoomState> CapturedStates()
        {
            List<Dictionary<int, RoomState>> calls = [.. StatePersistence.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name == nameof(IRoomStatePersistenceService.SetStatesAsync))
                .Select(call => (Dictionary<int, RoomState>)call.GetArguments()[0]!)];

            calls.ShouldNotBeEmpty("the service should have published room states");
            return calls[^1];
        }

        public static RulesTestContext Create(TimeSpan localTimeOfDay, params string[] zoneNames)
        {
            ServiceCollection services = new();
            ISchedulePersistenceService schedulePersistence = Substitute.For<ISchedulePersistenceService>();
            IRoomStatePersistenceService statePersistence = Substitute.For<IRoomStatePersistenceService>();
            FakeDeviceSettingsPersistenceService settings = new();

            services.AddSingleton<HistoryService, MockHistoryService>();
            services.AddSingleton(Substitute.For<IScheduler>());
            services.AddSingleton(Substitute.For<IHomeBattery>());
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
            services.AddSingleton(schedulePersistence);
            services.AddSingleton(statePersistence);
            services.AddSingleton(Substitute.For<IBoilerVerificationService>());
            services.AddSingleton(Substitute.For<IBoilerStatePersistence>());
            services.AddSingleton<IDeviceSettingsPersistenceService>(settings);

            ServiceProvider provider = services.BuildServiceProvider();

            // FakeTimeProvider is GMT; an explicit zero offset makes the local time exactly the one asked for.
            provider.GetRequiredService<FakeTimeProvider>()
                .SetSpecificDateTime(new DateTimeOffset(new DateTime(2026, 10, 19).Add(localTimeOfDay), TimeSpan.Zero));

            FakeNamedEntities entities = provider.GetRequiredService<FakeNamedEntities>();
            foreach (ICustomNumericSensorEntity thermometer in new[]
            {
                entities.KitchenTemperature, entities.Bedroom1Temperature, entities.GamesRoomDeskTemperature,
                entities.DiningRoomClimateTemperature, entities.DenClimateTemperature
            })
            {
                ((FakeCustomNumericSensorEntity)thermometer).State = RoomTemperature;
            }

            foreach (ICustomClimateControlEntity valve in new[] { entities.Bedroom1RadiatorThermostat, entities.DenRadiator1Thermostat, entities.DenRadiator2Thermostat })
            {
                FakeCustomClimateControlEntity fake = (FakeCustomClimateControlEntity)valve;
                fake.CurrentTemperature = RoomTemperature;
                fake.SetTargetTemperature(IdleValveTarget);
            }

            List<RoomSchedule> rooms = [];
            for (int i = 0; i < zoneNames.Length; i++)
            {
                rooms.Add(new RoomSchedule
                {
                    Id = i + 1,
                    Name = zoneNames[i],
                    ScheduleTracks = [new HeatingScheduleTrack { TargetTime = new TimeOnly(0, 0), Temperature = 21 }]
                });
            }

            schedulePersistence.GetSchedulesAsync().Returns(Task.FromResult(new RoomSchedules { Rooms = rooms }));

            return new RulesTestContext
            {
                Service = ActivatorUtilities.CreateInstance<HeatingControlService>(provider),
                Entities = entities,
                Presence = (MockPresenceService)provider.GetRequiredService<IPresenceService>(),
                Settings = settings,
                StatePersistence = statePersistence,
                Rooms = rooms
            };
        }
    }
}

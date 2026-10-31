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

    // Pre-heat is per device, 0-60 minutes, default 30 (owner, 2026-10-19). Bedroom 1 has both kinds of device: at
    // 15 °C now and 21 °C from 18:00, a radiator pre-heating 30 minutes ahead and a heater 10.

    [TestMethod]
    [DataRow(17, 25, false, false, DisplayName = "17:25: neither has started")]
    [DataRow(17, 40, true, false, DisplayName = "17:40: the radiator has, the heater has not")]
    [DataRow(17, 55, true, true, DisplayName = "17:55: both")]
    public async Task EachDevicePreHeatsByItsOwnTime(int hour, int minute, bool radiatorOn, bool heaterOn)
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(hour, minute, 0), "bedroom 1");
        context.Rooms[0].ScheduleTracks = Bedroom1WarmsAt18();
        context.SaveRules(new()
        {
            [context.Bedroom1Radiator.EntityId] = new DeviceRulesDto { PreHeatMinutes = 30 },
            [context.Bedroom1Heater.EntityId] = new DeviceRulesDto { PreHeatMinutes = 10 }
        });

        await context.Service.EvaluateAllSchedules("unit test");

        (context.Bedroom1Radiator.TargetTemperature == RulesTestContext.HeatingValveTarget).ShouldBe(radiatorOn);
        context.Bedroom1Heater.IsOn().ShouldBe(heaterOn);
    }

    [TestMethod]
    public async Task ARoomShowsTheHighestTargetAnyOfItsDevicesIsAimingAt()
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(17, 40, 0), "bedroom 1");
        context.Rooms[0].ScheduleTracks = Bedroom1WarmsAt18();
        context.SaveRules(new()
        {
            [context.Bedroom1Radiator.EntityId] = new DeviceRulesDto { PreHeatMinutes = 30 },
            [context.Bedroom1Heater.EntityId] = new DeviceRulesDto { PreHeatMinutes = 10 }
        });

        await context.Service.EvaluateAllSchedules("unit test");

        context.CapturedStates()[1].TargetTemperature.ShouldBe(21, "the radiator is already pre-heating for 18:00");
        context.CapturedStates()[1].ActiveScheduleTrackId.ShouldBe(2);
    }

    [TestMethod]
    [DataRow(17, 29, false)]
    [DataRow(17, 31, true)]
    public async Task PreHeatDefaultsTo30Minutes(int hour, int minute, bool radiatorOn)
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(hour, minute, 0), "bedroom 1");
        context.Rooms[0].ScheduleTracks = Bedroom1WarmsAt18();
        context.SaveRules(new() { [context.Bedroom1Radiator.EntityId] = new DeviceRulesDto() });

        await context.Service.EvaluateAllSchedules("unit test");

        (context.Bedroom1Radiator.TargetTemperature == RulesTestContext.HeatingValveTarget).ShouldBe(radiatorOn);
    }

    [TestMethod]
    public async Task NoPreHeat_WaitsForTheTrackItself()
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(17, 59, 0), "bedroom 1");
        context.Rooms[0].ScheduleTracks = Bedroom1WarmsAt18();
        context.SaveRules(new() { [context.Bedroom1Radiator.EntityId] = new DeviceRulesDto { PreHeatMinutes = 0 } });

        await context.Service.EvaluateAllSchedules("unit test");

        context.Bedroom1Radiator.TargetTemperature.ShouldBe(RulesTestContext.IdleValveTarget);
    }

    [TestMethod]
    [DataRow(null, true, 30, DisplayName = "a radiator valve defaults to 30 minutes")]
    [DataRow(null, false, 0, DisplayName = "a heater plug defaults to none")]
    [DataRow(0, true, 0)]
    [DataRow(45, false, 45)]
    [DataRow(90, true, 60)]
    [DataRow(-5, false, 0)]
    public void PreHeatDefaultsByKind_AndIsClampedTo0To60Minutes(int? saved, bool isRadiatorValve, int expected)
    {
        DeviceRulesDto.PreHeatFor(new DeviceRulesDto { PreHeatMinutes = saved }, isRadiatorValve).ShouldBe(expected);
    }

    [TestMethod]
    public async Task AHeaterWithNoPreHeatSet_WaitsForTheTrack_WhileTheRadiatorPreHeats()
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(17, 55, 0), "bedroom 1");
        context.Rooms[0].ScheduleTracks = Bedroom1WarmsAt18();
        context.SaveRules(new()
        {
            [context.Bedroom1Radiator.EntityId] = new DeviceRulesDto(),
            [context.Bedroom1Heater.EntityId] = new DeviceRulesDto()
        });

        await context.Service.EvaluateAllSchedules("unit test");

        context.Bedroom1Radiator.TargetTemperature.ShouldBe(RulesTestContext.HeatingValveTarget, "30 minutes by default");
        context.Bedroom1Heater.TurnOnCount.ShouldBe(0, "none by default: a heater warms a room quickly");
    }

    private static List<HeatingScheduleTrack> Bedroom1WarmsAt18() =>
    [
        new HeatingScheduleTrack { Id = 1, TargetTime = new TimeOnly(0, 0), Temperature = 15 },
        new HeatingScheduleTrack { Id = 2, TargetTime = new TimeOnly(18, 0), Temperature = 21 }
    ];

    [TestMethod]
    public async Task EachPass_UsesTheSavedChoiceOfDetectorsPerZone()
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(12, 0, 0), "kitchen");
        Dictionary<string, List<string>> occupancy = new() { ["kitchen"] = [PresenceService.LivingRoomLightsOn] };
        context.Settings.Settings = new DeviceSettingsDto { Heating = new HeatingSettingsDto { ZoneOccupancy = occupancy } };

        await context.Service.EvaluateAllSchedules("unit test");

        context.Presence.LastZoneOccupancy.ShouldBeSameAs(occupancy);
    }

    [TestMethod]
    public async Task House_IsOnlyTheZonesThatMeanSomeoneIsHome()
    {
        // Ruth's desk plug switched on makes the dining room zone "in use", but nobody need be there, so it is not
        // one of the house zones and must not run the kitchen heater (owner's rules, 2026-10-19).
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(12, 0, 0), "kitchen");
        context.Presence.SetRoomValue("games desk", false);
        context.Presence.SetRoomValue("dining room", true);
        context.Presence.HouseZonesOverride = ["games desk"];

        await context.Service.EvaluateAllSchedules("desk plug left on, nobody home");

        context.KitchenHeater.TurnOnCount.ShouldBe(0);
    }

    [TestMethod]
    public async Task SwitchingAZoneOn_SendsEachDeviceOneCommand()
    {
        // 2026-10-19: the kitchen plug was turned on twice in one pass - once by the zone, then again by the
        // device-rules pass, which read the plug before HA had reported it on.
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(12, 0, 0), "kitchen");
        context.Presence.SetRoomValue("games desk", true);
        context.KitchenHeater.ReportsLate = true;

        await context.Service.EvaluateAllSchedules("cold, someone home");

        context.KitchenHeater.TurnOnCount.ShouldBe(1);
    }

    [TestMethod]
    public async Task AChangedTarget_IsUploadedEvenIfNothingElseChanged()
    {
        // 2026-10-19: after a restart the UI showed no targets for 7 minutes, because a new target or track
        // never triggered an upload; only a changed temperature or heating state did.
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(12, 0, 0), "kitchen");
        context.Presence.SetRoomValue("games desk", false);
        await context.Service.EvaluateAllSchedules("first pass");
        int uploads = context.UploadCount();

        // Still cold and still held off by its rules: only the target moves.
        context.Rooms[0].ScheduleTracks = [new HeatingScheduleTrack { TargetTime = new TimeOnly(0, 0), Temperature = 22 }];
        await context.Service.EvaluateAllSchedules("schedule edited");

        context.UploadCount().ShouldBe(uploads + 1);
        context.CapturedStates()[1].TargetTemperature.ShouldBe(22);
    }

    [TestMethod]
    public async Task AZoneGainingPresence_IsUploadedEvenIfNothingElseChanged()
    {
        // 2026-10-20: choosing a presence detector for Ellie's room did not offer the in-use / not-in-use tracks in
        // the schedule editor, because the zone's new capability was recomputed but never uploaded until some
        // other part of its state (temperature, target, track) happened to change.
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(12, 0, 0), "kitchen");
        context.Presence.SetRoomValue("games desk", false);
        await context.Service.EvaluateAllSchedules("first pass");
        context.CapturedStates()[1].Capabilities.HasFlag(RoomCapabilities.CanDetectRoomOccupancy).ShouldBeFalse();
        int uploads = context.UploadCount();

        context.Presence.SetRoomValue("kitchen", false);
        await context.Service.EvaluateAllSchedules("occupancy chosen for the zone");

        context.UploadCount().ShouldBe(uploads + 1);
        context.CapturedStates()[1].Capabilities.HasFlag(RoomCapabilities.CanDetectRoomOccupancy).ShouldBeTrue();
    }

    [TestMethod]
    public async Task ChangedDeviceRules_AreUploadedEvenIfNothingElseChanged()
    {
        // Same gap as above for the Devices list: the rules shown on a device only reached the UI when something else
        // about the zone changed.
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(12, 0, 0), "kitchen");
        context.Presence.SetRoomValue("games desk", false);
        await context.Service.EvaluateAllSchedules("first pass");
        int uploads = context.UploadCount();

        // Still held off at noon, so only the rules shown on the device differ.
        context.SaveRules(new()
        {
            [context.KitchenHeater.EntityId] = new DeviceRulesDto
            {
                TimeWindows = [new TimeWindowDto { Start = "01:00", End = "02:00" }]
            }
        });
        await context.Service.EvaluateAllSchedules("rules saved");

        context.UploadCount().ShouldBe(uploads + 1);
    }

    [TestMethod]
    public async Task ARoomWithNothingToEvaluate_StillUploadsItsChangedTemperature()
    {
        // A zone with no schedule applying is not evaluated, so its thermometer used to reach the UI only when some
        // other room happened to upload.
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(12, 0, 0), "kitchen");
        context.Rooms[0].ScheduleTracks = [];
        await context.Service.EvaluateAllSchedules("first pass");
        int uploads = context.UploadCount();

        ((FakeCustomNumericSensorEntity)context.Entities.KitchenTemperature).State = 16.5;
        await context.Service.EvaluateAllSchedules("kitchen temperature changed");

        context.UploadCount().ShouldBe(uploads + 1);
        context.CapturedStates()[1].CurrentTemperature.ShouldBe(16.5);
    }

    [TestMethod]
    public async Task ChangingOnlyAReading_DoesNotUploadAgain()
    {
        // A device's live state changes constantly and must not, by itself, be a reason to upload.
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(12, 0, 0), "kitchen");
        context.Presence.SetRoomValue("games desk", false);
        await context.Service.EvaluateAllSchedules("first pass");
        int uploads = context.UploadCount();

        await context.Service.EvaluateAllSchedules("nothing changed");

        context.UploadCount().ShouldBe(uploads);
    }

    // An Auto pre-heat (a radiator valve with none set, owner 2026-10-20) is worked out from how fast that valve has
    // warmed its room. Bedroom 1 is 15 °C now and 21 °C from 18:00: 6 degrees to find.

    [TestMethod]
    [DataRow(16, 25, false, DisplayName = "4 °C an hour needs 90 minutes: not yet at 16:25")]
    [DataRow(16, 35, true, DisplayName = "16:35: it has started")]
    public async Task AnAutoValve_StartsWhenItsMeasuredWarmUpReachesBackFromTheTrack(int hour, int minute, bool radiatorOn)
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(hour, minute, 0), "bedroom 1");
        context.Rooms[0].ScheduleTracks = Bedroom1WarmsAt18();
        context.SaveRules(new() { [context.Bedroom1Radiator.EntityId] = new DeviceRulesDto() });
        context.GiveRadiatorARate(context.Bedroom1Radiator.EntityId, 4);

        await context.Service.EvaluateAllSchedules("unit test");

        (context.Bedroom1Radiator.TargetTemperature == RulesTestContext.HeatingValveTarget).ShouldBe(radiatorOn);
    }

    [TestMethod]
    public async Task AnAutoValve_WithAFasterRate_StartsLater()
    {
        // 12 °C an hour: 6 degrees takes only 30 minutes, so it starts at 17:30 and 17:20 is too early.
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(17, 20, 0), "bedroom 1");
        context.Rooms[0].ScheduleTracks = Bedroom1WarmsAt18();
        context.SaveRules(new() { [context.Bedroom1Radiator.EntityId] = new DeviceRulesDto() });
        context.GiveRadiatorARate(context.Bedroom1Radiator.EntityId, 12);

        await context.Service.EvaluateAllSchedules("17:20");

        context.Bedroom1Radiator.TargetTemperature.ShouldBe(RulesTestContext.IdleValveTarget, "30 minutes before 18:00 is 17:30");
    }

    [TestMethod]
    public async Task AnAutoValve_NeverStartsMoreThanTwoHoursAhead()
    {
        // 1 °C an hour would want six hours.
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(15, 50, 0), "bedroom 1");
        context.Rooms[0].ScheduleTracks = Bedroom1WarmsAt18();
        context.SaveRules(new() { [context.Bedroom1Radiator.EntityId] = new DeviceRulesDto() });
        context.GiveRadiatorARate(context.Bedroom1Radiator.EntityId, 1);

        await context.Service.EvaluateAllSchedules("15:50");
        context.Bedroom1Radiator.TargetTemperature.ShouldBe(RulesTestContext.IdleValveTarget, "more than 120 minutes before 18:00");

        context.SetLocalTime(new TimeSpan(16, 5, 0));
        await context.Service.EvaluateAllSchedules("16:05");
        context.Bedroom1Radiator.TargetTemperature.ShouldBe(RulesTestContext.HeatingValveTarget);
    }

    [TestMethod]
    public async Task AnAutoValve_WithNoDataYet_UsesThirtyMinutes()
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(17, 29, 0), "bedroom 1");
        context.Rooms[0].ScheduleTracks = Bedroom1WarmsAt18();
        context.SaveRules(new() { [context.Bedroom1Radiator.EntityId] = new DeviceRulesDto() });

        await context.Service.EvaluateAllSchedules("17:29");
        context.Bedroom1Radiator.TargetTemperature.ShouldBe(RulesTestContext.IdleValveTarget);

        context.SetLocalTime(new TimeSpan(17, 31, 0));
        await context.Service.EvaluateAllSchedules("17:31");
        context.Bedroom1Radiator.TargetTemperature.ShouldBe(RulesTestContext.HeatingValveTarget);
    }

    [TestMethod]
    public async Task AFixedPreHeat_IsNotAutomatic_EvenForAMeasuredValve()
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(16, 35, 0), "bedroom 1");
        context.Rooms[0].ScheduleTracks = Bedroom1WarmsAt18();
        context.SaveRules(new() { [context.Bedroom1Radiator.EntityId] = new DeviceRulesDto { PreHeatMinutes = 30 } });
        context.GiveRadiatorARate(context.Bedroom1Radiator.EntityId, 4);

        await context.Service.EvaluateAllSchedules("16:35");

        context.Bedroom1Radiator.TargetTemperature.ShouldBe(RulesTestContext.IdleValveTarget);
    }

    [TestMethod]
    public async Task AnAutoPreHeat_OnceStarted_IsKeptUntilTheTrackBegins()
    {
        // As the room warms the time still needed shrinks as fast as the time left does, so a rate a little too
        // pessimistic would otherwise shut the valve again part-way and start over.
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(16, 35, 0), "bedroom 1");
        context.Rooms[0].ScheduleTracks = Bedroom1WarmsAt18();
        context.SaveRules(new() { [context.Bedroom1Radiator.EntityId] = new DeviceRulesDto() });
        context.GiveRadiatorARate(context.Bedroom1Radiator.EntityId, 4);
        await context.Service.EvaluateAllSchedules("16:35");
        context.Bedroom1Radiator.TargetTemperature.ShouldBe(RulesTestContext.HeatingValveTarget);

        // The room warmed much faster than expected: 20.5 °C at 16:50, when only 0.5 degrees (8 minutes) is left to find.
        ((FakeCustomNumericSensorEntity)context.Entities.Bedroom1Temperature).State = 20.5;
        context.Bedroom1Radiator.CurrentTemperature = 20.5;
        context.SetLocalTime(new TimeSpan(16, 50, 0));
        await context.Service.EvaluateAllSchedules("16:50");

        context.CapturedStates()[1].TargetTemperature.ShouldBe(21, "still pre-heating for 18:00");
    }

    [TestMethod]
    public async Task ARadiatorValve_PublishesItsRateAndHowLongItWouldTake()
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(12, 0, 0), "bedroom 1");
        context.Rooms[0].ScheduleTracks = Bedroom1WarmsAt18();
        context.SaveRules(new() { [context.Bedroom1Radiator.EntityId] = new DeviceRulesDto() });
        context.GiveRadiatorARate(context.Bedroom1Radiator.EntityId, 4);

        await context.Service.EvaluateAllSchedules("unit test");

        RoomDeviceDto valve = context.CapturedStates()[1].Devices.Single(d => d.Role == RoomDeviceRoles.RadiatorValve);
        valve.PreHeatAuto.ShouldBeTrue();
        valve.HeatingRateSamples.ShouldBe(1);
        valve.HeatingRateCelsiusPerHour.ShouldNotBeNull().ShouldBe(4, 0.01);
        valve.WarmUpMinutes.ShouldBe(90, "15 °C to the 21 °C at 18:00, 6 degrees at 4 °C an hour");
    }

    [TestMethod]
    public async Task ARadiatorValve_PublishesWhatTheValveHealthMonitorHasSeen()
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(12, 0, 0), "den");
        ValveHealthMonitorTests.SeeALeak(context.ValveHealth, context.Entities.DenRadiator2Thermostat.EntityId, new DateTimeOffset(2026, 10, 19, 6, 0, 0, TimeSpan.Zero));

        await context.Service.EvaluateAllSchedules("unit test");

        List<RoomDeviceDto> valves = [.. context.CapturedStates()[1].Devices.Where(d => d.Role == RoomDeviceRoles.RadiatorValve)];
        ValveHealthIssueDto leak = valves.Single(v => v.EntityId == context.Entities.DenRadiator2Thermostat.EntityId)
            .ValveHealth.ShouldNotBeNull().ShouldHaveSingleItem();
        leak.Kind.ShouldBe(ValveHealthKinds.LeaksWhenShut);
        leak.Flagged.ShouldBeTrue();
        leak.Count.ShouldBe(1);
        leak.LastSeenAt.ShouldNotBeNull();
        leak.LastSeenDetail.ShouldNotBeNull().ShouldStartWith("Rose from 19.0 °C to ");
        leak.LastOkAt.ShouldBeNull();

        valves.Single(v => v.EntityId == context.Entities.DenRadiator1Thermostat.EntityId)
            .ValveHealth.ShouldNotBeNull().ShouldBeEmpty("nothing has been seen of it");
    }

    [TestMethod]
    public async Task AValveWithNoData_PublishesNoRate_ButIsStillAuto()
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(12, 0, 0), "bedroom 1");
        context.Rooms[0].ScheduleTracks = Bedroom1WarmsAt18();

        await context.Service.EvaluateAllSchedules("unit test");

        RoomDeviceDto valve = context.CapturedStates()[1].Devices.Single(d => d.Role == RoomDeviceRoles.RadiatorValve);
        valve.PreHeatAuto.ShouldBeTrue();
        valve.HeatingRateSamples.ShouldBe(0);
        valve.HeatingRateCelsiusPerHour.ShouldBeNull();
        valve.WarmUpMinutes.ShouldBeNull();
    }

    // A valve with no warm-up of its own goes by a measured valve's in the same zone (owner, 2026-10-23). On the
    // 30-minute fallback den radiator 2 opened after radiator 1 had nearly warmed the room, which then reached the
    // target too soon for the run to count, so it could never be measured.

    [TestMethod]
    [DataRow(16, 25, false, DisplayName = "4 °C an hour needs 90 minutes: not yet at 16:25")]
    [DataRow(16, 35, true, DisplayName = "16:35: both valves have started")]
    public async Task AnUnmeasuredValve_GoesByTheMeasuredValveInItsZone(int hour, int minute, bool radiatorsOn)
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(hour, minute, 0), "den");
        context.Rooms[0].ScheduleTracks = Bedroom1WarmsAt18();
        context.SaveRules(new()
        {
            [context.DenRadiator1.EntityId] = new DeviceRulesDto(),
            [context.DenRadiator2.EntityId] = new DeviceRulesDto()
        });
        context.GiveRadiatorARate(context.DenRadiator1.EntityId, 4);

        await context.Service.EvaluateAllSchedules("unit test");

        (context.DenRadiator1.TargetTemperature == RulesTestContext.HeatingValveTarget).ShouldBe(radiatorsOn);
        (context.DenRadiator2.TargetTemperature == RulesTestContext.HeatingValveTarget).ShouldBe(radiatorsOn, "it goes by radiator 1's rate, not 30 minutes");
    }

    [TestMethod]
    public async Task AnUnmeasuredValve_PublishesWhoseRateItGoesBy()
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(12, 0, 0), "den");
        context.Rooms[0].ScheduleTracks = Bedroom1WarmsAt18();
        context.GiveRadiatorARate(context.DenRadiator1.EntityId, 4);

        await context.Service.EvaluateAllSchedules("unit test");

        List<RoomDeviceDto> valves = [.. context.CapturedStates()[1].Devices.Where(d => d.Role == RoomDeviceRoles.RadiatorValve)];
        RoomDeviceDto measured = valves.Single(d => d.EntityId == context.DenRadiator1.EntityId);
        RoomDeviceDto borrowing = valves.Single(d => d.EntityId == context.DenRadiator2.EntityId);

        measured.HeatingRateFromEntityId.ShouldBeNull("its rate is its own");
        borrowing.HeatingRateFromEntityId.ShouldBe(context.DenRadiator1.EntityId);
        borrowing.HeatingRateCelsiusPerHour.ShouldNotBeNull().ShouldBe(4, 0.01);
        borrowing.HeatingRateSamples.ShouldBe(1, "radiator 1's samples");
        borrowing.WarmUpMinutes.ShouldBe(90);
        context.WarmUp.SampleCount(context.DenRadiator2.EntityId).ShouldBe(0, "nothing is copied into its own warm-ups");
    }

    [TestMethod]
    public async Task AZoneWithNoMeasuredValve_StillUsesThirtyMinutes()
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(17, 29, 0), "den");
        context.Rooms[0].ScheduleTracks = Bedroom1WarmsAt18();

        await context.Service.EvaluateAllSchedules("17:29");
        context.DenRadiator2.TargetTemperature.ShouldBe(RulesTestContext.IdleValveTarget);

        context.SetLocalTime(new TimeSpan(17, 31, 0));
        await context.Service.EvaluateAllSchedules("17:31");
        context.DenRadiator2.TargetTemperature.ShouldBe(RulesTestContext.HeatingValveTarget);
        context.CapturedStates()[1].Devices.Single(d => d.EntityId == context.DenRadiator2.EntityId).HeatingRateFromEntityId.ShouldBeNull();
    }

    [TestMethod]
    public async Task AnAutoHeaterPlug_NeverGoesByAValvesRate()
    {
        // Bedroom 1's radiator is measured at 4 °C an hour, which would start at 16:30; its Auto heater has no rate of its own.
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(16, 35, 0), "bedroom 1");
        context.Rooms[0].ScheduleTracks = Bedroom1WarmsAt18();
        context.SaveRules(new() { [context.Bedroom1Heater.EntityId] = new DeviceRulesDto { PreHeatAuto = true } });
        context.GiveRadiatorARate(context.Bedroom1Radiator.EntityId, 4);

        await context.Service.EvaluateAllSchedules("16:35");

        context.Bedroom1Heater.TurnOnCount.ShouldBe(0, "a fan heater warms a room far faster than a radiator");
        context.CapturedStates()[1].Devices.Single(d => d.Role == RoomDeviceRoles.HeaterPlug).HeatingRateFromEntityId.ShouldBeNull();
    }

    [TestMethod]
    public async Task AnUnmeasuredValve_NeverGoesByAHeaterPlugsRate()
    {
        // The heater's 4 °C an hour would start the radiator at 16:30; on its own fallback it waits until 17:30.
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(16, 35, 0), "bedroom 1");
        context.Rooms[0].ScheduleTracks = Bedroom1WarmsAt18();
        context.GiveRadiatorARate(context.Bedroom1Heater.EntityId, 4);

        await context.Service.EvaluateAllSchedules("16:35");

        context.Bedroom1Radiator.TargetTemperature.ShouldBe(RulesTestContext.IdleValveTarget);
        context.CapturedStates()[1].Devices.Single(d => d.Role == RoomDeviceRoles.RadiatorValve).HeatingRateFromEntityId.ShouldBeNull();
    }

    [TestMethod]
    public async Task AHeaterPlug_IsNotAutoByDefault()
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(12, 0, 0), "bedroom 1");
        context.Rooms[0].ScheduleTracks = Bedroom1WarmsAt18();

        await context.Service.EvaluateAllSchedules("unit test");

        context.CapturedStates()[1].Devices.Single(d => d.Role == RoomDeviceRoles.HeaterPlug).PreHeatAuto.ShouldBeFalse();
    }

    // Heater plugs (owner, 2026-10-21): a pre-heat of none by default, but they can be given a fixed one or Auto, and
    // whatever the pre-heat they still only run while their rules pass.

    [TestMethod]
    [DataRow(16, 25, false, DisplayName = "4 °C an hour needs 90 minutes: not yet at 16:25")]
    [DataRow(16, 35, true, DisplayName = "16:35: it has started")]
    public async Task AnAutoHeaterPlug_StartsWhenItsMeasuredWarmUpReachesBackFromTheTrack(int hour, int minute, bool heaterOn)
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(hour, minute, 0), "bedroom 1");
        context.Rooms[0].ScheduleTracks = Bedroom1WarmsAt18();
        context.SaveRules(new() { [context.Bedroom1Heater.EntityId] = new DeviceRulesDto { PreHeatAuto = true } });
        context.GiveRadiatorARate(context.Bedroom1Heater.EntityId, 4);

        await context.Service.EvaluateAllSchedules("unit test");

        context.Bedroom1Heater.IsOn().ShouldBe(heaterOn);
        RoomDeviceDto heater = context.CapturedStates()[1].Devices.Single(d => d.Role == RoomDeviceRoles.HeaterPlug);
        heater.PreHeatAuto.ShouldBeTrue();
        heater.HeatingRateCelsiusPerHour.ShouldNotBeNull().ShouldBe(4, 0.01);
    }

    [TestMethod]
    public async Task AnAutoHeaterPlug_StillWaitsForItsRules()
    {
        // The same measured warm-up as above, but the heater needs someone home, and nobody is.
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(16, 35, 0), "bedroom 1");
        context.Rooms[0].ScheduleTracks = Bedroom1WarmsAt18();
        context.Presence.SetRoomValue("games desk", false);
        context.SaveRules(new()
        {
            [context.Bedroom1Heater.EntityId] = new DeviceRulesDto
            {
                PreHeatAuto = true,
                Presence = new PresenceRuleDto { Sources = [PresenceSources.House] }
            }
        });
        context.GiveRadiatorARate(context.Bedroom1Heater.EntityId, 4);

        await context.Service.EvaluateAllSchedules("unit test");
        context.Bedroom1Heater.TurnOnCount.ShouldBe(0, "Auto pre-heat does not get round the heater's presence rule");

        context.Presence.SetRoomValue("games desk", true);
        await context.Service.EvaluateAllSchedules("someone came home");
        context.Bedroom1Heater.IsOn().ShouldBeTrue();
    }

    [TestMethod]
    public async Task AnAutoHeaterPlug_WithNoDataYet_HasNoPreHeat()
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(17, 50, 0), "bedroom 1");
        context.Rooms[0].ScheduleTracks = Bedroom1WarmsAt18();
        context.SaveRules(new() { [context.Bedroom1Heater.EntityId] = new DeviceRulesDto { PreHeatAuto = true } });

        await context.Service.EvaluateAllSchedules("17:50");

        context.Bedroom1Heater.TurnOnCount.ShouldBe(0, "a heater's default pre-heat is none until it has been measured");
    }

    [TestMethod]
    public async Task AHeaterPlugWarmingItsRoom_IsMeasured()
    {
        // 15 °C at 12:00 against 21 °C, reached at 12:20: a 20-minute warm-up, long enough for a heater (not a radiator).
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(12, 0, 0), "bedroom 1");
        context.Rooms[0].ScheduleTracks = [new HeatingScheduleTrack { Id = 1, TargetTime = new TimeOnly(0, 0), Temperature = 21 }];
        context.SaveRules(new() { [context.Bedroom1Heater.EntityId] = new DeviceRulesDto() });

        await context.Service.EvaluateAllSchedules("12:00");
        context.Bedroom1Heater.IsOn().ShouldBeTrue();

        ((FakeCustomNumericSensorEntity)context.Entities.Bedroom1Temperature).State = 18;
        context.SetLocalTime(new TimeSpan(12, 10, 0));
        await context.Service.EvaluateAllSchedules("12:10");

        ((FakeCustomNumericSensorEntity)context.Entities.Bedroom1Temperature).State = 21;
        context.SetLocalTime(new TimeSpan(12, 20, 0));
        await context.Service.EvaluateAllSchedules("12:20");

        context.WarmUp.SampleCount(context.Bedroom1Heater.EntityId).ShouldBe(1);
        context.WarmUp.RateFor(context.Bedroom1Heater.EntityId).ShouldNotBeNull().ShouldBe(18, 0.01, "6 degrees in 20 minutes");
        context.WarmUp.SampleCount(context.Bedroom1Radiator.EntityId).ShouldBe(0, "a valve only counts with the boiler on");
    }

    [TestMethod]
    [DataRow(null, null, true, true)]
    [DataRow(null, null, false, false)]
    [DataRow(null, 20, true, false)]
    [DataRow(true, null, false, true)]
    [DataRow(true, 20, false, true)]
    [DataRow(false, null, true, false)]
    public void AutoPreHeat_IsWhatPreHeatAutoSays_ElseAValveWithNoMinutes(bool? preHeatAuto, int? minutes, bool isRadiatorValve, bool expected)
    {
        DeviceRulesDto.IsAutoPreHeat(new DeviceRulesDto { PreHeatAuto = preHeatAuto, PreHeatMinutes = minutes }, isRadiatorValve).ShouldBe(expected);
    }

    // A track held back only by its "room in use" / "room not in use" condition is published, so the UI can say why
    // on the track (owner, 2026-10-21).

    private static List<HeatingScheduleTrack> Bedroom1InUseAt7() =>
    [
        new HeatingScheduleTrack { Id = 1, TargetTime = new TimeOnly(6, 0), Temperature = 15 },
        new HeatingScheduleTrack { Id = 2, TargetTime = new TimeOnly(7, 0), Temperature = 21, Conditions = ConditionType.RoomInUse },
        new HeatingScheduleTrack { Id = 3, TargetTime = new TimeOnly(7, 0), Temperature = 16, Conditions = ConditionType.RoomNotInUse }
    ];

    [TestMethod]
    [DataRow(false, 2, DisplayName = "Not in use: the in-use track waits")]
    [DataRow(true, 3, DisplayName = "In use: the not-in-use track waits")]
    public async Task ATrackDueButForItsOccupancyCondition_IsPublishedAsWaiting(bool inUse, int expected)
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(12, 0, 0), "bedroom 1");
        context.Rooms[0].ScheduleTracks = Bedroom1InUseAt7();
        context.Presence.SetRoomValue("bedroom 1", inUse);

        await context.Service.EvaluateAllSchedules("unit test");

        context.CapturedStates()[1].WaitingForOccupancyTrackIds.ShouldBe([expected]);
    }

    [TestMethod]
    public async Task ATrackALaterOneHasSuperseded_IsNotWaiting()
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(12, 0, 0), "bedroom 1");
        context.Rooms[0].ScheduleTracks = [.. Bedroom1InUseAt7(), new HeatingScheduleTrack { Id = 4, TargetTime = new TimeOnly(9, 0), Temperature = 18 }];
        context.Presence.SetRoomValue("bedroom 1", false);

        await context.Service.EvaluateAllSchedules("unit test");

        context.CapturedStates()[1].WaitingForOccupancyTrackIds.ShouldBeEmpty("09:00 took over from both 07:00 tracks");
    }

    [TestMethod]
    public async Task NoTrackWaits_WhileABoostRuns()
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(12, 0, 0), "bedroom 1");
        context.Rooms[0].ScheduleTracks = Bedroom1InUseAt7();
        context.Presence.SetRoomValue("bedroom 1", false);
        context.Rooms[0].Boost = new Boost
        {
            StartTime = new DateTimeOffset(2026, 10, 19, 11, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 10, 19, 14, 0, 0, TimeSpan.Zero),
            Temperature = 20
        };

        await context.Service.EvaluateAllSchedules("unit test");

        context.CapturedStates()[1].WaitingForOccupancyTrackIds.ShouldBeEmpty();
    }

    [TestMethod]
    public void TheNextWarmerTrack_IsWhatAWarmUpIsMeasuredAgainst()
    {
        RoomSchedule room = new()
        {
            Id = 1,
            Name = "bedroom 1",
            ScheduleTracks =
            [
                new HeatingScheduleTrack { Id = 1, TargetTime = new TimeOnly(6, 0), Temperature = 15 },
                new HeatingScheduleTrack { Id = 2, TargetTime = new TimeOnly(18, 0), Temperature = 21 }
            ]
        };

        HeatingControlService.NextWarmerTrackTemperature(room, new DateTime(2026, 10, 20, 12, 0, 0), 15).ShouldBe(21);
        HeatingControlService.NextWarmerTrackTemperature(room, new DateTime(2026, 10, 20, 12, 0, 0), 21).ShouldBeNull("the room is already as warm as any track");
    }

    [TestMethod]
    public void TheNextWarmerTrack_AfterTheLastOfTheDay_IsTomorrowsFirst()
    {
        RoomSchedule room = new()
        {
            Id = 1,
            Name = "bedroom 1",
            ScheduleTracks =
            [
                new HeatingScheduleTrack { Id = 1, TargetTime = new TimeOnly(6, 30), Temperature = 20 },
                new HeatingScheduleTrack { Id = 2, TargetTime = new TimeOnly(22, 0), Temperature = 15 }
            ]
        };

        HeatingControlService.NextWarmerTrackTemperature(room, new DateTime(2026, 10, 20, 23, 0, 0), 15).ShouldBe(20);
    }

    [TestMethod]
    public void TheNextWarmerTrack_RespectsTheDaysATrackRuns()
    {
        // 2026-10-20 is a Tuesday, so the Wednesday-only 21 °C track is tomorrow's, and Tuesday's own 22 °C comes first.
        RoomSchedule room = new()
        {
            Id = 1,
            Name = "bedroom 1",
            ScheduleTracks =
            [
                new HeatingScheduleTrack { Id = 1, TargetTime = new TimeOnly(18, 0), Temperature = 21, Days = Days.Wednesday },
                new HeatingScheduleTrack { Id = 2, TargetTime = new TimeOnly(19, 0), Temperature = 22, Days = Days.Tuesday }
            ]
        };

        HeatingControlService.NextWarmerTrackTemperature(room, new DateTime(2026, 10, 20, 12, 0, 0), 15).ShouldBe(22);
    }

    // A thermometer that stops reporting (owner, 2026-10-31; HOME-ASSISTANT.md gotcha 32): Ste's desk thermometer went
    // silent at 12:57, HA kept its 19.2 °C and called it available, and the desk heater ran on into a 22 °C room.

    [TestMethod]
    public async Task ASilentThermometer_SwitchesItsZonesHeaterOff_UntilItReportsAgain()
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(16, 0, 0), "games desk");
        context.Presence.SetRoomValue("games desk", true);
        await context.Service.EvaluateAllSchedules("desk cold");
        context.GamesHeater.IsOn().ShouldBeTrue();
        string thermometer = context.Entities.GamesRoomDeskTemperature.EntityId;

        context.Thermometers.SilentFor(thermometer, HeatingControlService.ThermometerSilentAfter + TimeSpan.FromMinutes(1));
        await context.Service.EvaluateAllSchedules("5 minute period recheck");

        context.GamesHeater.IsOff().ShouldBeTrue("its last reading is no longer the room's temperature");
        RoomState desk = context.CapturedStates()[1];
        desk.ThermometerSilentSince.ShouldNotBeNull();
        desk.CurrentTemperature.ShouldBeNull();
        desk.HeatingActive.ShouldBeFalse();

        context.Thermometers.Reporting(thermometer);
        await context.Service.EvaluateAllSchedules("desk thermometer reported");

        context.GamesHeater.IsOn().ShouldBeTrue("reporting again, and still cold");
        context.CapturedStates()[1].ThermometerSilentSince.ShouldBeNull();
    }

    [TestMethod]
    public async Task AThermometerThatReportedJustInside_TheLimit_IsTrusted()
    {
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(16, 0, 0), "games desk");
        context.Presence.SetRoomValue("games desk", true);
        context.Thermometers.SilentFor(context.Entities.GamesRoomDeskTemperature.EntityId, HeatingControlService.ThermometerSilentAfter - TimeSpan.FromMinutes(1));

        await context.Service.EvaluateAllSchedules("desk cold");

        context.GamesHeater.IsOn().ShouldBeTrue();
    }

    [TestMethod]
    public async Task AThermometerWhoseReportTimeCannotBeRead_IsTakenAsWorking()
    {
        // HA unreachable: a check that cannot run must not stop the heating.
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(16, 0, 0), "games desk");
        context.Presence.SetRoomValue("games desk", true);
        context.Thermometers.Unknown(context.Entities.GamesRoomDeskTemperature.EntityId);

        await context.Service.EvaluateAllSchedules("desk cold");

        context.GamesHeater.IsOn().ShouldBeTrue();
    }

    [TestMethod]
    public async Task AnUnavailableThermometer_SwitchesItsZonesHeaterOff()
    {
        // Until 2026-10-31 a zone with no reading was skipped, and a heater that was on stayed on.
        RulesTestContext context = RulesTestContext.Create(new TimeSpan(16, 0, 0), "games desk");
        context.Presence.SetRoomValue("games desk", true);
        await context.Service.EvaluateAllSchedules("desk cold");
        context.GamesHeater.IsOn().ShouldBeTrue();

        ((FakeCustomNumericSensorEntity)context.Entities.GamesRoomDeskTemperature).State = null;
        await context.Service.EvaluateAllSchedules("desk thermometer unavailable");

        context.GamesHeater.IsOff().ShouldBeTrue();
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

        /// <summary>Measured warm-ups, empty until a test gives a valve a rate with <see cref="GiveRadiatorARate"/>.</summary>
        public required WarmUpRates WarmUp { get; init; }
        public required FakeTimeProvider Clock { get; init; }

        /// <summary>The valve-health monitor the service publishes from.</summary>
        public required IValveHealthMonitor ValveHealth { get; init; }

        /// <summary>When each thermometer last reported; every one is reporting until a test says otherwise.</summary>
        public required FakeThermometerReports Thermometers { get; init; }

        /// <summary>Makes the valve's measured rate this many degrees an hour, as if it had warmed its room at that pace.</summary>
        public void GiveRadiatorARate(string entityId, double degreesPerHour) =>
            WarmUpRatesTests.HeatToTarget(WarmUp, entityId, new DateTimeOffset(2026, 10, 1, 6, 0, 0, TimeSpan.Zero), 14, 17, degreesPerHour);

        public void SetLocalTime(TimeSpan localTimeOfDay) =>
            Clock.SetSpecificDateTime(new DateTimeOffset(new DateTime(2026, 10, 19).Add(localTimeOfDay), TimeSpan.Zero));

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

        /// <summary>How many times room states were uploaded. Each upload passes the same live dictionary, so
        /// <see cref="CapturedStates"/> alone cannot tell whether a change was uploaded.</summary>
        public int UploadCount() => StatePersistence.ReceivedCalls()
            .Count(call => call.GetMethodInfo().Name == nameof(IRoomStatePersistenceService.SetStatesAsync));

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
            services.AddSingleton<IDeviceSettingsPersistenceService>(settings);
            services.AddSingleton<IWarmUpRateStore, InMemoryWarmUpRateStore>();
            services.AddSingleton<WarmUpRates>();
            services.AddSingleton<IWarmUpRates>(provider => provider.GetRequiredService<WarmUpRates>());
            services.AddSingleton<IValveHealthStore, InMemoryValveHealthStore>();
            services.AddSingleton<IValveHealthMonitor, ValveHealthMonitor>();
            services.AddSingleton(provider => new FakeThermometerReports(provider.GetRequiredService<FakeTimeProvider>()));
            services.AddSingleton<IThermometerReports>(provider => provider.GetRequiredService<FakeThermometerReports>());

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
                Rooms = rooms,
                WarmUp = provider.GetRequiredService<WarmUpRates>(),
                Clock = provider.GetRequiredService<FakeTimeProvider>(),
                ValveHealth = provider.GetRequiredService<IValveHealthMonitor>(),
                Thermometers = provider.GetRequiredService<FakeThermometerReports>()
            };
        }
    }
}

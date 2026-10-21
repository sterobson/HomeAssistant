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
/// The boiler has two exclusive control paths - the fingerbot pressing its physical button, or a
/// boiler thermostat (portable, normally in the kitchen) - chosen by a physical switch that Home Assistant
/// cannot read. In boiler-thermostat mode the boiler's button is dead, so a fingerbot press is a no-op,
/// and verifying one against radiator temperatures reads the thermostat's own burns as evidence.
/// These tests cover the gate that keeps the app's hands off the boiler in that mode.
/// </summary>
[TestClass]
public sealed class HeatingBoilerControlTests
{
    private const string DisabledLogFragment = "Boiler control is DISABLED";
    private const string EnabledLogFragment = "Boiler control is ENABLED";

    [TestMethod]
    public async Task Batteries_GoUpWithTheRoomStates_AndAChangedLevelIsReasonEnoughToUpload()
    {
        // The UI's low-battery icons come from the room-state upload (owner, 2026-10-20). Levels move slowly and
        // without touching the heating, so a changed battery has to trigger an upload by itself.
        FakeBatteryOverview batteries = new();
        batteries.Batteries.Add(new BatteryDeviceDto
        {
            EntityId = "sensor.desk_button_battery", Name = "Dining Room - Desk - Button", Percent = 22, Area = "Dining Room",
            DeviceEntityIds = ["sensor.desk_button_battery"]
        });
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, 10, 10, batteries: batteries);

        await context.Service.EvaluateAllSchedules("first pass");
        await context.Service.EvaluateAllSchedules("settled");
        context.LastUploadedBatteries()!.Single().Percent.ShouldBe(22);
        int uploads = context.UploadCount();

        await context.Service.EvaluateAllSchedules("nothing changed");
        context.UploadCount().ShouldBe(uploads, "nothing changed, so nothing to send");

        batteries.Batteries[0].Percent = 19;
        await context.Service.EvaluateAllSchedules("the button's battery dropped");
        context.UploadCount().ShouldBe(uploads + 1);
        context.LastUploadedBatteries()!.Single().Percent.ShouldBe(19);
    }

    /// <summary>Batteries as the test sets them; each read returns copies, as the real overview builds new ones.</summary>
    private sealed class FakeBatteryOverview : IBatteryOverview
    {
        public List<BatteryDeviceDto> Batteries { get; } = [];

        public List<BatteryDeviceDto> Read() => [.. Batteries.Select(battery => new BatteryDeviceDto
        {
            EntityId = battery.EntityId, Name = battery.Name, Percent = battery.Percent, Area = battery.Area,
            DeviceEntityIds = [.. battery.DeviceEntityIds]
        })];
    }

    [TestMethod]
    public async Task BoilerThermostatMode_DoesNotPressTheFingerbot()
    {
        BoilerTestContext context = BoilerTestContext.Create(BoilerControlPath.BoilerThermostat);

        await context.Service.EvaluateAllSchedules("unit test");

        context.BoilerFingerbot.ToggleCount.ShouldBe(0, "the boiler's button is dead in boiler thermostat mode, so no press should be attempted");
        context.Verification.DidNotReceiveWithAnyArgs().NotifyBoilerToggled(default, default!, default);
    }

    [TestMethod]
    public async Task BoilerThermostatMode_StillControlsRoomsNormally()
    {
        BoilerTestContext context = BoilerTestContext.Create(BoilerControlPath.BoilerThermostat);

        await context.Service.EvaluateAllSchedules("unit test");

        // Only the boiler actuator is gated - valves and heater plugs carry on.
        context.LivingRoomThermostat.TargetTemperature.GetValueOrDefault().ShouldBeGreaterThan(context.LivingRoomThermostat.CurrentTemperature.GetValueOrDefault());
        context.Bedroom2Thermostat.TargetTemperature.GetValueOrDefault().ShouldBeGreaterThan(context.Bedroom2Thermostat.CurrentTemperature.GetValueOrDefault());
    }

    [TestMethod]
    public async Task BoilerThermostatMode_DoesNotRunBoilerVerification()
    {
        BoilerTestContext context = BoilerTestContext.Create(BoilerControlPath.BoilerThermostat);

        await context.Service.EvaluateAllSchedules("unit test");

        await context.Verification.DidNotReceive().CheckAsync();
        context.Verification.DidNotReceiveWithAnyArgs().NotifyValveStartedHeating(default!, default!);
        context.Verification.DidNotReceiveWithAnyArgs().NotifyValveStopped(default!);
    }

    [TestMethod]
    public async Task BoilerThermostatMode_DiscardsAnyPendingVerification()
    {
        BoilerTestContext context = BoilerTestContext.Create(BoilerControlPath.BoilerThermostat);

        await context.Service.EvaluateAllSchedules("unit test");

        // Anything left pending from fingerbot mode must go, or it could resurface as a
        // corrective press long after the switch at the boiler was flipped.
        context.Verification.Received(1).Reset();
    }

    [TestMethod]
    public async Task BoilerThermostatMode_DoesNotIssueTheCorrectivePress()
    {
        BoilerTestContext context = BoilerTestContext.Create(BoilerControlPath.BoilerThermostat, roomsCallForHeat: false);
        context.Verification.GetVerifiedBoilerState().Returns(true);

        await context.Service.EvaluateAllSchedules("unit test");

        context.BoilerFingerbot.ToggleCount.ShouldBe(0, "a mismatch reported in boiler thermostat mode cannot be corrected by a press");
        context.Verification.DidNotReceive().GetVerifiedBoilerState();
    }

    [TestMethod]
    public async Task BoilerThermostatMode_LogsOnlyOnceAcrossRepeatedEvaluations()
    {
        BoilerTestContext context = BoilerTestContext.Create(BoilerControlPath.BoilerThermostat);

        await context.Service.EvaluateAllSchedules("first evaluation");
        await context.Service.EvaluateAllSchedules("second evaluation");
        await context.Service.EvaluateAllSchedules("third evaluation");

        context.Logger.CountMessagesContaining(DisabledLogFragment).ShouldBe(1, "the mode is logged per transition, not per evaluation cycle");
    }

    [TestMethod]
    public async Task FingerbotMode_PressesTheFingerbotWhenRoomsCallForHeat()
    {
        BoilerTestContext context = BoilerTestContext.Create(BoilerControlPath.Fingerbot);

        await context.Service.EvaluateAllSchedules("unit test");

        context.BoilerFingerbot.ToggleCount.ShouldBe(1);
        context.Verification.Received(1).NotifyBoilerToggled(true, Arg.Any<List<(string RoomName, ICustomClimateControlEntity Valve)>>(), Arg.Any<DateTimeOffset?>());
        await context.Verification.Received(1).CheckAsync();
        context.Verification.DidNotReceive().Reset();
    }

    [TestMethod]
    public async Task FingerbotMode_DoesNotPressTheFingerbotWhenNoRoomCallsForHeat()
    {
        BoilerTestContext context = BoilerTestContext.Create(BoilerControlPath.Fingerbot, roomsCallForHeat: false);

        await context.Service.EvaluateAllSchedules("unit test");

        context.BoilerFingerbot.ToggleCount.ShouldBe(0);
        context.Verification.DidNotReceiveWithAnyArgs().NotifyBoilerToggled(default, default!, default);
    }

    [TestMethod]
    public async Task FingerbotMode_TurnsOnWhenTheCombinedDeficitReachesThreeDegrees()
    {
        // Living room 2 degrees short plus bedroom 2 one degree short: neither would do it alone.
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 17, bedroom2Target: 16);

        await context.Service.EvaluateAllSchedules("unit test");

        context.BoilerFingerbot.ToggleCount.ShouldBe(1);
        context.Logger.CountMessagesContaining("Boiler turned ON").ShouldBe(1);
    }

    [TestMethod]
    public async Task FingerbotMode_StaysOffWhenTheCombinedDeficitIsUnderThreeDegrees()
    {
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 17, bedroom2Target: 15.8);

        await context.Service.EvaluateAllSchedules("unit test");

        context.BoilerFingerbot.ToggleCount.ShouldBe(0, "2.8 degrees in total is under the threshold");
    }

    [TestMethod]
    public async Task FingerbotMode_OneRoomThreeDegreesShortIsEnough()
    {
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 18, bedroom2Target: 15);

        await context.Service.EvaluateAllSchedules("unit test");

        context.BoilerFingerbot.ToggleCount.ShouldBe(1, "the threshold is inclusive");
    }

    [TestMethod]
    public async Task FingerbotMode_ARoomWhoseValveItsRulesHoldOffAddsNoDemand()
    {
        // The living room alone is 3 degrees short, enough to start the boiler, but its valve's rules
        // need someone in the den and nobody is, so the valve cannot take heat and the room adds nothing.
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 18, bedroom2Target: 15);
        context.DeviceSettings.Settings.Heating!.DeviceRules = new()
        {
            [context.LivingRoomThermostat.EntityId] = new DeviceRulesDto { Presence = new PresenceRuleDto { Sources = ["den"] } }
        };

        await context.Service.EvaluateAllSchedules("unit test");

        context.BoilerFingerbot.ToggleCount.ShouldBe(0);
        context.LivingRoomThermostat.TargetTemperature.ShouldBe(10, "the held-off valve is left closed");
    }

    [TestMethod]
    public async Task FingerbotMode_ARoomThatIsTooHotIsDisregardedNotSubtracted()
    {
        // Living room is 5 degrees over target. If that counted as -5 it would cancel the bedroom's
        // 3-degree deficit and the boiler would stay off.
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 10, bedroom2Target: 18);

        await context.Service.EvaluateAllSchedules("unit test");

        context.BoilerFingerbot.ToggleCount.ShouldBe(1);
    }

    [TestMethod]
    public async Task FingerbotMode_StaysOnUntilEveryRoomIsAtItsTarget()
    {
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 18, bedroom2Target: 15);

        await context.Service.EvaluateAllSchedules("cold");
        context.BoilerFingerbot.ToggleCount.ShouldBe(1);

        // Down to a 1 degree deficit: well under the 3 degrees it took to start, but not zero.
        context.LivingRoomTemperature.State = 17;
        await context.Service.EvaluateAllSchedules("warming");
        context.BoilerFingerbot.ToggleCount.ShouldBe(1, "the boiler only turns off when the deficit is gone");

        context.LivingRoomTemperature.State = 18;
        await context.Service.EvaluateAllSchedules("at target");
        context.BoilerFingerbot.ToggleCount.ShouldBe(2, "a deficit of zero turns the boiler off");
        context.Logger.CountMessagesContaining("Boiler turned OFF").ShouldBe(1);
    }

    // TRV battery (owner, 2026-10-19): a valve opened with the boiler off heats nothing.

    [TestMethod]
    public async Task FingerbotMode_ValvesStayShutWhileTheBoilerStaysOff()
    {
        // 2.8 degrees short in total: under the threshold, so no boiler and no hot water.
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 17, bedroom2Target: 15.8);

        await context.Service.EvaluateAllSchedules("rooms short, under the threshold");
        await context.Service.EvaluateAllSchedules("next cycle");

        context.BoilerFingerbot.ToggleCount.ShouldBe(0);
        context.ValvesAreShut().ShouldBeTrue("opening a valve the boiler is not feeding only spends its battery");
        context.LivingRoomThermostat.SetCount.ShouldBe(0, "the valves were not touched at all");
        context.Bedroom2Thermostat.SetCount.ShouldBe(0);
    }

    [TestMethod]
    public async Task FingerbotMode_ValvesOpenInThePassThatTurnsTheBoilerOn()
    {
        BoilerTestContext context = BoilerTestContext.Create(BoilerControlPath.Fingerbot);

        await context.Service.EvaluateAllSchedules("cold");

        context.BoilerFingerbot.ToggleCount.ShouldBe(1);
        context.ValvesAreOpen().ShouldBeTrue("they open straight after the press, not on the next cycle");
    }

    [TestMethod]
    public async Task BoilerThermostatMode_OpensValvesWithoutWaitingForTheBoiler()
    {
        // The boiler thermostat may be running the boiler, and the app cannot tell, so valves follow their
        // rooms as they always have.
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.BoilerThermostat, livingRoomTarget: 17, bedroom2Target: 15.8);

        await context.Service.EvaluateAllSchedules("rooms short");

        context.ValvesAreOpen().ShouldBeTrue();
    }

    [TestMethod]
    public async Task WaitingForTheBoiler_NeverShutsAValveThatIsAlreadyOpen()
    {
        // The valves opened under the boiler thermostat. Switching to the fingerbot with the rooms 2.8
        // degrees short leaves the boiler off, but shutting the valves for that would be a wasted move.
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.BoilerThermostat, livingRoomTarget: 17, bedroom2Target: 15.8);
        await context.Service.EvaluateAllSchedules("on the boiler thermostat");
        context.ValvesAreOpen().ShouldBeTrue();
        int setsBefore = context.LivingRoomThermostat.SetCount + context.Bedroom2Thermostat.SetCount;

        context.DeviceSettings.SetBoilerControlPath(BoilerControlPath.Fingerbot);
        await context.Service.EvaluateAllSchedules("switched to the fingerbot");
        await context.Service.EvaluateAllSchedules("next cycle");

        context.BoilerFingerbot.ToggleCount.ShouldBe(0);
        context.ValvesAreOpen().ShouldBeTrue();
        (context.LivingRoomThermostat.SetCount + context.Bedroom2Thermostat.SetCount).ShouldBe(setsBefore);
    }

    [TestMethod]
    public async Task ARestartWithTheBoilerBelievedOn_DoesNotShutAndReopenTheValves()
    {
        InMemoryBoilerStatePersistence boilerState = new() { Saved = new BoilerBelief(true, DateTimeOffset.UtcNow.AddMinutes(-10)) };
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 20, bedroom2Target: 20, boilerState: boilerState);

        await context.Service.EvaluateAllSchedules("app startup");

        context.BoilerFingerbot.ToggleCount.ShouldBe(0, "the boiler is already on");
        context.ValvesAreOpen().ShouldBeTrue();
        context.LivingRoomThermostat.SetCount.ShouldBe(1, "opened once, from the saved belief, with no shut-then-open on the way");
    }

    // The boiler goes off once no room is calling, and valves are only moved when there may be hot water
    // behind them (owner, 2026-10-19).

    [TestMethod]
    public async Task TheBoilerTurnsOff_WhenNoRoomIsCalling_EvenWithOneAFractionShort()
    {
        // As on 2026-10-19: a boost ends and Bedroom 3 is left 0.1 degrees short. Too little to open its
        // valve, so it must not hold the boiler on with every valve shut.
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 18, bedroom2Target: 15.1);
        await context.Service.EvaluateAllSchedules("living room 3 degrees short");
        context.BoilerFingerbot.ToggleCount.ShouldBe(1);

        context.LivingRoomTemperature.State = 18;
        await context.Service.EvaluateAllSchedules("living room at target, bedroom 2 still 0.1 short");

        context.BoilerFingerbot.ToggleCount.ShouldBe(2, "bedroom 2 never started calling, so nothing needs the boiler");
        context.Logger.CountMessagesContaining("Boiler turned OFF").ShouldBe(1);
    }

    [TestMethod]
    public async Task ARoomThatStartedCalling_KeepsTheBoilerOnUntilItReachesItsTarget()
    {
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 18, bedroom2Target: 15);
        await context.Service.EvaluateAllSchedules("cold");

        context.LivingRoomTemperature.State = 17.9;
        await context.Service.EvaluateAllSchedules("0.1 short, still calling");

        context.BoilerFingerbot.ToggleCount.ShouldBe(1, "a room that is warming carries on until it gets there");
    }

    [TestMethod]
    public async Task TurningTheBoilerOff_LeavesTheValvesWhereTheyAre()
    {
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 18, bedroom2Target: 15);
        await context.Service.EvaluateAllSchedules("cold");
        context.LivingRoomThermostat.TargetTemperature.GetValueOrDefault().ShouldBeGreaterThan(context.LivingRoomThermostat.CurrentTemperature.GetValueOrDefault());
        int setsWhileOn = context.LivingRoomThermostat.SetCount;

        // Warm enough that, with the boiler on, its valve would be shut.
        context.LivingRoomTemperature.State = 18.5;
        await context.Service.EvaluateAllSchedules("warm, boiler off");
        await context.Service.EvaluateAllSchedules("next cycle");

        context.BoilerFingerbot.ToggleCount.ShouldBe(2);
        context.LivingRoomThermostat.SetCount.ShouldBe(setsWhileOn, "with the boiler off, shutting the valve would spend its battery for nothing");
    }

    [TestMethod]
    public async Task TurningTheBoilerOn_BringsAValveLeftOpenInAWarmRoomIntoLine()
    {
        // The living room is warm but its valve was left open when the boiler last went off.
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 10, bedroom2Target: 20);
        context.LivingRoomThermostat.SetTargetTemperature(20);
        context.Bedroom2Temperature.State = 25;
        await context.Service.EvaluateAllSchedules("nothing calling");
        context.BoilerFingerbot.ToggleCount.ShouldBe(0);
        context.LivingRoomThermostat.TargetTemperature.ShouldBe(20, "left alone while the boiler is off");

        context.Bedroom2Temperature.State = 15;
        await context.Service.EvaluateAllSchedules("bedroom 2 calls for heat");

        context.BoilerFingerbot.ToggleCount.ShouldBe(1);
        context.LivingRoomThermostat.TargetTemperature.GetValueOrDefault().ShouldBeLessThan(context.LivingRoomThermostat.CurrentTemperature.GetValueOrDefault(),
            "with hot water flowing, the warm room's valve is shut in the same pass");
        context.Bedroom2Thermostat.TargetTemperature.GetValueOrDefault().ShouldBeGreaterThan(context.Bedroom2Thermostat.CurrentTemperature.GetValueOrDefault());
    }

    [TestMethod]
    public async Task AFailedTurnOffCheck_ShutsTheWarmRoomsValves()
    {
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 20, bedroom2Target: 20, realVerification: true);
        await context.Service.EvaluateAllSchedules("cold, boiler on");
        context.BoilerFingerbot.ToggleCount.ShouldBe(1);

        context.AdvanceMinutes(BoilerVerificationService.RadiatorSettledMinutes + 1);
        context.LivingRoomTemperature.State = 25;
        context.Bedroom2Temperature.State = 25;
        await context.Service.EvaluateAllSchedules("warm, boiler off");
        context.BoilerFingerbot.ToggleCount.ShouldBe(2);
        context.ValvesAreOpen().ShouldBeTrue("the boiler is believed off, so the valves are left alone");

        // The radiators dip, then heat up again with their valves still open: the turn-off did not reach the boiler.
        context.AdvanceMinutes(BoilerVerificationService.TurnOffSettlingMinutes + 1);
        context.SetRadiatorValveReadings(14.5);
        await context.Service.EvaluateAllSchedules("settled");
        context.AdvanceMinutes(10);
        context.SetRadiatorValveReadings(16);
        await context.Service.EvaluateAllSchedules("radiators hot again");
        context.ValvesAreOpen().ShouldBeTrue();

        await context.Service.EvaluateAllSchedules("next cycle");

        context.BoilerFingerbot.ToggleCount.ShouldBe(3, "the mismatch is corrected with a press");
        context.ValvesAreShut().ShouldBeTrue("evidence that the boiler may still be running is the reason to shut the warm rooms' valves");
    }

    // A room someone is in starts the boiler whatever the total deficit, like a boost (owner, 2026-10-19: the den's
    // TV was on and its 21 °C "in use" track active, but 1 °C short on its own never started the boiler).

    [TestMethod]
    public async Task AnOccupiedRoom_StartsTheBoilerUnderTheThreshold()
    {
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 15, bedroom2Target: 15);
        context.GiveLivingRoomAnInUseTrack(16);
        context.Presence.SetRoomValue("Living room", true);

        await context.Service.EvaluateAllSchedules("TV on");

        context.BoilerFingerbot.ToggleCount.ShouldBe(1, "one degree short is under 3, but someone is in the room");
        context.Logger.CountMessagesContaining("in use: Living room").ShouldBe(1);
        context.ValvesAreShut().ShouldBeFalse();
    }

    [TestMethod]
    public async Task TheSameRoomEmpty_WaitsForTheThreshold()
    {
        // Nobody in: the "not in use" track applies, the room is at its target and nothing calls.
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 15, bedroom2Target: 15);
        context.GiveLivingRoomAnInUseTrack(16);
        context.Presence.SetRoomValue("Living room", false);

        await context.Service.EvaluateAllSchedules("TV off");

        context.BoilerFingerbot.ToggleCount.ShouldBe(0);
    }

    [TestMethod]
    public async Task AnEmptyRoomShort_OfItsOrdinaryTarget_StillWaitsForTheThreshold()
    {
        // 1 degree short on an ordinary track (no presence condition): no one is known to be there.
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 16, bedroom2Target: 15);

        await context.Service.EvaluateAllSchedules("schedule");

        context.BoilerFingerbot.ToggleCount.ShouldBe(0);
    }

    [TestMethod]
    public async Task AnOccupiedRoomAlmostAtTarget_DoesNotStartTheBoiler()
    {
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 15, bedroom2Target: 15);
        context.GiveLivingRoomAnInUseTrack(15.1);
        context.Presence.SetRoomValue("Living room", true);

        await context.Service.EvaluateAllSchedules("TV on, room nearly warm");

        context.BoilerFingerbot.ToggleCount.ShouldBe(0, "0.1 degrees short is inside the band where its valve would not open");
    }

    [TestMethod]
    [DataRow(45, 1, DisplayName = "45 minutes' pre-heat: the 12:30 track has started for this valve")]
    [DataRow(15, 0, DisplayName = "15 minutes' pre-heat: not yet")]
    public async Task AValvesPreHeat_CallsForTheBoilerEarly(int preHeatMinutes, int presses)
    {
        // At 12:00 the living room is 15 °C on a 15 °C track, with 21 °C from 12:30: 6 degrees short once its
        // valve starts pre-heating.
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 15, bedroom2Target: 15);
        context.Rooms[0].ScheduleTracks =
        [
            new HeatingScheduleTrack { Id = 1, TargetTime = new TimeOnly(0, 0), Temperature = 15 },
            new HeatingScheduleTrack { Id = 2, TargetTime = new TimeOnly(12, 30), Temperature = 21 }
        ];
        context.DeviceSettings.Settings.Heating!.DeviceRules = new()
        {
            [context.LivingRoomThermostat.EntityId] = new DeviceRulesDto { PreHeatMinutes = preHeatMinutes }
        };

        await context.Service.EvaluateAllSchedules("unit test");

        context.BoilerFingerbot.ToggleCount.ShouldBe(presses);
    }

    // The UI shows what the app believes the boiler is doing, in fingerbot mode (owner, 2026-10-19).

    [TestMethod]
    public async Task TheBoilersBelief_IsUploadedWithTheRoomStates()
    {
        BoilerTestContext context = BoilerTestContext.Create(BoilerControlPath.Fingerbot);

        await context.Service.EvaluateAllSchedules("cold");

        BoilerStateDto boiler = context.LastUploadedBoiler()!;
        boiler.AppControlsBoiler.ShouldBeTrue();
        boiler.BelievedOn.ShouldBeTrue();
        boiler.Since.ShouldNotBeNull();
    }

    [TestMethod]
    public async Task TheValveExercise_ShowsTheBoilerOnStraightAway()
    {
        // Evaluations wait for the exercise, so without its own upload the UI would show the boiler off throughout.
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 10, bedroom2Target: 10);
        await context.Service.EvaluateAllSchedules("warm, boiler off");
        context.LastUploadedBoiler()!.BelievedOn.ShouldBeFalse();

        await context.Service.StartValveExerciseAsync("unit test");

        context.LastUploadedBoiler()!.BelievedOn.ShouldBeTrue();
    }

    [TestMethod]
    public async Task OnTheBoilerThermostat_TheAppDoesNotClaimTheBoiler()
    {
        BoilerTestContext context = BoilerTestContext.Create(BoilerControlPath.BoilerThermostat);

        await context.Service.EvaluateAllSchedules("cold");

        BoilerStateDto boiler = context.LastUploadedBoiler()!;
        boiler.AppControlsBoiler.ShouldBeFalse();
        boiler.BelievedOn.ShouldBeFalse();
        boiler.Since.ShouldBeNull();
    }

    // The monthly valve exercise (owner, 2026-10-19, after a TRV seized from not moving for months).

    [TestMethod]
    public async Task Exercise_FlipsEveryValve_RunsTheBoiler_ThenPutsItAllBack()
    {
        // Warm rooms, so nothing needs heat; the living room's valve happens to be open and bedroom 2's shut.
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 10, bedroom2Target: 10);
        context.LivingRoomThermostat.SetTargetTemperature(20);

        (await context.Service.StartValveExerciseAsync("unit test")).ShouldBeTrue();

        context.BoilerFingerbot.ToggleCount.ShouldBe(1, "the boiler is run for the exercise");
        context.LivingRoomThermostat.TargetTemperature.ShouldBe(10, "the open valve is shut");
        context.Bedroom2Thermostat.TargetTemperature.ShouldBe(20, "the shut valve is opened");

        await context.Service.RestoreValvesAfterExerciseAsync();
        context.LivingRoomThermostat.TargetTemperature.ShouldBe(20, "put back to its exact setpoint");
        context.Bedroom2Thermostat.TargetTemperature.ShouldBe(10);

        await context.Service.EndValveExerciseAsync();
        context.BoilerFingerbot.ToggleCount.ShouldBe(2, "nothing needs heat, so the boiler goes off again");
        context.Logger.CountMessagesContaining("Boiler turned OFF after the valve exercise").ShouldBe(1);
    }

    [TestMethod]
    public async Task Exercise_IsSkippedWhenTheBoilerIsAlreadyOn()
    {
        // The heating is evidently working, so that month's exercise is skipped.
        InMemoryBoilerStatePersistence boilerState = new() { Saved = new BoilerBelief(true, DateTimeOffset.UtcNow.AddMinutes(-20)) };
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 20, bedroom2Target: 20, boilerState: boilerState);

        (await context.Service.StartValveExerciseAsync("unit test")).ShouldBeFalse();

        context.BoilerFingerbot.ToggleCount.ShouldBe(0);
        context.LivingRoomThermostat.SetCount.ShouldBe(0, "no valve is moved either");
        context.Logger.CountMessagesContaining("Valve exercise skipped").ShouldBe(1);
    }

    [TestMethod]
    public async Task DuringTheExercise_NormalEvaluationsWait()
    {
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 10, bedroom2Target: 10);
        await context.Service.StartValveExerciseAsync("unit test");
        int sets = context.LivingRoomThermostat.SetCount + context.Bedroom2Thermostat.SetCount;

        // Warm rooms with the boiler on would normally shut the opened valves and turn the boiler off.
        await context.Service.EvaluateAllSchedules("temperature changed mid-exercise");

        (context.LivingRoomThermostat.SetCount + context.Bedroom2Thermostat.SetCount).ShouldBe(sets);
        context.BoilerFingerbot.ToggleCount.ShouldBe(1);
    }

    [TestMethod]
    public async Task Exercise_LeavesTheBoilerOnWhenTheRoomsNeedIt()
    {
        // Cold rooms that had not yet been evaluated: once the exercise ends they want the boiler anyway.
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 20, bedroom2Target: 20);
        await context.Service.StartValveExerciseAsync("unit test");
        await context.Service.RestoreValvesAfterExerciseAsync();

        await context.Service.EndValveExerciseAsync();

        context.BoilerFingerbot.ToggleCount.ShouldBe(1, "no off-then-on again: it stays on");
        context.Logger.CountMessagesContaining("Boiler left ON after the valve exercise").ShouldBe(1);
        context.ValvesAreOpen().ShouldBeTrue("and normal control opens the cold rooms' valves");
    }

    [TestMethod]
    public async Task Exercise_DoesNotArmTheBoilerVerification()
    {
        // A 5-minute burn would fail the 15-minute turn-on check, which would "correct" it with another press.
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 10, bedroom2Target: 10);

        await context.Service.StartValveExerciseAsync("unit test");

        context.Verification.Received(1).Reset();
        context.Verification.DidNotReceiveWithAnyArgs().NotifyBoilerToggled(default, default!, default);
    }

    [TestMethod]
    public async Task OnTheBoilerThermostat_OnlyTheValvesAreExercised()
    {
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.BoilerThermostat, livingRoomTarget: 10, bedroom2Target: 10);

        (await context.Service.StartValveExerciseAsync("unit test")).ShouldBeTrue();

        context.BoilerFingerbot.ToggleCount.ShouldBe(0, "the app cannot drive the boiler in that mode");
        context.ValvesAreOpen().ShouldBeTrue();

        await context.Service.RestoreValvesAfterExerciseAsync();
        await context.Service.EndValveExerciseAsync();
        context.ValvesAreShut().ShouldBeTrue();
        context.BoilerFingerbot.ToggleCount.ShouldBe(0);
    }

    // A boost starts the boiler whatever the total deficit (owner, 2026-10-19).

    [TestMethod]
    public async Task ABoost_StartsTheBoilerUnderTheThreshold()
    {
        // Both rooms are at their scheduled 15 degrees; the living room is boosted to 16.
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 15, bedroom2Target: 15);
        context.BoostLivingRoom(16);

        await context.Service.EvaluateAllSchedules("boost started");

        context.BoilerFingerbot.ToggleCount.ShouldBe(1, "one degree short is far under 3, but a boost overrides the threshold");
        context.Logger.CountMessagesContaining("boost running in Living room").ShouldBe(1);
        context.LivingRoomThermostat.TargetTemperature.GetValueOrDefault().ShouldBeGreaterThan(context.LivingRoomThermostat.CurrentTemperature.GetValueOrDefault());
    }

    [TestMethod]
    public async Task ABoostTheRoomHasAlmostReached_DoesNotStartTheBoiler()
    {
        // 0.1 degrees short: inside the 0.2 degree band in which the valve would not open either.
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 15, bedroom2Target: 15);
        context.BoostLivingRoom(15.1);

        await context.Service.EvaluateAllSchedules("boost started");

        context.BoilerFingerbot.ToggleCount.ShouldBe(0);
    }

    [TestMethod]
    public async Task ABoostInARoomWhoseValveItsRulesHoldOff_DoesNotStartTheBoiler()
    {
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 15, bedroom2Target: 15);
        context.BoostLivingRoom(20);
        context.DeviceSettings.Settings.Heating!.DeviceRules = new()
        {
            [context.LivingRoomThermostat.EntityId] = new DeviceRulesDto { Presence = new PresenceRuleDto { Sources = ["den"] } }
        };

        await context.Service.EvaluateAllSchedules("boost started, nobody in the den");

        context.BoilerFingerbot.ToggleCount.ShouldBe(0, "a boost does not bypass device rules, and a valve that may not open takes no heat");
    }

    [TestMethod]
    public async Task ABoostedRoomReachingItsTarget_TurnsTheBoilerOff()
    {
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 15, bedroom2Target: 15);
        context.BoostLivingRoom(16);
        await context.Service.EvaluateAllSchedules("boost started");
        context.BoilerFingerbot.ToggleCount.ShouldBe(1);

        context.LivingRoomTemperature.State = 16;
        await context.Service.EvaluateAllSchedules("boost target reached");

        context.BoilerFingerbot.ToggleCount.ShouldBe(2, "with no room short the deficit is zero, so the boiler goes off as usual");
    }

    // The upstairs bathroom's towel rail has no valve: it is hot whenever the boiler runs, so the only way to heat the
    // bathroom is to run the boiler, and it calls for it like a valve room (owner, 2026-10-20).

    [TestMethod]
    public async Task TheBathroomsShortfall_CountsTowardsTheThreshold()
    {
        // Living room 2 degrees short, bathroom 1: only the two together reach 3.
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 17, bedroom2Target: 15, bathroomTarget: 16);

        await context.Service.EvaluateAllSchedules("rooms short");

        context.BoilerFingerbot.ToggleCount.ShouldBe(1);
        context.Logger.CountMessagesContaining("Upstairs bathroom (1.0°C)").ShouldBe(1);
    }

    [TestMethod]
    public async Task TheBathroomAlone_StartsTheBoilerAtTheThreshold()
    {
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 10, bedroom2Target: 10, bathroomTarget: 18);

        await context.Service.EvaluateAllSchedules("bathroom cold");

        context.BoilerFingerbot.ToggleCount.ShouldBe(1);
        context.ValvesAreShut().ShouldBeTrue("the warm rooms' valves stay shut: only the bathroom wants the heat");
    }

    [TestMethod]
    public async Task ABathroomBoost_StartsTheBoilerUnderTheThreshold()
    {
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 10, bedroom2Target: 10, bathroomTarget: 15);
        context.BoostBathroom(17);

        await context.Service.EvaluateAllSchedules("bathroom boosted");

        context.BoilerFingerbot.ToggleCount.ShouldBe(1);
        context.Logger.CountMessagesContaining("boost running in Upstairs bathroom").ShouldBe(1);
        context.LastUploadedBathroom().TargetTemperature.ShouldBe(17);
    }

    [TestMethod]
    public async Task TheBathroomReachingItsTarget_TurnsTheBoilerOff()
    {
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 10, bedroom2Target: 10, bathroomTarget: 18);
        await context.Service.EvaluateAllSchedules("bathroom cold");
        context.BoilerFingerbot.ToggleCount.ShouldBe(1);

        context.BathroomTemperature.State = 17.5;
        await context.Service.EvaluateAllSchedules("warming");
        context.BoilerFingerbot.ToggleCount.ShouldBe(1, "still calling until it reaches its target");

        context.BathroomTemperature.State = 18;
        await context.Service.EvaluateAllSchedules("at target");
        context.BoilerFingerbot.ToggleCount.ShouldBe(2);
    }

    [TestMethod]
    public async Task TheTowelRail_IsShownHeatingWhileTheBoilerIsOn()
    {
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 20, bedroom2Target: 20, bathroomTarget: 10);

        await context.Service.EvaluateAllSchedules("the other rooms are cold");

        context.BoilerFingerbot.ToggleCount.ShouldBe(1);
        RoomState bathroom = context.LastUploadedBathroom();
        bathroom.HeatingActive.ShouldBeTrue("the rail has no valve, so it is hot whenever the boiler runs, wanted or not");
        bathroom.Devices.Single(device => device.Role == RoomDeviceRoles.TowelRail).State.ShouldBe("on");
    }

    [TestMethod]
    public async Task OnTheBoilerThermostat_TheBathroomIsNotClaimedToBeHeating()
    {
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.BoilerThermostat, livingRoomTarget: 10, bedroom2Target: 10, bathroomTarget: 18);

        await context.Service.EvaluateAllSchedules("bathroom cold");

        context.BoilerFingerbot.ToggleCount.ShouldBe(0);
        RoomState bathroom = context.LastUploadedBathroom();
        bathroom.TargetTemperature.ShouldBe(18);
        bathroom.HeatingActive.ShouldBeFalse("nothing can tell whether the boiler thermostat is running the boiler");
        bathroom.Devices.Single(device => device.Role == RoomDeviceRoles.TowelRail).State.ShouldBeNull();
    }

    [TestMethod]
    public async Task FingerbotMode_IssuesTheCorrectivePressOnAVerificationMismatch()
    {
        BoilerTestContext context = BoilerTestContext.Create(BoilerControlPath.Fingerbot, roomsCallForHeat: false);
        context.Verification.GetVerifiedBoilerState().Returns(true);

        await context.Service.EvaluateAllSchedules("unit test");

        // Unchanged from today's behaviour: with the button live, correcting a mismatch is real.
        context.BoilerFingerbot.ToggleCount.ShouldBe(1);
    }

    [TestMethod]
    public async Task FlippingTheModeToFingerbot_StartsControllingTheBoiler()
    {
        BoilerTestContext context = BoilerTestContext.Create(BoilerControlPath.BoilerThermostat);

        await context.Service.EvaluateAllSchedules("while on the boiler thermostat");
        context.BoilerFingerbot.ToggleCount.ShouldBe(0);

        context.DeviceSettings.SetBoilerControlPath(BoilerControlPath.Fingerbot);
        await context.Service.EvaluateAllSchedules("after the switch at the boiler was flipped");

        context.BoilerFingerbot.ToggleCount.ShouldBe(1);
        context.Logger.CountMessagesContaining(EnabledLogFragment).ShouldBe(1);
    }

    [TestMethod]
    public async Task UnsetSettings_LeaveTheBoilerAlone()
    {
        // Out of the box - no device settings saved at all - the app must not touch the boiler.
        BoilerTestContext context = BoilerTestContext.Create(boilerControlPath: null);

        await context.Service.EvaluateAllSchedules("unit test");

        context.BoilerFingerbot.ToggleCount.ShouldBe(0);
        context.Logger.CountMessagesContaining(DisabledLogFragment).ShouldBe(1);
    }

    [TestMethod]
    public async Task UnreadableSettings_LeaveTheBoilerAlone()
    {
        BoilerTestContext context = BoilerTestContext.Create(BoilerControlPath.Fingerbot);
        context.DeviceSettings.ThrowOnGet = true;

        await context.Service.EvaluateAllSchedules("unit test");

        context.BoilerFingerbot.ToggleCount.ShouldBe(0, "a failure to read the mode must not be read as permission to press");
    }

    // Finding 1: the turn-on verification was armed after the valves had already been reported as
    // heating, which wiped the "shut since" time it needs, so in the running app it never armed.

    [TestMethod]
    public async Task TurnOnVerification_CatchesAPressTheBoilerIgnored_WhenTheValvesWereShut()
    {
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 20, bedroom2Target: 20, realVerification: true);
        context.LivingRoomTemperature.State = 25;
        context.Bedroom2Temperature.State = 25;
        await context.Service.EvaluateAllSchedules("warm, valves shut");

        context.AdvanceMinutes(BoilerVerificationService.RadiatorSettledMinutes + 1);
        context.LivingRoomTemperature.State = 15;
        context.Bedroom2Temperature.State = 15;
        await context.Service.EvaluateAllSchedules("cold, valves open");
        context.BoilerFingerbot.ToggleCount.ShouldBe(1, "the rooms called for heat");

        // The radiators never warm: the press did not reach the boiler.
        context.AdvanceMinutes(BoilerVerificationService.TurnOnDeadlineMinutes + 1);
        await context.Service.EvaluateAllSchedules("verification due");
        await context.Service.EvaluateAllSchedules("next cycle");

        context.BoilerFingerbot.ToggleCount.ShouldBe(2, "a turn-on the radiators never felt is corrected with a second press");
        context.Logger.CountMessagesContaining("state mismatch").ShouldBe(1);
    }

    [TestMethod]
    public async Task TurnOnVerification_WatchesTheValvesThatOpenWithThePress()
    {
        // 2.8 degrees short between them: the boiler stays off, so the valves stay shut.
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 17, bedroom2Target: 15.8, realVerification: true);
        await context.Service.EvaluateAllSchedules("rooms short, boiler off");
        context.BoilerFingerbot.ToggleCount.ShouldBe(0);
        context.ValvesAreShut().ShouldBeTrue();

        // Half an hour on, bedroom 2 has slipped and the total reaches 3 degrees. The valves open with the
        // press, onto radiators that have been cold all along, so they are the ones worth watching.
        context.AdvanceMinutes(BoilerVerificationService.RadiatorSettledMinutes + 1);
        context.Bedroom2Temperature.State = 14.5;
        await context.Service.EvaluateAllSchedules("deficit reaches 3 degrees");
        context.BoilerFingerbot.ToggleCount.ShouldBe(1);
        context.ValvesAreOpen().ShouldBeTrue();

        context.AdvanceMinutes(BoilerVerificationService.TurnOnDeadlineMinutes + 1);
        await context.Service.EvaluateAllSchedules("verification due");
        await context.Service.EvaluateAllSchedules("next cycle");

        context.BoilerFingerbot.ToggleCount.ShouldBe(2);
    }

    [TestMethod]
    public async Task TurnOnVerification_LeavesAPressAloneWhenTheRadiatorsWarm()
    {
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 20, bedroom2Target: 20, realVerification: true);
        context.LivingRoomTemperature.State = 25;
        context.Bedroom2Temperature.State = 25;
        await context.Service.EvaluateAllSchedules("warm, valves shut");

        context.AdvanceMinutes(BoilerVerificationService.RadiatorSettledMinutes + 1);
        context.LivingRoomTemperature.State = 15;
        context.Bedroom2Temperature.State = 15;
        await context.Service.EvaluateAllSchedules("cold, valves open");

        context.AdvanceMinutes(BoilerVerificationService.TurnOnDeadlineMinutes + 1);
        context.SetRadiatorValveReadings(15 + BoilerVerificationService.TurnOnTemperatureRiseThreshold + 0.5);
        await context.Service.EvaluateAllSchedules("verification due");
        await context.Service.EvaluateAllSchedules("next cycle");

        context.BoilerFingerbot.ToggleCount.ShouldBe(1, "the boiler responded, so there is nothing to correct");
        context.Logger.CountMessagesContaining("state mismatch").ShouldBe(0);
    }

    // 2026-10-20 (HOME-ASSISTANT.md gotcha 23): two false turn-on failures each pressed a boiler that was already
    // right, and a false turn-off pass then left the boiler running all night while the app showed it off.

    [TestMethod]
    [DataRow(16, 0.5, 31, 4.8, DisplayName = "14:47, dining: +0.5 at 16 min, then +4.8")]
    [DataRow(15, 0.1, 30, 1.0, DisplayName = "18:05, Bedroom 3: +0.1 at 15 min, first rise at 28")]
    public async Task TurnOnVerification_WaitsForASlowTrv_InsteadOfPressingAgain(int firstCheckMinutes, double firstRise, int laterMinutes, double laterRise)
    {
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 20, bedroom2Target: 20, realVerification: true);
        context.LivingRoomTemperature.State = 25;
        context.Bedroom2Temperature.State = 25;
        await context.Service.EvaluateAllSchedules("warm, valves shut");

        context.AdvanceMinutes(BoilerVerificationService.RadiatorSettledMinutes + 1);
        context.LivingRoomTemperature.State = 15;
        context.Bedroom2Temperature.State = 15;
        await context.Service.EvaluateAllSchedules("cold, boiler on");
        context.BoilerFingerbot.ToggleCount.ShouldBe(1);

        // The boiler fired, but the TRVs are slow to show it.
        context.AdvanceMinutes(firstCheckMinutes);
        context.SetRadiatorValveReadings(15 + firstRise);
        await context.Service.EvaluateAllSchedules("the old 15-minute check");
        await context.Service.EvaluateAllSchedules("next cycle");
        context.AdvanceMinutes(laterMinutes - firstCheckMinutes);
        context.SetRadiatorValveReadings(15 + laterRise);
        await context.Service.EvaluateAllSchedules("the radiators show it");
        context.AdvanceMinutes(BoilerVerificationService.TurnOnDeadlineMinutes);
        await context.Service.EvaluateAllSchedules("past the deadline");
        await context.Service.EvaluateAllSchedules("next cycle");

        context.BoilerFingerbot.ToggleCount.ShouldBe(1, "the boiler was on all along, so a corrective press would have turned it off");
        context.Logger.CountMessagesContaining("state mismatch").ShouldBe(0);
        context.BoilerState.Saved!.BoilerOn.ShouldBeTrue();
    }

    [TestMethod]
    public async Task TurnOnVerification_DoesNotWatchAValveShutInTheSamePass()
    {
        // 18:05:24: the press that turned the boiler on also shut the dining valve (24.5 -> 19 °C), which had been
        // left open in a warm room, and the verification watched it. Here the living room plays the dining room, and
        // both TRVs report their new setpoints late, as HA does.
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 10, bedroom2Target: 20);
        context.LivingRoomThermostat.SetTargetTemperature(20);
        context.Bedroom2Temperature.State = 25;
        await context.Service.EvaluateAllSchedules("nothing calling");
        context.BoilerFingerbot.ToggleCount.ShouldBe(0);

        context.LivingRoomThermostat.ReportsLate = true;
        context.Bedroom2Thermostat.ReportsLate = true;
        context.Bedroom2Temperature.State = 15;
        await context.Service.EvaluateAllSchedules("bedroom 2 calls for heat");

        context.BoilerFingerbot.ToggleCount.ShouldBe(1);
        context.Verification.Received(1).NotifyBoilerToggled(
            true,
            Arg.Is<List<(string RoomName, ICustomClimateControlEntity Valve)>>(valves =>
                valves.Count == 1 && valves[0].Valve == context.Bedroom2Thermostat),
            Arg.Any<DateTimeOffset?>());
    }

    [TestMethod]
    public async Task TurnOffVerification_CatchesABoilerStillRunning_WhenAnOpenValveHeatsUpAgain()
    {
        // 19:02: the turn-off press in fact turned the boiler on. Bedroom 3's valve, opened at 17.5 °C and so set to
        // 22.5, was left open (valves wait while the boiler is believed off) and regulated itself on the hot water.
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 20, bedroom2Target: 20, realVerification: true);
        context.SetRadiatorValveReadings(17.5);
        await context.Service.EvaluateAllSchedules("cold, boiler on");
        context.BoilerFingerbot.ToggleCount.ShouldBe(1);
        context.Bedroom2Thermostat.TargetTemperature.ShouldBe(22.5);

        context.AdvanceMinutes(BoilerVerificationService.RadiatorSettledMinutes + 1);
        context.LivingRoomTemperature.State = 25;
        context.Bedroom2Temperature.State = 25;
        context.SetRadiatorValveReadings(22.0);
        await context.Service.EvaluateAllSchedules("warm, boiler off");
        context.BoilerFingerbot.ToggleCount.ShouldBe(2);
        context.BoilerState.Saved!.BoilerOn.ShouldBeFalse();

        // The TRVs cycle on the running boiler: 22.0, down to 21.5, up to 23.0.
        foreach ((int minutes, double reading) in new[] { (15, 22.0), (16, 21.5), (10, 23.0) })
        {
            context.AdvanceMinutes(minutes);
            context.SetRadiatorValveReadings(reading);
            await context.Service.EvaluateAllSchedules("TRVs cycling");
        }

        context.BoilerFingerbot.ToggleCount.ShouldBe(2, "the check that saw it runs at the end of the pass");
        await context.Service.EvaluateAllSchedules("next cycle");

        context.BoilerFingerbot.ToggleCount.ShouldBe(3, "the boiler was running, so it is pressed off");
        context.Logger.CountMessagesContaining("state mismatch").ShouldBe(1);
        context.BoilerState.Saved!.BoilerOn.ShouldBeFalse("the corrective press put the boiler where the app believed it");
    }

    [TestMethod]
    public async Task TurnOffVerification_LeavesAPressAloneWhenTheRadiatorsCool()
    {
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 20, bedroom2Target: 20, realVerification: true);
        context.SetRadiatorValveReadings(17.5);
        await context.Service.EvaluateAllSchedules("cold, boiler on");

        context.AdvanceMinutes(BoilerVerificationService.RadiatorSettledMinutes + 1);
        context.LivingRoomTemperature.State = 25;
        context.Bedroom2Temperature.State = 25;
        context.SetRadiatorValveReadings(22.0);
        await context.Service.EvaluateAllSchedules("warm, boiler off");

        foreach (double reading in new[] { 21.5, 21.0, 20.0, 19.5, 19.0, 19.0, 18.5 })
        {
            context.AdvanceMinutes(15);
            context.SetRadiatorValveReadings(reading);
            await context.Service.EvaluateAllSchedules("radiators cooling");
        }

        context.BoilerFingerbot.ToggleCount.ShouldBe(2);
        context.Logger.CountMessagesContaining("state mismatch").ShouldBe(0);
    }

    // Finding 2: the belief was reset to OFF by every restart, and presses made outside the app
    // went unnoticed.

    [TestMethod]
    public async Task ARestart_CarriesOnFromTheSavedBelief()
    {
        InMemoryBoilerStatePersistence boilerState = new();
        BoilerTestContext beforeRestart = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 20, bedroom2Target: 20, boilerState: boilerState);
        await beforeRestart.Service.EvaluateAllSchedules("cold");
        beforeRestart.BoilerFingerbot.ToggleCount.ShouldBe(1);
        boilerState.Saved!.BoilerOn.ShouldBeTrue();

        // A new copy of the app (an over-the-air update) starts with every room warm.
        BoilerTestContext afterRestart = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 10, bedroom2Target: 10, boilerState: boilerState);
        await afterRestart.Service.EvaluateAllSchedules("app startup");

        afterRestart.BoilerFingerbot.ToggleCount.ShouldBe(1, "the boiler was left on before the restart, so it has to be turned off");
        afterRestart.Logger.CountMessagesContaining("Carrying on from the saved belief").ShouldBe(1);
        boilerState.Saved!.BoilerOn.ShouldBeFalse();
    }

    [TestMethod]
    public async Task ComingFromTheBoilerThermostat_IgnoresTheSavedBelief()
    {
        InMemoryBoilerStatePersistence boilerState = new() { Saved = new BoilerBelief(true, DateTimeOffset.UtcNow.AddDays(-3)) };
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.BoilerThermostat, livingRoomTarget: 10, bedroom2Target: 10, boilerState: boilerState);
        await context.Service.EvaluateAllSchedules("on the boiler thermostat");

        context.DeviceSettings.SetBoilerControlPath(BoilerControlPath.Fingerbot);
        await context.Service.EvaluateAllSchedules("switch at the boiler flipped");

        context.BoilerFingerbot.ToggleCount.ShouldBe(0, "the saved belief predates days of the boiler thermostat driving the boiler");
        context.Logger.CountMessagesContaining("Assuming the boiler is currently OFF").ShouldBe(1);
    }

    [TestMethod]
    public async Task APressOutsideTheApp_FlipsTheBelief()
    {
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 10, bedroom2Target: 10);
        context.Service.WatchBoilerFingerbot();
        await context.Service.EvaluateAllSchedules("nothing needs heat");
        context.BoilerFingerbot.ToggleCount.ShouldBe(0);

        // Someone presses it from the HA UI, as on 2026-10-19. Nothing needs heat, so the app now
        // knows the boiler is on and turns it off again.
        await context.BoilerFingerbot.SetStateFromOutsideTheApp(true);

        context.Logger.CountMessagesContaining("pressed 1 time(s) outside the app").ShouldBe(1);
        context.BoilerFingerbot.ToggleCount.ShouldBe(1);
        context.Logger.CountMessagesContaining("Boiler turned OFF").ShouldBe(1);
    }

    [TestMethod]
    public async Task TheAppsOwnPress_IsNotMistakenForAnOutsidePress()
    {
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 20, bedroom2Target: 20);
        context.Service.WatchBoilerFingerbot();

        await context.Service.EvaluateAllSchedules("cold");
        await context.Service.EvaluateAllSchedules("still cold");

        context.BoilerFingerbot.ToggleCount.ShouldBe(1);
        context.Logger.CountMessagesContaining("outside the app").ShouldBe(0);
    }

    [TestMethod]
    public async Task TheFingerbotDroppingOffAndComingBack_IsNotAPress()
    {
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 10, bedroom2Target: 10);
        context.Service.WatchBoilerFingerbot();
        await context.Service.EvaluateAllSchedules("nothing needs heat");

        await context.BoilerFingerbot.SetStateFromOutsideTheApp(null);
        await context.BoilerFingerbot.SetStateFromOutsideTheApp(false);
        await context.Service.EvaluateAllSchedules("fingerbot back");

        context.Logger.CountMessagesContaining("outside the app").ShouldBe(0);
        context.BoilerFingerbot.ToggleCount.ShouldBe(0);
    }

    // Finding 3: a press sent to an unavailable fingerbot still flipped the belief.

    [TestMethod]
    public async Task AnUnavailableFingerbot_IsNotPressedAndTheBeliefIsKept()
    {
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 20, bedroom2Target: 20);
        await context.BoilerFingerbot.SetStateFromOutsideTheApp(null);

        await context.Service.EvaluateAllSchedules("cold, fingerbot unavailable");
        await context.Service.EvaluateAllSchedules("still unavailable");

        context.BoilerFingerbot.ToggleCount.ShouldBe(0, "a command to an unreachable device presses nothing");
        context.Logger.CountMessagesContaining("Boiler turned ON").ShouldBe(0);
        context.Logger.CountMessagesContaining("fingerbot is unavailable").ShouldBe(1, "warned once per outage, not every cycle");

        // Back online: the app still believes the boiler is off, so it presses to turn it on.
        await context.BoilerFingerbot.SetStateFromOutsideTheApp(false);
        await context.Service.EvaluateAllSchedules("fingerbot back");

        context.BoilerFingerbot.ToggleCount.ShouldBe(1);
        context.Logger.CountMessagesContaining("Boiler turned ON").ShouldBe(1);
    }

    // Finding 4: a corrective press re-armed the same verification, so a radiator that never
    // responded had the boiler pressed again every cycle.

    [TestMethod]
    public async Task AVerificationThatDisagreesAgainAfterACorrection_DoesNotPressAgain()
    {
        BoilerTestContext context = BoilerTestContext.Create(BoilerControlPath.Fingerbot, roomsCallForHeat: false);
        context.Verification.GetVerifiedBoilerState().Returns(true);

        await context.Service.EvaluateAllSchedules("first mismatch");
        context.BoilerFingerbot.ToggleCount.ShouldBe(1, "the first mismatch is corrected");

        await context.Service.EvaluateAllSchedules("second mismatch");
        await context.Service.EvaluateAllSchedules("third mismatch");

        context.BoilerFingerbot.ToggleCount.ShouldBe(1, "a second disagreement straight after a correction is not trusted with another press");
        context.Logger.CountMessagesContaining("disagreed again").ShouldBe(2);
    }

    // How fast a radiator warms its room (owner, 2026-10-20): measured while its valve is open and the boiler is on.

    [TestMethod]
    public async Task AValveOpenWithTheBoilerOn_IsTimedFromWhenItOpenedToWhenItsRoomReachedTarget()
    {
        // The living room is 15 °C against 18: 3 degrees short, enough to start the boiler. It then rises 1 °C every
        // 30 minutes and reaches 18 after 90 minutes: 2 °C an hour.
        InMemoryWarmUpRateStore store = new();
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.Fingerbot, livingRoomTarget: 18, bedroom2Target: 15, warmUpStore: store);

        for (int minutes = 0; minutes <= 90; minutes += 15)
        {
            context.LivingRoomTemperature.State = 15 + 2.0 * minutes / 60;
            await context.Service.EvaluateAllSchedules("room warming");
            context.AdvanceMinutes(15);
        }

        context.BoilerFingerbot.ToggleCount.ShouldBe(2, "on for the warm-up, then off once the room reached its target, in the pass that ended the run");
        store.Saved.ShouldContainKey(context.LivingRoomThermostat.EntityId);
        store.Saved[context.LivingRoomThermostat.EntityId].ShouldHaveSingleItem().DegreesPerHour.ShouldBe(2, 0.001);
        store.Saved.ShouldNotContainKey(context.Bedroom2Thermostat.EntityId, "that room was never short of its target");
    }

    [TestMethod]
    public async Task WithTheBoilerOnTheBoilerThermostat_NoWarmUpIsTimed()
    {
        // In boiler-thermostat mode nothing says when the boiler runs, so no valve counts as warming its room.
        InMemoryWarmUpRateStore store = new();
        BoilerTestContext context = BoilerTestContext.CreateWithTargets(BoilerControlPath.BoilerThermostat, livingRoomTarget: 18, bedroom2Target: 15, warmUpStore: store);

        for (int minutes = 0; minutes <= 90; minutes += 15)
        {
            context.LivingRoomTemperature.State = 15 + 2.0 * minutes / 60;
            await context.Service.EvaluateAllSchedules("room warming");
            context.AdvanceMinutes(15);
        }

        store.Saved.ShouldBeEmpty();
    }

    private sealed class BoilerTestContext
    {
        public required HeatingControlService Service { get; init; }
        public required FakeTimeProvider TimeProvider { get; init; }
        public required InMemoryBoilerStatePersistence BoilerState { get; init; }
        public required FakeCustomSwitchEntity BoilerFingerbot { get; init; }
        public required FakeCustomClimateControlEntity LivingRoomThermostat { get; init; }
        public required FakeCustomClimateControlEntity Bedroom2Thermostat { get; init; }
        public required FakeCustomNumericSensorEntity LivingRoomTemperature { get; init; }
        public required FakeCustomNumericSensorEntity Bedroom2Temperature { get; init; }
        public required FakeCustomNumericSensorEntity BathroomTemperature { get; init; }
        public required IBoilerVerificationService Verification { get; init; }
        public required FakeDeviceSettingsPersistenceService DeviceSettings { get; init; }
        public required RecordingLogger<HeatingControlService> Logger { get; init; }
        public required List<RoomSchedule> Rooms { get; init; }
        public required MockPresenceService Presence { get; init; }
        public required IRoomStatePersistenceService StatePersistence { get; init; }

        /// <summary>The boiler state sent with the most recent room-state upload.</summary>
        public BoilerStateDto? LastUploadedBoiler() => StatePersistence.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IRoomStatePersistenceService.SetStatesAsync))
            .Select(call => (BoilerStateDto?)call.GetArguments()[1])
            .LastOrDefault();

        /// <summary>The batteries sent with the most recent room-state upload.</summary>
        public List<BatteryDeviceDto>? LastUploadedBatteries() => StatePersistence.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IRoomStatePersistenceService.SetStatesAsync))
            .Select(call => (List<BatteryDeviceDto>?)call.GetArguments()[2])
            .LastOrDefault();

        public int UploadCount() => StatePersistence.ReceivedCalls()
            .Count(call => call.GetMethodInfo().Name == nameof(IRoomStatePersistenceService.SetStatesAsync));

        /// <summary>
        /// Gives the living room the den's real pattern: 15 °C while nobody is in it, and an "in use" track at
        /// <paramref name="inUseTemperature"/> that is only selected while presence says someone is there.
        /// </summary>
        public void GiveLivingRoomAnInUseTrack(double inUseTemperature)
        {
            Rooms[0].ScheduleTracks =
            [
                new HeatingScheduleTrack { Id = 1, TargetTime = new TimeOnly(0, 0), Temperature = 15, Conditions = ConditionType.RoomNotInUse },
                new HeatingScheduleTrack { Id = 2, TargetTime = new TimeOnly(0, 1), Temperature = inUseTemperature, Conditions = ConditionType.RoomInUse }
            ];
        }

        /// <summary>
        /// Builds a house whose living room and bedroom 2 are both radiator-valve rooms, both at
        /// 15 degrees. When they call for heat they are 5 degrees short each, well past the
        /// 3-degree deficit that turns the boiler on; otherwise they are above target.
        /// </summary>
        public static BoilerTestContext Create(BoilerControlPath? boilerControlPath, bool roomsCallForHeat = true)
        {
            double scheduledTemperature = roomsCallForHeat ? 20 : 10;
            return CreateWithTargets(boilerControlPath, scheduledTemperature, scheduledTemperature);
        }

        /// <param name="realVerification">
        /// Use the real <see cref="BoilerVerificationService"/>, so a test sees what the app does with the
        /// valve history it actually reports, instead of a substitute that answers whatever it is told.
        /// </param>
        /// <param name="boilerState">Share one store between two contexts to model a restart.</param>
        public static BoilerTestContext CreateWithTargets(
            BoilerControlPath? boilerControlPath,
            double livingRoomTarget,
            double bedroom2Target,
            bool realVerification = false,
            InMemoryBoilerStatePersistence? boilerState = null,
            IBatteryOverview? batteries = null,
            InMemoryWarmUpRateStore? warmUpStore = null,
            double? bathroomTarget = null)
        {
            ServiceCollection services = new();
            if (warmUpStore != null)
            {
                services.AddSingleton<IWarmUpRateStore>(warmUpStore);
                services.AddSingleton<IWarmUpRates, WarmUpRates>();
            }

            if (batteries != null)
            {
                services.AddSingleton(batteries);
            }

            RecordingLogger<HeatingControlService> logger = new();
            IBoilerVerificationService substituteVerification = Substitute.For<IBoilerVerificationService>();
            FakeDeviceSettingsPersistenceService deviceSettings = new();
            InMemoryBoilerStatePersistence boilerStatePersistence = boilerState ?? new();

            if (boilerControlPath.HasValue)
            {
                deviceSettings.SetBoilerControlPath(boilerControlPath.Value);
            }

            services.AddSingleton<HistoryService, MockHistoryService>();
            services.AddSingleton(Substitute.For<IScheduler>());
            services.AddSingleton(Substitute.For<IHomeBattery>());
            services.AddSingleton(Substitute.For<IElectricityMeter>());
            services.AddSingleton<IPresenceService, MockPresenceService>();
            services.AddSingleton<ILogger<HeatingControlService>>(logger);
            services.AddSingleton(Substitute.For<ILogger<HistoryService>>());
            services.AddSingleton(Substitute.For<NetDaemon.HassModel.IHaContext>());
            services.AddSingleton<FakeNamedEntities>();
            services.AddSingleton<INamedEntities>(provider => provider.GetRequiredService<FakeNamedEntities>());
            services.AddSingleton<HomeAssistantConfiguration>();
            services.AddSingleton<FakeTimeProvider>();
            services.AddSingleton<TimeProvider>(provider => provider.GetRequiredService<FakeTimeProvider>());
            services.AddSingleton(Substitute.For<ILogger<ISchedulePersistenceService>>());
            services.AddSingleton(Substitute.For<ISchedulePersistenceService>());
            services.AddSingleton(Substitute.For<IRoomStatePersistenceService>());
            services.AddSingleton<IBoilerVerificationService>(provider => realVerification
                ? new BoilerVerificationService(Substitute.For<ILogger<BoilerVerificationService>>(), provider.GetRequiredService<FakeTimeProvider>())
                : substituteVerification);
            services.AddSingleton<IDeviceSettingsPersistenceService>(deviceSettings);
            services.AddSingleton<IBoilerStatePersistence>(boilerStatePersistence);

            ServiceProvider provider = services.BuildServiceProvider();

            FakeTimeProvider timeProvider = provider.GetRequiredService<FakeTimeProvider>();
            timeProvider.SetSpecificDateTime(new DateTimeOffset(new DateTime(2025, 11, 24, 12, 00, 00)));

            double roomTemperature = 15;
            FakeNamedEntities namedEntities = provider.GetRequiredService<FakeNamedEntities>();
            ((FakeCustomNumericSensorEntity)namedEntities.LivingRoomClimateTemperature).State = roomTemperature;
            ((FakeCustomNumericSensorEntity)namedEntities.Bedroom2Temperature).State = roomTemperature;

            FakeCustomClimateControlEntity livingRoomThermostat = (FakeCustomClimateControlEntity)namedEntities.LivingRoomRadiatorThermostat;
            FakeCustomClimateControlEntity bedroom2Thermostat = (FakeCustomClimateControlEntity)namedEntities.Bedroom2RadiatorThermostat;

            foreach (FakeCustomClimateControlEntity thermostat in new[] { livingRoomThermostat, bedroom2Thermostat })
            {
                thermostat.CurrentTemperature = roomTemperature;
                thermostat.SetTargetTemperature(roomTemperature - 5);
            }

            List<RoomSchedule> rooms = [
                BuildRoom(1, "Living room", livingRoomTarget),
                BuildRoom(2, "Bedroom 2", bedroom2Target)
            ];

            // The upstairs bathroom, heated only by a towel rail with no valve, at the same 15 degrees.
            ((FakeCustomNumericSensorEntity)namedEntities.UpstairsBathroomTemperature).State = roomTemperature;
            if (bathroomTarget.HasValue)
            {
                rooms.Add(BuildRoom(3, "Upstairs bathroom", bathroomTarget.Value));
            }
            ISchedulePersistenceService schedulePersistence = provider.GetRequiredService<ISchedulePersistenceService>();
            schedulePersistence.GetSchedulesAsync().Returns(Task.FromResult(new RoomSchedules { Rooms = rooms }));
            livingRoomThermostat.SetCount = 0;
            bedroom2Thermostat.SetCount = 0;

            return new BoilerTestContext
            {
                Service = ActivatorUtilities.CreateInstance<HeatingControlService>(provider),
                TimeProvider = timeProvider,
                BoilerState = boilerStatePersistence,
                BoilerFingerbot = (FakeCustomSwitchEntity)namedEntities.BoilerFingerbot,
                LivingRoomThermostat = livingRoomThermostat,
                Bedroom2Thermostat = bedroom2Thermostat,
                LivingRoomTemperature = (FakeCustomNumericSensorEntity)namedEntities.LivingRoomClimateTemperature,
                Bedroom2Temperature = (FakeCustomNumericSensorEntity)namedEntities.Bedroom2Temperature,
                BathroomTemperature = (FakeCustomNumericSensorEntity)namedEntities.UpstairsBathroomTemperature,
                Verification = provider.GetRequiredService<IBoilerVerificationService>(),
                DeviceSettings = deviceSettings,
                Logger = logger,
                Rooms = rooms,
                Presence = (MockPresenceService)provider.GetRequiredService<IPresenceService>(),
                StatePersistence = provider.GetRequiredService<IRoomStatePersistenceService>()
            };
        }

        /// <summary>A valve is open when its setpoint is at or above its own reading (as the app sets it).</summary>
        public bool ValvesAreOpen() => IsOpen(LivingRoomThermostat) && IsOpen(Bedroom2Thermostat);

        public bool ValvesAreShut() => !IsOpen(LivingRoomThermostat) && !IsOpen(Bedroom2Thermostat);

        private static bool IsOpen(FakeCustomClimateControlEntity valve) =>
            valve.TargetTemperature.GetValueOrDefault() >= valve.CurrentTemperature.GetValueOrDefault();

        public void BoostLivingRoom(double temperature)
        {
            DateTimeOffset now = TimeProvider.GetUtcNow();
            Rooms[0].Boost = new Boost { StartTime = now, EndTime = now.AddHours(1), Temperature = temperature };
        }

        public void BoostBathroom(double temperature)
        {
            DateTimeOffset now = TimeProvider.GetUtcNow();
            Rooms.Single(room => room.Id == 3).Boost = new Boost { StartTime = now, EndTime = now.AddHours(1), Temperature = temperature };
        }

        /// <summary>The upstairs bathroom's state from the most recent room-state upload.</summary>
        public RoomState LastUploadedBathroom() => StatePersistence.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IRoomStatePersistenceService.SetStatesAsync))
            .Select(call => (Dictionary<int, RoomState>)call.GetArguments()[0]!)
            .Last()[3];

        public void AdvanceMinutes(int minutes)
        {
            TimeProvider.SetSpecificDateTime(TimeProvider.GetUtcNow().AddMinutes(minutes));
        }

        public void SetRadiatorValveReadings(double temperature)
        {
            LivingRoomThermostat.CurrentTemperature = temperature;
            Bedroom2Thermostat.CurrentTemperature = temperature;
        }

        private static RoomSchedule BuildRoom(int id, string name, double temperature)
        {
            return new RoomSchedule
            {
                Id = id,
                Name = name,
                ScheduleTracks = [
                    new HeatingScheduleTrack
                    {
                        TargetTime = new TimeOnly(00, 00),
                        Temperature = temperature
                    }
                ]
            };
        }
    }
}

internal sealed class InMemoryBoilerStatePersistence : IBoilerStatePersistence
{
    public BoilerBelief? Saved { get; set; }

    public BoilerBelief? Load() => Saved;

    public void Save(BoilerBelief belief) => Saved = belief;
}

/// <summary>
/// Captures formatted log messages so tests can assert that a message is logged once per
/// transition rather than on every evaluation cycle.
/// </summary>
internal class RecordingLogger<T> : ILogger<T>
{
    private readonly List<string> _messages = [];

    public IReadOnlyList<string> Messages => _messages;

    public int CountMessagesContaining(string fragment)
    {
        return _messages.Count(message => message.Contains(fragment, StringComparison.Ordinal));
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        _messages.Add(formatter(state, exception));
    }
}

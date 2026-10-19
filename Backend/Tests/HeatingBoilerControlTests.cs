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

        // The radiators never cool: the turn-off did not reach the boiler.
        context.AdvanceMinutes(BoilerVerificationService.TurnOffSettlingMinutes + 1);
        await context.Service.EvaluateAllSchedules("settled");
        context.AdvanceMinutes(BoilerVerificationService.TurnOffVerificationMinutes + 1);
        await context.Service.EvaluateAllSchedules("verification due");
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
        context.AdvanceMinutes(BoilerVerificationService.TurnOnWaitMinutes + 1);
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

        context.AdvanceMinutes(BoilerVerificationService.TurnOnWaitMinutes + 1);
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

        context.AdvanceMinutes(BoilerVerificationService.TurnOnWaitMinutes + 1);
        context.SetRadiatorValveReadings(15 + BoilerVerificationService.TurnOnTemperatureRiseThreshold + 0.5);
        await context.Service.EvaluateAllSchedules("verification due");
        await context.Service.EvaluateAllSchedules("next cycle");

        context.BoilerFingerbot.ToggleCount.ShouldBe(1, "the boiler responded, so there is nothing to correct");
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
        public required IBoilerVerificationService Verification { get; init; }
        public required FakeDeviceSettingsPersistenceService DeviceSettings { get; init; }
        public required RecordingLogger<HeatingControlService> Logger { get; init; }
        public required List<RoomSchedule> Rooms { get; init; }
        public required MockPresenceService Presence { get; init; }

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
            InMemoryBoilerStatePersistence? boilerState = null)
        {
            ServiceCollection services = new();

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
                Verification = provider.GetRequiredService<IBoilerVerificationService>(),
                DeviceSettings = deviceSettings,
                Logger = logger,
                Rooms = rooms,
                Presence = (MockPresenceService)provider.GetRequiredService<IPresenceService>()
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

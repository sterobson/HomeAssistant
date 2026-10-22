using HomeAssistant.apps;
using HomeAssistant.Services.Climate;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace HomeAssistant.Tests;

/// <summary>
/// The verification service infers whether a fingerbot press landed from radiator temperatures:
/// a turn-on from a rise, a turn-off from a fall after a settling period. Nothing in Home
/// Assistant reports the boiler's real state, so a failed verification is what provokes
/// HeatingControlService into a corrective press - which is why Reset exists and why the
/// boiler-thermostat gate has to stop this running at all.
/// </summary>
[TestClass]
public sealed class BoilerVerificationServiceTests
{
    private const string Room = "living room";
    private const double ColdValveTemperature = 15;
    private const double HotValveTemperature = 25;

    [TestMethod]
    public async Task WithNothingPending_NoBoilerStateIsReported()
    {
        (BoilerVerificationService service, FakeTimeProvider _) = CreateService();

        await service.CheckAsync();

        service.GetVerifiedBoilerState().ShouldBeNull();
    }

    [TestMethod]
    public async Task TurnOn_PassesWhenAColdValveWarmsUp()
    {
        (BoilerVerificationService service, FakeTimeProvider timeProvider) = CreateService();
        FakeCustomClimateControlEntity valve = CreateValve(ColdValveTemperature);

        // A valve has to have been cold for half an hour to be worth watching.
        service.NotifyValveStopped(Room);
        Advance(timeProvider, minutes: 31);
        service.NotifyBoilerToggled(true, [(Room, valve)], boilerOffSince: null);

        Advance(timeProvider, minutes: BoilerVerificationService.TurnOnDeadlineMinutes + 1);
        valve.CurrentTemperature = ColdValveTemperature + BoilerVerificationService.TurnOnTemperatureRiseThreshold + 0.5;
        await service.CheckAsync();

        service.GetVerifiedBoilerState().ShouldBeNull("a passed verification leaves nothing to correct");
    }

    [TestMethod]
    public async Task TurnOn_FailsWhenNoValveWarmsUp_AndReportsTheBoilerOff()
    {
        (BoilerVerificationService service, FakeTimeProvider timeProvider) = CreateService();
        FakeCustomClimateControlEntity valve = CreateValve(ColdValveTemperature);

        service.NotifyValveStopped(Room);
        Advance(timeProvider, minutes: 31);
        service.NotifyBoilerToggled(true, [(Room, valve)], boilerOffSince: null);

        Advance(timeProvider, minutes: BoilerVerificationService.TurnOnDeadlineMinutes + 1);
        await service.CheckAsync();

        // This is the verdict HeatingControlService answers with a corrective press.
        service.GetVerifiedBoilerState().ShouldBe(false);
    }

    [TestMethod]
    public async Task AValveReportedShutEveryCycle_KeepsTheTimeItFirstShut()
    {
        (BoilerVerificationService service, FakeTimeProvider timeProvider) = CreateService();
        FakeCustomClimateControlEntity valve = CreateValve(ColdValveTemperature);

        // The app reports a shut valve on every evaluation, every few minutes.
        service.NotifyValveStopped(Room);
        Advance(timeProvider, minutes: 20);
        service.NotifyValveStopped(Room);
        Advance(timeProvider, minutes: 11);
        service.NotifyBoilerToggled(true, [(Room, valve)], boilerOffSince: null);

        Advance(timeProvider, minutes: BoilerVerificationService.TurnOnDeadlineMinutes + 1);
        await service.CheckAsync();

        service.GetVerifiedBoilerState().ShouldBe(false, "shut for 31 minutes in all, so the press was watched and the cold radiator caught");
    }

    [TestMethod]
    public async Task TurnOn_IsNotJudgedBeforeTheWaitHasElapsed()
    {
        (BoilerVerificationService service, FakeTimeProvider timeProvider) = CreateService();
        FakeCustomClimateControlEntity valve = CreateValve(ColdValveTemperature);

        service.NotifyValveStopped(Room);
        Advance(timeProvider, minutes: 31);
        service.NotifyBoilerToggled(true, [(Room, valve)], boilerOffSince: null);

        Advance(timeProvider, minutes: BoilerVerificationService.TurnOnDeadlineMinutes - 1);
        await service.CheckAsync();

        service.GetVerifiedBoilerState().ShouldBeNull();
    }

    [TestMethod]
    public async Task TurnOff_PassesWhenAHotValveCoolsAfterSettling()
    {
        (BoilerVerificationService service, FakeTimeProvider timeProvider) = CreateService();
        FakeCustomClimateControlEntity valve = CreateValve(HotValveTemperature);

        service.NotifyValveStartedHeating(Room, valve);
        Advance(timeProvider, minutes: 31);
        service.NotifyBoilerToggled(false, [(Room, valve)], boilerOffSince: null);

        Advance(timeProvider, minutes: BoilerVerificationService.TurnOffSettlingMinutes + 1);
        await service.CheckAsync();

        Advance(timeProvider, minutes: BoilerVerificationService.TurnOffVerificationMinutes + 1);
        valve.CurrentTemperature = HotValveTemperature - 2;
        await service.CheckAsync();

        service.GetVerifiedBoilerState().ShouldBeNull();
    }

    [TestMethod]
    public async Task TurnOff_AValveThatOnlyStaysWarm_IsNoReasonToPress()
    {
        // Until 2026-10-21 this failed the turn-off. A TRV that reads the same an hour and a half later may be
        // regulating on a running boiler, or sitting at room temperature with a cold radiator: it proves nothing,
        // and a press on that would invert the belief half the time.
        (BoilerVerificationService service, FakeTimeProvider timeProvider) = CreateService();
        FakeCustomClimateControlEntity valve = CreateValve(HotValveTemperature);

        service.NotifyValveStartedHeating(Room, valve);
        Advance(timeProvider, minutes: 31);
        service.NotifyBoilerToggled(false, [(Room, valve)], boilerOffSince: null);

        Advance(timeProvider, minutes: BoilerVerificationService.TurnOffSettlingMinutes + 1);
        await service.CheckAsync();

        Advance(timeProvider, minutes: BoilerVerificationService.TurnOffVerificationMinutes + 1);
        await service.CheckAsync();

        service.GetVerifiedBoilerState().ShouldBeNull();
    }

    // 2026-10-20, 19:02 BST: the boiler was in fact running (an earlier false failure had pressed it the wrong way),
    // and the turn-off watched only Bedroom 3, whose valve was left open at 22.5 °C. Its TRV regulated itself on the
    // hot water, cycling 21.5 to 23 °C all night, and the old check read 22.0 -> 21.5 as the radiator cooling.

    [TestMethod]
    public async Task TurnOff_AnOpenValveHeatingUpAgain_ShowsTheBoilerStillRunning()
    {
        (BoilerVerificationService service, FakeTimeProvider timeProvider) = CreateService();
        FakeCustomClimateControlEntity valve = CreateValve(22.0);

        service.NotifyValveStartedHeating(Room, valve);
        Advance(timeProvider, minutes: 31);
        service.NotifyBoilerToggled(false, [(Room, valve)], boilerOffSince: null);

        // With the boiler believed off every room is reported shut on each pass; the turn-off still watches it.
        service.NotifyValveStopped(Room);
        Advance(timeProvider, minutes: 15);
        await service.CheckAsync();
        Advance(timeProvider, minutes: BoilerVerificationService.TurnOffSettlingMinutes - 15 + 1);
        valve.CurrentTemperature = 21.5;
        await service.CheckAsync();
        service.GetVerifiedBoilerState().ShouldBeNull("falling between cycles is what fooled the old check");

        Advance(timeProvider, minutes: 10);
        valve.CurrentTemperature = 23.0;
        await service.CheckAsync();

        service.GetVerifiedBoilerState().ShouldBe(true, "a radiator with its valve open heated up again, so the boiler is running");
    }

    [TestMethod]
    public async Task TurnOff_AnOpenValveThatNeverStopsClimbing_ShowsTheBoilerStillRunning_AsSoonAsSettlingEnds()
    {
        // 2026-10-21 21:58 BST: the app's "off" press had in fact turned the boiler on (HOME-ASSISTANT.md gotcha 24).
        // Den radiator 2, open, read 20.0 °C at the turn-off and climbed straight through the 30-minute settling to
        // 23.5 °C. Measured from its low after settling, it needed yet another 1 °C before anything was concluded.
        (BoilerVerificationService service, FakeTimeProvider timeProvider) = CreateService();
        FakeCustomClimateControlEntity valve = CreateValve(20.0);
        service.NotifyValveStartedHeating("den", valve);
        Advance(timeProvider, minutes: 57);
        service.NotifyBoilerToggled(false, [("den", valve)], boilerOffSince: null);

        Advance(timeProvider, minutes: 21);
        valve.CurrentTemperature = 22.0;
        await service.CheckAsync();
        Advance(timeProvider, minutes: 10);
        valve.CurrentTemperature = 23.5;
        await service.CheckAsync();

        service.GetVerifiedBoilerState().ShouldBe(true, "a radiator still climbing after half an hour has hot water behind it");
    }

    [TestMethod]
    public async Task TurnOff_ARadiatorThatCoastedUpAndSitsAtItsPeak_IsNoReasonToPress()
    {
        // 2026-10-21 22:40: den radiator 1 read 23.4 °C at a turn-off that worked, stepped to 23.9 eight minutes later
        // on the water left in it, then reported nothing for over 15 minutes. TRVs lag and step in 0.5 °C, so one that
        // coasted up a full degree can still be showing that peak when settling ends. Only a new high counts.
        (BoilerVerificationService service, FakeTimeProvider timeProvider) = CreateService();
        FakeCustomClimateControlEntity valve = CreateValve(20.0);
        service.NotifyValveStartedHeating(Room, valve);
        Advance(timeProvider, minutes: 31);
        service.NotifyBoilerToggled(false, [(Room, valve)], boilerOffSince: null);

        Advance(timeProvider, minutes: 12);
        valve.CurrentTemperature = 21.0;
        await service.CheckAsync();
        Advance(timeProvider, minutes: BoilerVerificationService.TurnOffSettlingMinutes - 12 + 1);
        await service.CheckAsync();
        Advance(timeProvider, minutes: 20);
        await service.CheckAsync();

        service.GetVerifiedBoilerState().ShouldBeNull("21.0 °C is its settling peak, not a new high");
    }

    [TestMethod]
    public async Task TurnOff_WaterLeftInARadiator_PeaksAndFalls_AndIsNoReasonToPress()
    {
        // A radiator still warming when the boiler goes off: the hot water already in it lifts its TRV for a while,
        // then it cools. Over 1 °C above the turn-off reading, but past its peak once settling ends.
        (BoilerVerificationService service, FakeTimeProvider timeProvider) = CreateService();
        FakeCustomClimateControlEntity valve = CreateValve(20.0);
        service.NotifyValveStartedHeating(Room, valve);
        Advance(timeProvider, minutes: 31);
        service.NotifyBoilerToggled(false, [(Room, valve)], boilerOffSince: null);

        Advance(timeProvider, minutes: 10);
        valve.CurrentTemperature = 22.0;
        await service.CheckAsync();
        Advance(timeProvider, minutes: BoilerVerificationService.TurnOffSettlingMinutes - 10 + 1);
        valve.CurrentTemperature = 21.5;
        await service.CheckAsync();
        Advance(timeProvider, minutes: BoilerVerificationService.TurnOffVerificationMinutes + 1);
        valve.CurrentTemperature = 20.5;
        await service.CheckAsync();

        service.GetVerifiedBoilerState().ShouldBeNull();
    }

    [TestMethod]
    public async Task TurnOff_ARoomWarmedBySomethingElse_DoesNotCountAsTheBoiler()
    {
        // The radiator cooled from 25 to 19 °C; then the sun (or a heater plug) lifts the TRV 1.5 °C. Still far below
        // where the radiator was when the boiler went off, so it is not hot water.
        (BoilerVerificationService service, FakeTimeProvider timeProvider) = CreateService();
        FakeCustomClimateControlEntity valve = CreateValve(HotValveTemperature);

        service.NotifyValveStartedHeating(Room, valve);
        Advance(timeProvider, minutes: 31);
        service.NotifyBoilerToggled(false, [(Room, valve)], boilerOffSince: null);

        Advance(timeProvider, minutes: BoilerVerificationService.TurnOffSettlingMinutes + 1);
        valve.CurrentTemperature = 19;
        await service.CheckAsync();
        Advance(timeProvider, minutes: 20);
        valve.CurrentTemperature = 20.5;
        await service.CheckAsync();
        Advance(timeProvider, minutes: BoilerVerificationService.TurnOffVerificationMinutes);
        await service.CheckAsync();

        service.GetVerifiedBoilerState().ShouldBeNull();
    }

    // 2026-10-20: two turn-on checks failed, 15 minutes after the press, with the boiler running.

    [TestMethod]
    public async Task TurnOn_ASlowTrv_IsNotMistakenForTheBoilerNotFiring_1447()
    {
        // 14:47 BST: the dining radiator's TRV read +0.5 °C after 16 minutes, then rose 4.8 °C.
        (BoilerVerificationService service, FakeTimeProvider timeProvider) = CreateService();
        FakeCustomClimateControlEntity valve = CreateValve(ColdValveTemperature);
        service.NotifyValveStopped(Room);
        Advance(timeProvider, minutes: 31);
        service.NotifyBoilerToggled(true, [(Room, valve)], boilerOffSince: null);

        Advance(timeProvider, minutes: 16);
        valve.CurrentTemperature = ColdValveTemperature + 0.5;
        await service.CheckAsync();
        service.GetVerifiedBoilerState().ShouldBeNull("the old 15-minute check failed here");

        Advance(timeProvider, minutes: 15);
        valve.CurrentTemperature = ColdValveTemperature + 4.8;
        await service.CheckAsync();
        Advance(timeProvider, minutes: BoilerVerificationService.TurnOnDeadlineMinutes);
        await service.CheckAsync();

        service.GetVerifiedBoilerState().ShouldBeNull();
    }

    [TestMethod]
    public async Task TurnOn_ATrvFirstRisingAfter28Minutes_PassesBeforeTheDeadline_1805()
    {
        // 18:05 BST press: Bedroom 3 was +0.1 °C at the 18:20 check; its first rise came at 18:33.
        (BoilerVerificationService service, FakeTimeProvider timeProvider) = CreateService();
        FakeCustomClimateControlEntity valve = CreateValve(ColdValveTemperature);
        service.NotifyValveStopped(Room);
        Advance(timeProvider, minutes: 31);
        service.NotifyBoilerToggled(true, [(Room, valve)], boilerOffSince: null);

        foreach ((int minutes, double rise) in new[] { (15, 0.1), (5, 0.1), (8, 0.5), (7, 1.0) })
        {
            Advance(timeProvider, minutes);
            valve.CurrentTemperature = ColdValveTemperature + rise;
            await service.CheckAsync();
            service.GetVerifiedBoilerState().ShouldBeNull();
        }

        // Passed at 35 minutes, so nothing is left to fail at the deadline even if the TRV dips again.
        valve.CurrentTemperature = ColdValveTemperature;
        Advance(timeProvider, minutes: BoilerVerificationService.TurnOnDeadlineMinutes);
        await service.CheckAsync();
        service.GetVerifiedBoilerState().ShouldBeNull();
    }

    [TestMethod]
    public async Task TurnOn_AValveTheAppShutsBeforeItRose_StopsCounting()
    {
        (BoilerVerificationService service, FakeTimeProvider timeProvider) = CreateService();
        FakeCustomClimateControlEntity valve = CreateValve(ColdValveTemperature);
        service.NotifyValveStopped(Room);
        Advance(timeProvider, minutes: 31);
        service.NotifyBoilerToggled(true, [(Room, valve)], boilerOffSince: null);
        service.NotifyValveStartedHeating(Room, valve);

        // Its room reached target by other means and the app shut it: it can show nothing more.
        Advance(timeProvider, minutes: 20);
        service.NotifyValveStopped(Room);
        Advance(timeProvider, minutes: BoilerVerificationService.TurnOnDeadlineMinutes);
        await service.CheckAsync();

        service.GetVerifiedBoilerState().ShouldBeNull("with nothing left to watch, nothing is concluded");
    }

    [TestMethod]
    public async Task TurnOn_AColdRadiatorWhoseValveOpensLater_IsWatched_AndCatchesAPressThatNeverReachedTheBoiler()
    {
        // 2026-10-22: the boiler had been off since 00:53 when the app pressed it on at 02:42 for the den. The press
        // never reached the boiler. The den's valves were shut at 03:25 before rising, and the check gave up; Bedroom
        // 3's valve, shut in the pressing pass, opened at 03:03 and cooled 17.5 -> 16.5 °C for the next two hours.
        (BoilerVerificationService service, FakeTimeProvider timeProvider) = CreateService();
        DateTimeOffset boilerOffSince = timeProvider.GetUtcNow();
        FakeCustomClimateControlEntity den1 = new() { EntityId = "climate.den_radiator_1_thermostat", CurrentTemperature = 21.9 };
        FakeCustomClimateControlEntity den2 = new() { EntityId = "climate.den_radiator_2_thermostat", CurrentTemperature = 21.5 };
        FakeCustomClimateControlEntity bedroom3 = new() { EntityId = "climate.bedroom_3_radiator_thermostat", CurrentTemperature = 17.8 };
        Advance(timeProvider, minutes: 109);

        service.NotifyBoilerToggled(true, [("den", den1), ("den", den2)], boilerOffSince);
        service.NotifyValveStartedHeating("den", den1);
        service.NotifyValveStartedHeating("den", den2);
        service.NotifyValveStopped("Bedroom 3");

        Advance(timeProvider, minutes: 21);
        bedroom3.CurrentTemperature = 17.5;
        service.NotifyValveStartedHeating("Bedroom 3", bedroom3);
        await service.CheckAsync();

        Advance(timeProvider, minutes: 22);
        den1.CurrentTemperature = 21.4;
        den2.CurrentTemperature = 20.5;
        service.NotifyValveStopped("den");
        await service.CheckAsync();
        service.GetVerifiedBoilerState().ShouldBeNull("the den's valves were shut, but Bedroom 3 is still watched");

        Advance(timeProvider, minutes: 39);
        bedroom3.CurrentTemperature = 17.0;
        await service.CheckAsync();

        service.GetVerifiedBoilerState().ShouldBe(false, "Bedroom 3 has been open an hour with its radiator cold, so the boiler is off");
    }

    [TestMethod]
    public async Task TurnOn_AValveThatWasOpenOnTheRunningBoiler_IsNotWatchedWhenItReopens()
    {
        // A radiator that has just had hot water behind it could not rise another degree, so it proves nothing.
        (BoilerVerificationService service, FakeTimeProvider timeProvider) = CreateService();
        FakeCustomClimateControlEntity valve = CreateValve(ColdValveTemperature);
        FakeCustomClimateControlEntity warm = new() { EntityId = "climate.bedroom_3_radiator_thermostat", CurrentTemperature = 24 };
        service.NotifyValveStopped(Room);
        Advance(timeProvider, minutes: 31);

        service.NotifyBoilerToggled(true, [(Room, valve), ("Bedroom 3", warm)], boilerOffSince: null);
        service.NotifyValveStartedHeating(Room, valve);
        service.NotifyValveStartedHeating("Bedroom 3", warm);
        Advance(timeProvider, minutes: 10);
        service.NotifyValveStopped("Bedroom 3");
        service.NotifyValveStopped(Room);
        Advance(timeProvider, minutes: 5);
        service.NotifyValveStartedHeating("Bedroom 3", warm);

        // Bedroom 3 stays at 24 °C for well over an hour: had it been watched, that would fail the turn-on.
        Advance(timeProvider, minutes: BoilerVerificationService.TurnOnDeadlineMinutes + 15);
        await service.CheckAsync();

        service.GetVerifiedBoilerState().ShouldBeNull("nothing cold is open, so nothing is concluded");
    }

    [TestMethod]
    public async Task TurnOn_IsArmedWithNothingToWatchYet_AndWatchesTheFirstColdRadiatorToOpen()
    {
        // The press itself shut or left shut every valve; one opens later.
        (BoilerVerificationService service, FakeTimeProvider timeProvider) = CreateService();
        DateTimeOffset boilerOffSince = timeProvider.GetUtcNow();
        FakeCustomClimateControlEntity valve = CreateValve(ColdValveTemperature);
        Advance(timeProvider, minutes: 60);

        service.NotifyBoilerToggled(true, [], boilerOffSince);
        Advance(timeProvider, minutes: 10);
        service.NotifyValveStartedHeating(Room, valve);

        Advance(timeProvider, minutes: BoilerVerificationService.TurnOnDeadlineMinutes - 1);
        await service.CheckAsync();
        service.GetVerifiedBoilerState().ShouldBeNull("its hour runs from when it opened, not from the press");

        Advance(timeProvider, minutes: 2);
        await service.CheckAsync();
        service.GetVerifiedBoilerState().ShouldBe(false);
    }

    [TestMethod]
    public async Task Reset_DiscardsAPendingVerificationSoItCannotProvokeACorrectivePress()
    {
        (BoilerVerificationService service, FakeTimeProvider timeProvider) = CreateService();
        FakeCustomClimateControlEntity valve = CreateValve(ColdValveTemperature);

        service.NotifyValveStopped(Room);
        Advance(timeProvider, minutes: 31);
        service.NotifyBoilerToggled(true, [(Room, valve)], boilerOffSince: null);

        service.Reset();

        // The valve never warms, so without the reset this would fail and report "boiler off".
        Advance(timeProvider, minutes: BoilerVerificationService.TurnOnDeadlineMinutes + 1);
        await service.CheckAsync();

        service.GetVerifiedBoilerState().ShouldBeNull();
    }

    [TestMethod]
    public async Task Reset_ClearsValveHistorySoNoLaterVerificationIsArmed()
    {
        (BoilerVerificationService service, FakeTimeProvider timeProvider) = CreateService();
        FakeCustomClimateControlEntity valve = CreateValve(ColdValveTemperature);

        service.NotifyValveStopped(Room);
        Advance(timeProvider, minutes: 31);

        service.Reset();

        // Without the cold-for-30-minutes history there is nothing to measure against.
        service.NotifyBoilerToggled(true, [(Room, valve)], boilerOffSince: null);
        Advance(timeProvider, minutes: BoilerVerificationService.TurnOnDeadlineMinutes + 1);
        await service.CheckAsync();

        service.GetVerifiedBoilerState().ShouldBeNull();
    }

    private static (BoilerVerificationService Service, FakeTimeProvider TimeProvider) CreateService()
    {
        FakeTimeProvider timeProvider = new();
        timeProvider.SetSpecificDateTime(new DateTimeOffset(new DateTime(2025, 11, 24, 12, 00, 00), TimeSpan.Zero));

        BoilerVerificationService service = new(Substitute.For<ILogger<BoilerVerificationService>>(), timeProvider);

        return (service, timeProvider);
    }

    private static FakeCustomClimateControlEntity CreateValve(double currentTemperature)
    {
        return new FakeCustomClimateControlEntity
        {
            EntityId = "climate.living_room_radiator_thermostat",
            CurrentTemperature = currentTemperature
        };
    }

    private static void Advance(FakeTimeProvider timeProvider, int minutes)
    {
        timeProvider.SetSpecificDateTime(timeProvider.GetUtcNow().AddMinutes(minutes));
    }
}

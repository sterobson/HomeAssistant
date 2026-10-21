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

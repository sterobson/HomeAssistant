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
/// external-scheduler gate has to stop this running at all.
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

        Advance(timeProvider, minutes: BoilerVerificationService.TurnOnWaitMinutes + 1);
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

        Advance(timeProvider, minutes: BoilerVerificationService.TurnOnWaitMinutes + 1);
        await service.CheckAsync();

        // This is the verdict HeatingControlService answers with a corrective press.
        service.GetVerifiedBoilerState().ShouldBe(false);
    }

    [TestMethod]
    public async Task TurnOn_IsNotJudgedBeforeTheWaitHasElapsed()
    {
        (BoilerVerificationService service, FakeTimeProvider timeProvider) = CreateService();
        FakeCustomClimateControlEntity valve = CreateValve(ColdValveTemperature);

        service.NotifyValveStopped(Room);
        Advance(timeProvider, minutes: 31);
        service.NotifyBoilerToggled(true, [(Room, valve)], boilerOffSince: null);

        Advance(timeProvider, minutes: BoilerVerificationService.TurnOnWaitMinutes - 1);
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
    public async Task TurnOff_FailsWhenAValveStaysHot_AndReportsTheBoilerOn()
    {
        (BoilerVerificationService service, FakeTimeProvider timeProvider) = CreateService();
        FakeCustomClimateControlEntity valve = CreateValve(HotValveTemperature);

        service.NotifyValveStartedHeating(Room, valve);
        Advance(timeProvider, minutes: 31);
        service.NotifyBoilerToggled(false, [(Room, valve)], boilerOffSince: null);

        Advance(timeProvider, minutes: BoilerVerificationService.TurnOffSettlingMinutes + 1);
        await service.CheckAsync();

        Advance(timeProvider, minutes: BoilerVerificationService.TurnOffVerificationMinutes + 1);
        await service.CheckAsync();

        service.GetVerifiedBoilerState().ShouldBe(true);
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
        Advance(timeProvider, minutes: BoilerVerificationService.TurnOnWaitMinutes + 1);
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
        Advance(timeProvider, minutes: BoilerVerificationService.TurnOnWaitMinutes + 1);
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

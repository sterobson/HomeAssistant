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
/// Whether a boost is still running must be decided on a timeline, not on clock faces. The UI sends a
/// boost's end as a UTC timestamp, and the heating service's "now" is the local wall clock, so comparing
/// the two as bare date-times is wrong by the UTC offset: in British Summer Time every boost ended an hour
/// early and any boost shorter than an hour never took effect at all.
///
/// The shared FakeTimeProvider is hard-wired to GMT, which can never show that, so these tests use a clock
/// that really is in London.
/// </summary>
[TestClass]
public sealed class BoostExpiryTests
{
    // 12:00 UTC is 13:00 in London in July (BST, UTC+1) and 12:00 in December (GMT, UTC+0).
    private static readonly DateTimeOffset SummerNoonUtc = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WinterNoonUtc = new(2026, 12, 10, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task InSummer_AThirtyMinuteBoostIsActive()
    {
        // The bug in one line: the end is 12:30 UTC, "now" reads 13:00 locally, and 12:30 > 13:00 is false.
        (await BoostIsActive(SummerNoonUtc, SummerNoonUtc.AddMinutes(30))).ShouldBeTrue();
    }

    [TestMethod]
    public async Task InSummer_AFifteenMinuteBoostIsActive()
    {
        (await BoostIsActive(SummerNoonUtc, SummerNoonUtc.AddMinutes(15))).ShouldBeTrue();
    }

    [TestMethod]
    public async Task InSummer_AOneHourBoostIsActive()
    {
        // Exactly one UTC offset: ended the instant it began under the bug.
        (await BoostIsActive(SummerNoonUtc, SummerNoonUtc.AddHours(1))).ShouldBeTrue();
    }

    [TestMethod]
    public async Task InSummer_ALongBoostIsActive()
    {
        (await BoostIsActive(SummerNoonUtc, SummerNoonUtc.AddHours(3))).ShouldBeTrue();
    }

    [TestMethod]
    public async Task InSummer_ABoostThatEndedFiveMinutesAgoIsNotActive()
    {
        (await BoostIsActive(SummerNoonUtc, SummerNoonUtc.AddMinutes(-5))).ShouldBeFalse();
    }

    [TestMethod]
    public async Task InSummer_ABoostThatEndedExactlyNowIsNotActive()
    {
        (await BoostIsActive(SummerNoonUtc, SummerNoonUtc)).ShouldBeFalse();
    }

    [TestMethod]
    public async Task InWinter_ABoostIsActiveUntilItEnds()
    {
        (await BoostIsActive(WinterNoonUtc, WinterNoonUtc.AddMinutes(30))).ShouldBeTrue();
        (await BoostIsActive(WinterNoonUtc, WinterNoonUtc.AddMinutes(-5))).ShouldBeFalse();
    }

    [TestMethod]
    public async Task AnEndTimeWrittenInLocalSummerTimeIsHonouredAsTheSameInstant()
    {
        // 13:30+01:00 and 12:30Z are the same moment, so both are 30 minutes away at 12:00Z.
        DateTimeOffset sameInstantInBst = new(2026, 7, 10, 13, 30, 0, TimeSpan.FromHours(1));
        (await BoostIsActive(SummerNoonUtc, sameInstantInBst)).ShouldBeTrue();

        DateTimeOffset pastInBst = new(2026, 7, 10, 12, 30, 0, TimeSpan.FromHours(1)); // 11:30Z
        (await BoostIsActive(SummerNoonUtc, pastInBst)).ShouldBeFalse();
    }

    [TestMethod]
    public async Task ABoostWithNoTemperatureIsNeverActive()
    {
        (await BoostIsActive(SummerNoonUtc, SummerNoonUtc.AddHours(3), temperature: null)).ShouldBeFalse();
    }

    // ---- harness ------------------------------------------------------------------------------

    private const double RoomTemperature = 15;
    private const double IdleRadiatorTarget = RoomTemperature - 5; // 10
    private const double HeatingRadiatorTarget = RoomTemperature + 5; // 20

    /// <summary>
    /// Runs the real heating service for a radiator zone whose schedule alone wants it cooler than the
    /// room, so the valve only opens if the boost is judged active.
    /// </summary>
    private static async Task<bool> BoostIsActive(DateTimeOffset nowUtc, DateTimeOffset boostEnd, double? temperature = 25)
    {
        ServiceCollection services = new();

        ISchedulePersistenceService schedulePersistence = Substitute.For<ISchedulePersistenceService>();

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
        services.AddSingleton<TimeProvider>(new LondonClock(nowUtc));
        services.AddSingleton(Substitute.For<ILogger<ISchedulePersistenceService>>());
        services.AddSingleton(schedulePersistence);
        services.AddSingleton(Substitute.For<IRoomStatePersistenceService>());
        services.AddSingleton(Substitute.For<IBoilerVerificationService>());
        services.AddSingleton<FakeDeviceSettingsPersistenceService>();
        services.AddSingleton<IDeviceSettingsPersistenceService>(provider => provider.GetRequiredService<FakeDeviceSettingsPersistenceService>());

        ServiceProvider provider = services.BuildServiceProvider();
        FakeNamedEntities entities = provider.GetRequiredService<FakeNamedEntities>();

        ((FakeCustomNumericSensorEntity)entities.DiningRoomClimateTemperature).State = RoomTemperature;
        FakeCustomClimateControlEntity radiator = (FakeCustomClimateControlEntity)entities.DiningRoomRadiatorThermostat;
        radiator.CurrentTemperature = RoomTemperature;
        radiator.SetTargetTemperature(IdleRadiatorTarget);

        // The house is occupied, so the schedule would otherwise apply; it asks for 14 degrees, below the room.
        ((FakeCustomSwitchEntity)entities.DiningRoomDeskPlugOnOff).TurnOn();
        ((FakeCustomNumericSensorEntity)entities.DiningRoomDeskPlugPower).State = 50;

        schedulePersistence.GetSchedulesAsync().Returns(Task.FromResult(new RoomSchedules
        {
            Rooms =
            [
                new RoomSchedule
                {
                    Id = 1,
                    Name = "dining room",
                    Boost = new Boost { StartTime = nowUtc, EndTime = boostEnd, Temperature = temperature },
                    ScheduleTracks = [new HeatingScheduleTrack { TargetTime = new TimeOnly(0, 0), Temperature = 14 }]
                }
            ]
        }));

        HeatingControlService sut = ActivatorUtilities.CreateInstance<HeatingControlService>(provider);
        await sut.EvaluateAllSchedules("unit test");

        radiator.TargetTemperature.ShouldBeOneOf(IdleRadiatorTarget, HeatingRadiatorTarget);
        return radiator.TargetTemperature == HeatingRadiatorTarget;
    }

    /// <summary>A clock frozen at an instant, reporting local time in London.</summary>
    private sealed class LondonClock(DateTimeOffset nowUtc) : TimeProvider
    {
        private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

        public override DateTimeOffset GetUtcNow() => nowUtc;

        public override TimeZoneInfo LocalTimeZone => London;
    }
}

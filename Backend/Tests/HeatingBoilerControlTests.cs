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
/// temperature/time scheduler in the kitchen - chosen by a physical switch that Home Assistant
/// cannot read. In scheduler mode the boiler's button is dead, so a fingerbot press is a no-op,
/// and verifying one against radiator temperatures reads the scheduler's own burns as evidence.
/// These tests cover the gate that keeps the app's hands off the boiler in that mode.
/// </summary>
[TestClass]
public sealed class HeatingBoilerControlTests
{
    private const string DisabledLogFragment = "Boiler control is DISABLED";
    private const string EnabledLogFragment = "Boiler control is ENABLED";

    [TestMethod]
    public async Task ExternalSchedulerMode_DoesNotPressTheFingerbot()
    {
        BoilerTestContext context = BoilerTestContext.Create(BoilerControlPath.ExternalScheduler);

        await context.Service.EvaluateAllSchedules("unit test");

        context.BoilerFingerbot.ToggleCount.ShouldBe(0, "the boiler's button is dead in external scheduler mode, so no press should be attempted");
        context.Verification.DidNotReceiveWithAnyArgs().NotifyBoilerToggled(default, default!);
    }

    [TestMethod]
    public async Task ExternalSchedulerMode_StillControlsRoomsNormally()
    {
        BoilerTestContext context = BoilerTestContext.Create(BoilerControlPath.ExternalScheduler);

        await context.Service.EvaluateAllSchedules("unit test");

        // Only the boiler actuator is gated - valves and heater plugs carry on.
        context.LivingRoomThermostat.TargetTemperature.GetValueOrDefault().ShouldBeGreaterThan(context.LivingRoomThermostat.CurrentTemperature.GetValueOrDefault());
        context.Bedroom2Thermostat.TargetTemperature.GetValueOrDefault().ShouldBeGreaterThan(context.Bedroom2Thermostat.CurrentTemperature.GetValueOrDefault());
    }

    [TestMethod]
    public async Task ExternalSchedulerMode_DoesNotRunBoilerVerification()
    {
        BoilerTestContext context = BoilerTestContext.Create(BoilerControlPath.ExternalScheduler);

        await context.Service.EvaluateAllSchedules("unit test");

        await context.Verification.DidNotReceive().CheckAsync();
        context.Verification.DidNotReceiveWithAnyArgs().NotifyValveStartedHeating(default!, default!);
        context.Verification.DidNotReceiveWithAnyArgs().NotifyValveStopped(default!);
    }

    [TestMethod]
    public async Task ExternalSchedulerMode_DiscardsAnyPendingVerification()
    {
        BoilerTestContext context = BoilerTestContext.Create(BoilerControlPath.ExternalScheduler);

        await context.Service.EvaluateAllSchedules("unit test");

        // Anything left pending from fingerbot mode must go, or it could resurface as a
        // corrective press long after the switch at the boiler was flipped.
        context.Verification.Received(1).Reset();
    }

    [TestMethod]
    public async Task ExternalSchedulerMode_DoesNotIssueTheCorrectivePress()
    {
        BoilerTestContext context = BoilerTestContext.Create(BoilerControlPath.ExternalScheduler, roomsCallForHeat: false);
        context.Verification.GetVerifiedBoilerState().Returns(true);

        await context.Service.EvaluateAllSchedules("unit test");

        context.BoilerFingerbot.ToggleCount.ShouldBe(0, "a mismatch reported in external scheduler mode cannot be corrected by a press");
        context.Verification.DidNotReceive().GetVerifiedBoilerState();
    }

    [TestMethod]
    public async Task ExternalSchedulerMode_LogsOnlyOnceAcrossRepeatedEvaluations()
    {
        BoilerTestContext context = BoilerTestContext.Create(BoilerControlPath.ExternalScheduler);

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
        context.Verification.Received(1).NotifyBoilerToggled(true, Arg.Any<List<(string RoomName, ICustomClimateControlEntity Valve)>>());
        await context.Verification.Received(1).CheckAsync();
        context.Verification.DidNotReceive().Reset();
    }

    [TestMethod]
    public async Task FingerbotMode_DoesNotPressTheFingerbotWhenNoRoomCallsForHeat()
    {
        BoilerTestContext context = BoilerTestContext.Create(BoilerControlPath.Fingerbot, roomsCallForHeat: false);

        await context.Service.EvaluateAllSchedules("unit test");

        context.BoilerFingerbot.ToggleCount.ShouldBe(0);
        context.Verification.DidNotReceiveWithAnyArgs().NotifyBoilerToggled(default, default!);
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
        BoilerTestContext context = BoilerTestContext.Create(BoilerControlPath.ExternalScheduler);

        await context.Service.EvaluateAllSchedules("while on the external scheduler");
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

    private sealed class BoilerTestContext
    {
        public required HeatingControlService Service { get; init; }
        public required FakeCustomSwitchEntity BoilerFingerbot { get; init; }
        public required FakeCustomClimateControlEntity LivingRoomThermostat { get; init; }
        public required FakeCustomClimateControlEntity Bedroom2Thermostat { get; init; }
        public required FakeCustomNumericSensorEntity LivingRoomTemperature { get; init; }
        public required FakeCustomNumericSensorEntity Bedroom2Temperature { get; init; }
        public required IBoilerVerificationService Verification { get; init; }
        public required FakeDeviceSettingsPersistenceService DeviceSettings { get; init; }
        public required RecordingLogger<HeatingControlService> Logger { get; init; }

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

        public static BoilerTestContext CreateWithTargets(BoilerControlPath? boilerControlPath, double livingRoomTarget, double bedroom2Target)
        {
            ServiceCollection services = new();

            RecordingLogger<HeatingControlService> logger = new();
            IBoilerVerificationService verification = Substitute.For<IBoilerVerificationService>();
            FakeDeviceSettingsPersistenceService deviceSettings = new();

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
            services.AddSingleton(verification);
            services.AddSingleton<IDeviceSettingsPersistenceService>(deviceSettings);

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

            ISchedulePersistenceService schedulePersistence = provider.GetRequiredService<ISchedulePersistenceService>();
            schedulePersistence.GetSchedulesAsync().Returns(Task.FromResult(new RoomSchedules
            {
                Rooms = [
                    BuildRoom(1, "Living room", livingRoomTarget),
                    BuildRoom(2, "Bedroom 2", bedroom2Target)
                ]
            }));

            return new BoilerTestContext
            {
                Service = ActivatorUtilities.CreateInstance<HeatingControlService>(provider),
                BoilerFingerbot = (FakeCustomSwitchEntity)namedEntities.BoilerFingerbot,
                LivingRoomThermostat = livingRoomThermostat,
                Bedroom2Thermostat = bedroom2Thermostat,
                LivingRoomTemperature = (FakeCustomNumericSensorEntity)namedEntities.LivingRoomClimateTemperature,
                Bedroom2Temperature = (FakeCustomNumericSensorEntity)namedEntities.Bedroom2Temperature,
                Verification = verification,
                DeviceSettings = deviceSettings,
                Logger = logger
            };
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

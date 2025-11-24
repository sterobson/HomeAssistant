using HomeAssistant.apps;
using HomeAssistant.Devices.Batteries;
using HomeAssistant.Devices.Meters;
using HomeAssistant.Services;
using HomeAssistant.Services.Climate;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Reactive.Concurrency;

namespace HomeAssistant.Tests;

[TestClass]
public sealed class HeatinControlServiceTests
{
    private const bool Heating_Is_Off = false;
    private const bool Heating_Is_On = true;
    private const bool Heating_Should_Be_Off = false;
    private const bool Heating_Should_Be_On = true;

    [TestMethod]
    [DataRow("2025-11-23", "05:00", 19, Heating_Is_Off, 16, Heating_Should_Be_Off)]
    public async Task Test1(string date, string time, double? currentTemperature, bool currentHeatingState, double expectedTargetTemperature, bool expectedHeatingState)
    {
        ServiceProvider serviceProvider = GetServiceProvider();

        MockPresenceService presenceService = (MockPresenceService)serviceProvider.GetRequiredService<IPresenceService>();
        presenceService.SetRoomValue(Room.DiningRoom, true);

        HeatingControlService sut = ActivatorUtilities.CreateInstance<HeatingControlService>(serviceProvider);

        // serviceProvider.GetRequiredKeyedService<NamedEntities>();

        List<Schedule> schedules = [
            new Schedule(){
                 Room = Room.DiningRoom,
                 Condition = () => true,
                 ScheduleTracks = [
                    new HeatingScheduleTrack(){
                          TargetTime = new TimeOnly(06,00),
                          Temperature = 20
                    },
                    new HeatingScheduleTrack(){
                          TargetTime = new TimeOnly(07,00),
                          Temperature = 22,
                          Days = Days.Sunday
                    },
                    new HeatingScheduleTrack(){
                          TargetTime = new TimeOnly(07,00),
                          Temperature = 18,
                          Days = Days.Everyday & ~Days.Sunday
                    },
                    new HeatingScheduleTrack(){
                          TargetTime = new TimeOnly(10,00),
                          Temperature = 15
                    },
                    new HeatingScheduleTrack(){
                          TargetTime = new TimeOnly(18,00),
                          Temperature = 21
                    },
                    new HeatingScheduleTrack(){
                          TargetTime = new TimeOnly(21,00),
                          Temperature = 16
                    }
                ]
            }
        ];

        await sut.EvaluateAllSchedules(sut.Schedules);
    }

    private static ServiceProvider GetServiceProvider()
    {
        ServiceCollection services = new();
        services.AddSingleton<HistoryService>();
        services.AddSingleton(Substitute.For<IScheduler>());
        services.AddSingleton(Substitute.For<IHomeBattery>());
        services.AddSingleton(Substitute.For<ISolarPanels>());
        services.AddSingleton(Substitute.For<IElectricityMeter>());
        services.AddSingleton<IPresenceService, MockPresenceService>();
        services.AddSingleton(Substitute.For<ILogger<HeatingControlService>>());
        services.AddSingleton(Substitute.For<ILogger<HistoryService>>());
        services.AddSingleton(Substitute.For<NetDaemon.HassModel.IHaContext>());
        services.AddSingleton<NamedEntities>();
        services.AddSingleton<HeatingControlService>();
        services.AddSingleton<HomeAssistantConfiguration>();
        services.AddSingleton<FakeTimeProvider>();

        return services.BuildServiceProvider();
    }
}

internal class MockPresenceService : IPresenceService
{
    private readonly Dictionary<Room, bool> _presenceValue = [];

    public void SetRoomValue(Room room, bool value)
    {
        _presenceValue[room] = value;
    }

    public Task<bool> IsRoomInUse(Room room)
    {
        if (_presenceValue.TryGetValue(room, out bool result))
        {
            return Task.FromResult(result);
        }

        return Task.FromResult(false);
    }
}

internal class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _specificDateTime;

    public void SetSpecificDateTime(DateTimeOffset specificDateTime) => _specificDateTime = specificDateTime;

    public override DateTimeOffset GetUtcNow() => _specificDateTime;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.FindSystemTimeZoneById("GMT");
}
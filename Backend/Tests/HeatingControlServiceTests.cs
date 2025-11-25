using HomeAssistant.apps;
using HomeAssistant.Devices.Batteries;
using HomeAssistant.Devices.Meters;
using HomeAssistant.Services;
using HomeAssistant.Services.Climate;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using System.Reactive.Concurrency;

namespace HomeAssistant.Tests;

[TestClass]
public sealed class HeatingControlServiceTests
{
    private const bool Heating_Is_Off = false;
    private const bool Heating_Is_On = true;
    private const bool Heating_Should_Be_Off = false;
    private const bool Heating_Should_Be_On = true;
    private const bool Room_Unoccupied = false;
    private const bool Room_Occupied = true;
    private const double Current_Temp_13 = 13;
    private const double Current_Temp_14 = 14;
    private const double Current_Temp_15 = 15;
    private const double Current_Temp_16 = 16;
    private const double Current_Temp_17 = 17;
    private const double Current_Temp_18 = 18;
    private const double Current_Temp_19 = 19;
    private const double Current_Temp_20 = 20;
    private const double Current_Temp_21 = 21;
    private const double Current_Temp_22 = 22;
    private const double Current_Temp_23 = 23;

    [TestMethod]
    // GamesRoom test cases
    [DataRow(Room.GamesRoom, "2025-11-23", "00:59", Current_Temp_15, Heating_Is_Off, Heating_Should_Be_On, Room_Occupied, "Schedule 15 in 1 minute, but was scheduled 16 a few hours ago and should still be active")]
    [DataRow(Room.GamesRoom, "2025-11-23", "00:00", Current_Temp_16, Heating_Is_Off, Heating_Should_Be_Off, Room_Occupied, "Schedule 15 in 1 hour, already warm enough")]
    [DataRow(Room.GamesRoom, "2025-11-23", "00:00", Current_Temp_15, Heating_Is_Off, Heating_Should_Be_On, Room_Occupied, "Schedule 16 at 9pm, not yet warm enough")]
    [DataRow(Room.GamesRoom, "2025-11-23", "00:45", Current_Temp_14, Heating_Is_Off, Heating_Should_Be_On, Room_Occupied, "Schedule 15 in 15 minutes, not already warm enough, should be ramping up")]
    [DataRow(Room.GamesRoom, "2025-11-23", "07:00", Current_Temp_17, Heating_Is_Off, Heating_Should_Be_On, Room_Occupied, "Target is 22 on Sundays, 18 every other day - this is Sunday")]
    [DataRow(Room.GamesRoom, "2025-11-23", "07:00", Current_Temp_18, Heating_Is_Off, Heating_Should_Be_On, Room_Occupied, "Target is 22 on Sundays, 18 every other day - this is Sunday")]
    [DataRow(Room.GamesRoom, "2025-11-23", "07:00", Current_Temp_22, Heating_Is_Off, Heating_Should_Be_Off, Room_Occupied, "Target is 22 on Sundays, 18 every other day - this is Sunday")]
    [DataRow(Room.GamesRoom, "2025-11-23", "07:00", Current_Temp_23, Heating_Is_Off, Heating_Should_Be_Off, Room_Occupied, "Target is 22 on Sundays, 18 every other day - this is Sunday")]
    [DataRow(Room.GamesRoom, "2025-11-23", "07:00", Current_Temp_23, Heating_Is_On, Heating_Should_Be_Off, Room_Occupied, "Target is 22 on Sundays, 18 every other day - this is Sunday")]
    [DataRow(Room.GamesRoom, "2025-11-22", "07:00", Current_Temp_17, Heating_Is_Off, Heating_Should_Be_On, Room_Occupied, "Target is 22 on Sundays, 18 every other day - this is Saturday")]
    [DataRow(Room.GamesRoom, "2025-11-22", "07:00", Current_Temp_18, Heating_Is_Off, Heating_Should_Be_Off, Room_Occupied, "Target is 22 on Sundays, 18 every other day - this is Saturday")]
    [DataRow(Room.GamesRoom, "2025-11-22", "07:00", Current_Temp_22, Heating_Is_Off, Heating_Should_Be_Off, Room_Occupied, "Target is 22 on Sundays, 18 every other day - this is Saturday")]
    [DataRow(Room.GamesRoom, "2025-11-22", "07:00", Current_Temp_23, Heating_Is_Off, Heating_Should_Be_Off, Room_Occupied, "Target is 22 on Sundays, 18 every other day - this is Saturday")]
    [DataRow(Room.GamesRoom, "2025-11-22", "07:00", Current_Temp_23, Heating_Is_On, Heating_Should_Be_Off, Room_Occupied, "Target is 22 on Sundays, 18 every other day - this is Saturday")]
    // No pre-cooling tests
    [DataRow(Room.GamesRoom, "2025-11-23", "00:45", Current_Temp_16, Heating_Is_Off, Heating_Should_Be_Off, Room_Occupied, "At target temp from 21:00 schedule, don't pre-cool for lower upcoming 01:00 schedule")]
    [DataRow(Room.GamesRoom, "2025-11-23", "20:45", Current_Temp_20, Heating_Is_Off, Heating_Should_Be_On, Room_Occupied, "Don't pre-cool for upcoming 21:00 lower target (16°C), maintain 18:00 target (21°C), heating on to reach 21°C")]
    [DataRow(Room.GamesRoom, "2025-11-23", "20:45", Current_Temp_21, Heating_Is_Off, Heating_Should_Be_Off, Room_Occupied, "At 18:00 target (21°C), don't pre-cool for upcoming 21:00 lower target (16°C)")]
    [DataRow(Room.GamesRoom, "2025-11-23", "20:45", Current_Temp_17, Heating_Is_Off, Heating_Should_Be_On, Room_Occupied, "Still maintaining 18:00 schedule target of 21°C at 20:45")]
    // Exact schedule transition times
    [DataRow(Room.GamesRoom, "2025-11-23", "01:00", Current_Temp_16, Heating_Is_On, Heating_Should_Be_Off, Room_Occupied, "At exactly 01:00, new schedule (15°C) is now active, heating off")]
    [DataRow(Room.GamesRoom, "2025-11-23", "01:00", Current_Temp_14, Heating_Is_Off, Heating_Should_Be_On, Room_Occupied, "At exactly 01:00, new schedule (15°C) is now active, need heat")]
    [DataRow(Room.GamesRoom, "2025-11-23", "10:00", Current_Temp_19, Heating_Is_On, Heating_Should_Be_Off, Room_Occupied, "At exactly 10:00, target dropped to 15°C, turn off heating")]
    [DataRow(Room.GamesRoom, "2025-11-23", "10:00", Current_Temp_14, Heating_Is_Off, Heating_Should_Be_On, Room_Occupied, "At exactly 10:00, below new target of 15°C")]
    [DataRow(Room.GamesRoom, "2025-11-23", "21:00", Current_Temp_20, Heating_Is_On, Heating_Should_Be_Off, Room_Occupied, "At exactly 21:00, target dropped to 16°C, turn off heating")]
    // Just before ramp-up starts
    [DataRow(Room.GamesRoom, "2025-11-23", "00:29", Current_Temp_15, Heating_Is_Off, Heating_Should_Be_On, Room_Occupied, "1 minute before ramp-up for 01:00, still using 21:00 schedule (16°C)")]
    [DataRow(Room.GamesRoom, "2025-11-23", "05:29", Current_Temp_15, Heating_Is_Off, Heating_Should_Be_Off, Room_Occupied, "1 minute before ramp-up for 06:00, current schedule is 01:00 (15°C)")]
    // Pre-heating for higher upcoming target
    [DataRow(Room.GamesRoom, "2025-11-23", "05:45", Current_Temp_18, Heating_Is_Off, Heating_Should_Be_On, Room_Occupied, "Pre-heating for 06:00 schedule (20°C) from current 01:00 schedule (15°C)")]
    [DataRow(Room.GamesRoom, "2025-11-23", "05:45", Current_Temp_19, Heating_Is_Off, Heating_Should_Be_Off, Room_Occupied, "Pre-heating for 06:00, room occupied uses RoomInUse schedule (19°C), at target")]
    [DataRow(Room.GamesRoom, "2025-11-23", "05:45", Current_Temp_20, Heating_Is_Off, Heating_Should_Be_Off, Room_Occupied, "Pre-heating for 06:00 schedule (20°C), already at target")]
    [DataRow(Room.GamesRoom, "2025-11-23", "17:45", Current_Temp_18, Heating_Is_Off, Heating_Should_Be_On, Room_Occupied, "Pre-heating for 18:00 schedule (21°C) from 10:00 schedule (15°C)")]
    // Day boundary tests
    [DataRow(Room.GamesRoom, "2025-11-23", "00:01", Current_Temp_15, Heating_Is_Off, Heating_Should_Be_On, Room_Occupied, "After midnight, still using previous day's 21:00 schedule (16°C)")]
    [DataRow(Room.GamesRoom, "2025-11-24", "00:01", Current_Temp_15, Heating_Is_Off, Heating_Should_Be_On, Room_Occupied, "After midnight on Monday, still using previous day's 21:00 schedule (16°C)")]
    // Weekday vs weekend logic
    [DataRow(Room.GamesRoom, "2025-11-24", "07:00", Current_Temp_17, Heating_Is_Off, Heating_Should_Be_On, Room_Occupied, "Monday should use NotSunday schedule (18°C target)")]
    [DataRow(Room.GamesRoom, "2025-11-24", "07:00", Current_Temp_18, Heating_Is_Off, Heating_Should_Be_Off, Room_Occupied, "Monday at target for NotSunday schedule (18°C)")]
    [DataRow(Room.GamesRoom, "2025-11-24", "07:00", Current_Temp_22, Heating_Is_Off, Heating_Should_Be_Off, Room_Occupied, "Monday should NOT use Sunday schedule (22°C)")]
    [DataRow(Room.GamesRoom, "2025-11-25", "07:00", Current_Temp_17, Heating_Is_Off, Heating_Should_Be_On, Room_Occupied, "Tuesday should use NotSunday schedule (18°C target)")]
    [DataRow(Room.GamesRoom, "2025-11-26", "07:00", Current_Temp_17, Heating_Is_Off, Heating_Should_Be_On, Room_Occupied, "Wednesday should use NotSunday schedule (18°C target)")]
    [DataRow(Room.GamesRoom, "2025-11-27", "07:00", Current_Temp_17, Heating_Is_Off, Heating_Should_Be_On, Room_Occupied, "Thursday should use NotSunday schedule (18°C target)")]
    [DataRow(Room.GamesRoom, "2025-11-28", "07:00", Current_Temp_17, Heating_Is_Off, Heating_Should_Be_On, Room_Occupied, "Friday should use NotSunday schedule (18°C target)")]
    // After schedule drops
    [DataRow(Room.GamesRoom, "2025-11-23", "10:01", Current_Temp_19, Heating_Is_On, Heating_Should_Be_Off, Room_Occupied, "After 10:00, target dropped to 15°C, turn off")]
    [DataRow(Room.GamesRoom, "2025-11-23", "10:01", Current_Temp_14, Heating_Is_Off, Heating_Should_Be_On, Room_Occupied, "After 10:00, below target of 15°C, turn on")]
    [DataRow(Room.GamesRoom, "2025-11-23", "21:01", Current_Temp_20, Heating_Is_On, Heating_Should_Be_Off, Room_Occupied, "After 21:00, target dropped to 16°C from 21°C")]
    // Room occupied - RoomInUse condition applies
    [DataRow(Room.GamesRoom, "2025-11-24", "06:30", Current_Temp_18, Heating_Is_Off, Heating_Should_Be_On, Room_Occupied, "Room occupied at 06:30, RoomInUse schedule (19°C at 06:00) applies, needs heating")]
    [DataRow(Room.GamesRoom, "2025-11-24", "06:30", Current_Temp_19, Heating_Is_Off, Heating_Should_Be_Off, Room_Occupied, "Room occupied at 06:30, RoomInUse schedule (19°C) applies, at target")]
    [DataRow(Room.GamesRoom, "2025-11-24", "06:30", Current_Temp_20, Heating_Is_On, Heating_Should_Be_Off, Room_Occupied, "Room occupied at 06:30, RoomInUse schedule (19°C) applies, above target")]
    // Room unoccupied - RoomInUse condition does NOT apply, fallback to base 06:00 schedule
    [DataRow(Room.GamesRoom, "2025-11-24", "06:30", Current_Temp_19, Heating_Is_Off, Heating_Should_Be_On, Room_Unoccupied, "Room unoccupied at 06:30, RoomInUse doesn't apply, fallback to base 06:00 schedule (20°C)")]
    [DataRow(Room.GamesRoom, "2025-11-24", "06:30", Current_Temp_20, Heating_Is_Off, Heating_Should_Be_Off, Room_Unoccupied, "Room unoccupied at 06:30, at base 06:00 target (20°C)")]
    [DataRow(Room.GamesRoom, "2025-11-24", "06:30", Current_Temp_21, Heating_Is_On, Heating_Should_Be_Off, Room_Unoccupied, "Room unoccupied at 06:30, above base 06:00 target (20°C)")]
    // Room unoccupied on weekday morning - RoomNotInUse condition applies
    [DataRow(Room.GamesRoom, "2025-11-24", "07:30", Current_Temp_17, Heating_Is_Off, Heating_Should_Be_On, Room_Unoccupied, "Monday 07:30, room unoccupied, RoomNotInUse schedule (18°C at 07:00) applies")]
    [DataRow(Room.GamesRoom, "2025-11-24", "07:30", Current_Temp_18, Heating_Is_Off, Heating_Should_Be_Off, Room_Unoccupied, "Monday 07:30, room unoccupied, at RoomNotInUse target (18°C)")]
    [DataRow(Room.GamesRoom, "2025-11-25", "07:30", Current_Temp_17, Heating_Is_Off, Heating_Should_Be_On, Room_Unoccupied, "Tuesday 07:30, room unoccupied, RoomNotInUse schedule applies")]
    // Room occupied on weekday morning - RoomNotInUse does NOT apply, uses base schedule or RoomInUse
    [DataRow(Room.GamesRoom, "2025-11-24", "07:30", Current_Temp_18, Heating_Is_Off, Heating_Should_Be_Off, Room_Occupied, "Monday 07:30, room occupied, using 07:00 NotSunday base schedule (18°C), at target")]
    [DataRow(Room.GamesRoom, "2025-11-24", "07:30", Current_Temp_19, Heating_Is_Off, Heating_Should_Be_Off, Room_Occupied, "Monday 07:30, room occupied, above 07:00 NotSunday target (18°C)")]
    // Room unoccupied at 09:00 - RoomNotInUse condition applies
    [DataRow(Room.GamesRoom, "2025-11-24", "09:30", Current_Temp_15, Heating_Is_Off, Heating_Should_Be_On, Room_Unoccupied, "09:30, room unoccupied, RoomNotInUse schedule (16°C at 09:00) applies")]
    [DataRow(Room.GamesRoom, "2025-11-24", "09:30", Current_Temp_16, Heating_Is_Off, Heating_Should_Be_Off, Room_Unoccupied, "09:30, room unoccupied, at RoomNotInUse target (16°C)")]
    [DataRow(Room.GamesRoom, "2025-11-24", "09:30", Current_Temp_17, Heating_Is_On, Heating_Should_Be_Off, Room_Unoccupied, "09:30, room unoccupied, above RoomNotInUse target (16°C)")]
    // Room occupied at 09:00 - RoomNotInUse does NOT apply
    [DataRow(Room.GamesRoom, "2025-11-24", "09:30", Current_Temp_18, Heating_Is_Off, Heating_Should_Be_Off, Room_Occupied, "09:30, room occupied, RoomNotInUse doesn't apply, using 07:00 base schedule (18°C), at target")]
    [DataRow(Room.GamesRoom, "2025-11-24", "09:30", Current_Temp_19, Heating_Is_Off, Heating_Should_Be_Off, Room_Occupied, "09:30, room occupied, above 07:00 base target (18°C)")]
    // Evening - room unoccupied, RoomNotInUse 21:30 schedule applies
    [DataRow(Room.GamesRoom, "2025-11-24", "22:00", Current_Temp_13, Heating_Is_Off, Heating_Should_Be_On, Room_Unoccupied, "22:00, room unoccupied, RoomNotInUse schedule (14°C at 21:30) applies")]
    [DataRow(Room.GamesRoom, "2025-11-24", "22:00", Current_Temp_14, Heating_Is_Off, Heating_Should_Be_Off, Room_Unoccupied, "22:00, room unoccupied, at RoomNotInUse target (14°C)")]
    [DataRow(Room.GamesRoom, "2025-11-24", "22:00", Current_Temp_15, Heating_Is_On, Heating_Should_Be_Off, Room_Unoccupied, "22:00, room unoccupied, above RoomNotInUse target (14°C)")]
    // Evening - room occupied, RoomNotInUse does NOT apply, stays at 21:00 base schedule
    [DataRow(Room.GamesRoom, "2025-11-24", "22:00", Current_Temp_15, Heating_Is_Off, Heating_Should_Be_On, Room_Occupied, "22:00, room occupied, RoomNotInUse doesn't apply, maintain 21:00 schedule (16°C)")]
    [DataRow(Room.GamesRoom, "2025-11-24", "22:00", Current_Temp_16, Heating_Is_Off, Heating_Should_Be_Off, Room_Occupied, "22:00, room occupied, at 21:00 target (16°C)")]
    [DataRow(Room.GamesRoom, "2025-11-24", "22:00", Current_Temp_17, Heating_Is_On, Heating_Should_Be_Off, Room_Occupied, "22:00, room occupied, above 21:00 target (16°C)")]
    // Weekend - RoomNotInUse weekday schedule does NOT apply on Saturday
    [DataRow(Room.GamesRoom, "2025-11-22", "07:30", Current_Temp_17, Heating_Is_Off, Heating_Should_Be_On, Room_Unoccupied, "Saturday 07:30, room unoccupied, weekday RoomNotInUse doesn't apply, use Saturday 07:00 (18°C)")]
    [DataRow(Room.GamesRoom, "2025-11-22", "07:30", Current_Temp_18, Heating_Is_Off, Heating_Should_Be_Off, Room_Unoccupied, "Saturday 07:30, at Saturday target (18°C)")]
    // Kitchen test cases
    [DataRow(Room.Kitchen, "2025-11-24", "06:00", Current_Temp_18, Heating_Is_Off, Heating_Should_Be_On, Room_Occupied, "Kitchen at 06:00 weekday, target 19°C, needs heating")]
    [DataRow(Room.Kitchen, "2025-11-24", "06:00", Current_Temp_19, Heating_Is_Off, Heating_Should_Be_Off, Room_Occupied, "Kitchen at 06:00 weekday, at target 19°C")]
    [DataRow(Room.Kitchen, "2025-11-24", "12:00", Current_Temp_18, Heating_Is_Off, Heating_Should_Be_On, Room_Occupied, "Kitchen midday, target 18.5°C, needs heating")]
    [DataRow(Room.Kitchen, "2025-11-24", "12:00", Current_Temp_19, Heating_Is_Off, Heating_Should_Be_Off, Room_Occupied, "Kitchen midday, above target 18.5°C")]
    [DataRow(Room.Kitchen, "2025-11-24", "19:00", Current_Temp_18, Heating_Is_Off, Heating_Should_Be_On, Room_Occupied, "Kitchen evening 19:00, target 19°C, needs heating")]
    [DataRow(Room.Kitchen, "2025-11-24", "22:00", Current_Temp_17, Heating_Is_Off, Heating_Should_Be_Off, Room_Occupied, "Kitchen late evening, target 16°C, above target")]
    public async Task Test1(Room room, string date, string time, double? currentTemperature, bool currentHeatingState, bool expectedHeatingState, bool roomOccupied, string reason)
    {
        ServiceProvider serviceProvider = GetServiceProvider();

        FakeTimeProvider timeProvider = serviceProvider.GetRequiredService<FakeTimeProvider>();

        DateOnly d = DateOnly.ParseExact(date, "yyyy-MM-dd");
        TimeOnly t = TimeOnly.ParseExact(time, "HH:mm");

        timeProvider.SetSpecificDateTime(new DateTimeOffset(d.ToDateTime(t)));

        MockPresenceService presenceService = (MockPresenceService)serviceProvider.GetRequiredService<IPresenceService>();
        presenceService.SetRoomValue(room, roomOccupied);

        HeatingControlService sut = ActivatorUtilities.CreateInstance<HeatingControlService>(serviceProvider);

        FakeNamedEntities namedEntities = serviceProvider.GetRequiredService<FakeNamedEntities>();

        // Set temperature for the room being tested
        ICustomNumericSensorEntity temperatureSensor = room switch
        {
            Room.GamesRoom => namedEntities.GamesRoomDeskTemperature,
            Room.Kitchen => namedEntities.KitchenTemperature,
            Room.Bedroom1 => namedEntities.Bedroom1Temperature,
            Room.DiningRoom => namedEntities.DiningRoomClimateTemperature,
            _ => throw new NotSupportedException($"Room {room} not supported in tests")
        };
        ((FakeCustomNumericSensorEntity)temperatureSensor).State = currentTemperature;

        // Set heating state for the room being tested
        ICustomSwitchEntity heaterPlug = room switch
        {
            Room.GamesRoom => namedEntities.GamesRoomHeaterSmartPlugOnOff,
            Room.Kitchen => namedEntities.KitchenHeaterSmartPlugOnOff,
            Room.Bedroom1 => namedEntities.Bedroom1HeaterSmartPlugOnOff,
            Room.DiningRoom => namedEntities.DiningRoomHeaterSmartPlugOnOff,
            _ => throw new NotSupportedException($"Room {room} not supported in tests")
        };

        if (currentHeatingState)
        {
            heaterPlug.TurnOn();
        }
        else
        {
            heaterPlug.TurnOff();
        }

        await sut.EvaluateAllSchedules(GetSampleSchedule());

        if (expectedHeatingState == Heating_Should_Be_On)
        {
            heaterPlug.IsOn().ShouldBeTrue(reason);
        }
        else
        {
            heaterPlug.IsOff().ShouldBeTrue(reason);
        }
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
        services.AddSingleton<FakeNamedEntities>();
        services.AddSingleton<INamedEntities>(provider => provider.GetRequiredService<FakeNamedEntities>());
        services.AddSingleton<HeatingControlService>();
        services.AddSingleton<HomeAssistantConfiguration>();
        services.AddSingleton<FakeTimeProvider>();
        services.AddSingleton<TimeProvider>(provider => provider.GetRequiredService<FakeTimeProvider>());

        return services.BuildServiceProvider();
    }

    private List<RoomSchedule> GetSampleSchedule()
    {
        return [
            new RoomSchedule(){
                 Room = Room.GamesRoom,
                 Condition = () => true,
                 ScheduleTracks = [
                    new HeatingScheduleTrack(){
                          TargetTime = new TimeOnly(01,00),
                          Temperature = 15
                    },
                    new HeatingScheduleTrack(){
                          TargetTime = new TimeOnly(06,00),
                          Temperature = 19,
                          Conditions = ConditionType.RoomInUse
                    },
                    new HeatingScheduleTrack(){
                          TargetTime = new TimeOnly(06,00),
                          Temperature = 20
                    },
                    new HeatingScheduleTrack(){
                          TargetTime = new TimeOnly(07,00),
                          Temperature = 18,
                          Days = Days.Weekdays,
                          Conditions = ConditionType.RoomNotInUse
                    },
                    new HeatingScheduleTrack(){
                          TargetTime = new TimeOnly(07,00),
                          Temperature = 22,
                          Days = Days.Sunday
                    },
                    new HeatingScheduleTrack(){
                          TargetTime = new TimeOnly(07,00),
                          Temperature = 18,
                          Days = Days.NotSunday
                    },
                    new HeatingScheduleTrack(){
                          TargetTime = new TimeOnly(09,00),
                          Temperature = 16,
                          Conditions = ConditionType.RoomNotInUse
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
                    },
                    new HeatingScheduleTrack(){
                          TargetTime = new TimeOnly(21,30),
                          Temperature = 14,
                          Conditions = ConditionType.RoomNotInUse
                    }
                ]
            },
            new RoomSchedule(){
                 Room = Room.Kitchen,
                 Condition = () => true,
                 ScheduleTracks = [
                    new HeatingScheduleTrack(){
                          TargetTime = new TimeOnly(06,00),
                          Temperature = 19
                    },
                    new HeatingScheduleTrack(){
                          TargetTime = new TimeOnly(06,30),
                          Temperature = 18.5
                    },
                    new HeatingScheduleTrack(){
                          TargetTime = new TimeOnly(18,00),
                          Temperature = 19
                    },
                    new HeatingScheduleTrack(){
                          TargetTime = new TimeOnly(21,30),
                          Temperature = 16
                    }
                ]
            }
        ];
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
using HomeAssistant.apps;
using HomeAssistant.Devices.Batteries;
using HomeAssistant.Devices.Meters;
using HomeAssistant.Services;
using HomeAssistant.Services.Climate;
using HomeAssistant.Shared.Climate;
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

    // Hysteresis offset from HeatingControlService
    private const double Hysteresis = HeatingControlService.HysteresisOffset;

    [TestMethod]
    // GamesRoom test cases
    [DataRow("Games room", "2025-11-23", "00:59", Current_Temp_15, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "Schedule 15 in 1 minute, but was scheduled 16 a few hours ago and should still be active")]
    [DataRow("Games room", "2025-11-23", "00:00", Current_Temp_16, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Schedule 15 in 1 hour, already warm enough")]
    [DataRow("Games room", "2025-11-23", "00:00", Current_Temp_15, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "Schedule 16 at 9pm, not yet warm enough")]
    [DataRow("Games room", "2025-11-23", "00:45", Current_Temp_14, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "Schedule 15 in 15 minutes, not already warm enough, should be ramping up")]
    [DataRow("Games room", "2025-11-23", "07:00", Current_Temp_17, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "Target is 22 on Sundays, 18 every other day - this is Sunday")]
    [DataRow("Games room", "2025-11-23", "07:00", Current_Temp_18, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "Target is 22 on Sundays, 18 every other day - this is Sunday")]
    [DataRow("Games room", "2025-11-23", "07:00", Current_Temp_22, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Target is 22 on Sundays, 18 every other day - this is Sunday")]
    [DataRow("Games room", "2025-11-23", "07:00", Current_Temp_23, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Target is 22 on Sundays, 18 every other day - this is Sunday")]
    [DataRow("Games room", "2025-11-23", "07:00", Current_Temp_23, Heating_Is_On, Room_Occupied, Heating_Should_Be_Off, "Target is 22 on Sundays, 18 every other day - this is Sunday")]
    [DataRow("Games room", "2025-11-22", "07:00", Current_Temp_17, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "Target is 22 on Sundays, 18 every other day - this is Saturday")]
    [DataRow("Games room", "2025-11-22", "07:00", Current_Temp_18, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Target is 22 on Sundays, 18 every other day - this is Saturday")]
    [DataRow("Games room", "2025-11-22", "07:00", Current_Temp_22, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Target is 22 on Sundays, 18 every other day - this is Saturday")]
    [DataRow("Games room", "2025-11-22", "07:00", Current_Temp_23, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Target is 22 on Sundays, 18 every other day - this is Saturday")]
    [DataRow("Games room", "2025-11-22", "07:00", Current_Temp_23, Heating_Is_On, Room_Occupied, Heating_Should_Be_Off, "Target is 22 on Sundays, 18 every other day - this is Saturday")]
    // No pre-cooling tests
    [DataRow("Games room", "2025-11-23", "00:45", Current_Temp_16, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "At target temp from 21:00 schedule, don't pre-cool for lower upcoming 01:00 schedule")]
    [DataRow("Games room", "2025-11-23", "20:45", Current_Temp_20, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "Don't pre-cool for upcoming 21:00 lower target (16°C), maintain 18:00 target (21°C), heating on to reach 21°C")]
    [DataRow("Games room", "2025-11-23", "20:45", Current_Temp_21, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "At 18:00 target (21°C), don't pre-cool for upcoming 21:00 lower target (16°C)")]
    [DataRow("Games room", "2025-11-23", "20:45", Current_Temp_17, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "Still maintaining 18:00 schedule target of 21°C at 20:45")]
    // Exact schedule transition times
    [DataRow("Games room", "2025-11-23", "01:00", Current_Temp_16, Heating_Is_On, Room_Occupied, Heating_Should_Be_Off, "At exactly 01:00, new schedule (15°C) is now active, heating off")]
    [DataRow("Games room", "2025-11-23", "01:00", Current_Temp_14, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "At exactly 01:00, new schedule (15°C) is now active, need heat")]
    [DataRow("Games room", "2025-11-23", "10:00", Current_Temp_19, Heating_Is_On, Room_Occupied, Heating_Should_Be_Off, "At exactly 10:00, target dropped to 15°C, turn off heating")]
    [DataRow("Games room", "2025-11-23", "10:00", Current_Temp_14, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "At exactly 10:00, below new target of 15°C")]
    [DataRow("Games room", "2025-11-23", "21:00", Current_Temp_20, Heating_Is_On, Room_Occupied, Heating_Should_Be_Off, "At exactly 21:00, target dropped to 16°C, turn off heating")]
    // Just before ramp-up starts
    [DataRow("Games room", "2025-11-23", "00:29", Current_Temp_15, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "1 minute before ramp-up for 01:00, still using 21:00 schedule (16°C)")]
    [DataRow("Games room", "2025-11-23", "05:29", Current_Temp_15, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "1 minute before ramp-up for 06:00, current schedule is 01:00 (15°C)")]
    // Pre-heating for higher upcoming target
    [DataRow("Games room", "2025-11-23", "05:45", Current_Temp_18, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "Pre-heating for 06:00 schedule (20°C) from current 01:00 schedule (15°C)")]
    [DataRow("Games room", "2025-11-23", "05:45", Current_Temp_19, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Pre-heating for 06:00, room occupied uses RoomInUse schedule (19°C), at target")]
    [DataRow("Games room", "2025-11-23", "05:45", Current_Temp_20, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Pre-heating for 06:00 schedule (20°C), already at target")]
    [DataRow("Games room", "2025-11-23", "17:45", Current_Temp_18, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "Pre-heating for 18:00 schedule (21°C) from 10:00 schedule (15°C)")]
    // Day boundary tests
    [DataRow("Games room", "2025-11-23", "00:01", Current_Temp_15, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "After midnight, still using previous day's 21:00 schedule (16°C)")]
    [DataRow("Games room", "2025-11-24", "00:01", Current_Temp_15, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "After midnight on Monday, still using previous day's 21:00 schedule (16°C)")]
    // Weekday vs weekend logic
    [DataRow("Games room", "2025-11-24", "07:00", Current_Temp_17, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "Monday should use NotSunday schedule (18°C target)")]
    [DataRow("Games room", "2025-11-24", "07:00", Current_Temp_18, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Monday at target for NotSunday schedule (18°C)")]
    [DataRow("Games room", "2025-11-24", "07:00", Current_Temp_22, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Monday should NOT use Sunday schedule (22°C)")]
    [DataRow("Games room", "2025-11-25", "07:00", Current_Temp_17, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "Tuesday should use NotSunday schedule (18°C target)")]
    [DataRow("Games room", "2025-11-26", "07:00", Current_Temp_17, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "Wednesday should use NotSunday schedule (18°C target)")]
    [DataRow("Games room", "2025-11-27", "07:00", Current_Temp_17, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "Thursday should use NotSunday schedule (18°C target)")]
    [DataRow("Games room", "2025-11-28", "07:00", Current_Temp_17, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "Friday should use NotSunday schedule (18°C target)")]
    // After schedule drops
    [DataRow("Games room", "2025-11-23", "10:01", Current_Temp_19, Heating_Is_On, Room_Occupied, Heating_Should_Be_Off, "After 10:00, target dropped to 15°C, turn off")]
    [DataRow("Games room", "2025-11-23", "10:01", Current_Temp_14, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "After 10:00, below target of 15°C, turn on")]
    [DataRow("Games room", "2025-11-23", "21:01", Current_Temp_20, Heating_Is_On, Room_Occupied, Heating_Should_Be_Off, "After 21:00, target dropped to 16°C from 21°C")]
    // Room occupied - RoomInUse condition applies
    [DataRow("Games room", "2025-11-24", "06:30", Current_Temp_18, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "Room occupied at 06:30, RoomInUse schedule (19°C at 06:00) applies, needs heating")]
    [DataRow("Games room", "2025-11-24", "06:30", Current_Temp_19, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Room occupied at 06:30, RoomInUse schedule (19°C) applies, at target")]
    [DataRow("Games room", "2025-11-24", "06:30", Current_Temp_20, Heating_Is_On, Room_Occupied, Heating_Should_Be_Off, "Room occupied at 06:30, RoomInUse schedule (19°C) applies, above target")]
    // Room unoccupied - RoomInUse condition does NOT apply, fallback to base 06:00 schedule
    [DataRow("Games room", "2025-11-24", "06:30", Current_Temp_19, Heating_Is_Off, Room_Unoccupied, Heating_Should_Be_On, "Room unoccupied at 06:30, RoomInUse doesn't apply, fallback to base 06:00 schedule (20°C)")]
    [DataRow("Games room", "2025-11-24", "06:30", Current_Temp_20, Heating_Is_Off, Room_Unoccupied, Heating_Should_Be_Off, "Room unoccupied at 06:30, at base 06:00 target (20°C)")]
    [DataRow("Games room", "2025-11-24", "06:30", Current_Temp_21, Heating_Is_On, Room_Unoccupied, Heating_Should_Be_Off, "Room unoccupied at 06:30, above base 06:00 target (20°C)")]
    // Room unoccupied on weekday morning - RoomNotInUse condition applies
    [DataRow("Games room", "2025-11-24", "07:30", Current_Temp_17, Heating_Is_Off, Room_Unoccupied, Heating_Should_Be_On, "Monday 07:30, room unoccupied, RoomNotInUse schedule (18°C at 07:00) applies")]
    [DataRow("Games room", "2025-11-24", "07:30", Current_Temp_18, Heating_Is_Off, Room_Unoccupied, Heating_Should_Be_Off, "Monday 07:30, room unoccupied, at RoomNotInUse target (18°C)")]
    [DataRow("Games room", "2025-11-25", "07:30", Current_Temp_17, Heating_Is_Off, Room_Unoccupied, Heating_Should_Be_On, "Tuesday 07:30, room unoccupied, RoomNotInUse schedule applies")]
    // Room occupied on weekday morning - RoomNotInUse does NOT apply, uses base schedule or RoomInUse
    [DataRow("Games room", "2025-11-24", "07:30", Current_Temp_18, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Monday 07:30, room occupied, using 07:00 NotSunday base schedule (18°C), at target")]
    [DataRow("Games room", "2025-11-24", "07:30", Current_Temp_19, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Monday 07:30, room occupied, above 07:00 NotSunday target (18°C)")]
    // Room unoccupied at 09:00 - RoomNotInUse condition applies
    [DataRow("Games room", "2025-11-24", "09:30", Current_Temp_15, Heating_Is_Off, Room_Unoccupied, Heating_Should_Be_On, "09:30, room unoccupied, RoomNotInUse schedule (16°C at 09:00) applies")]
    [DataRow("Games room", "2025-11-24", "09:30", Current_Temp_16, Heating_Is_Off, Room_Unoccupied, Heating_Should_Be_Off, "09:30, room unoccupied, at RoomNotInUse target (16°C)")]
    [DataRow("Games room", "2025-11-24", "09:30", Current_Temp_17, Heating_Is_On, Room_Unoccupied, Heating_Should_Be_Off, "09:30, room unoccupied, above RoomNotInUse target (16°C)")]
    // Room occupied at 09:00 - RoomNotInUse does NOT apply
    [DataRow("Games room", "2025-11-24", "09:30", Current_Temp_18, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "09:30, room occupied, RoomNotInUse doesn't apply, using 07:00 base schedule (18°C), at target")]
    [DataRow("Games room", "2025-11-24", "09:30", Current_Temp_19, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "09:30, room occupied, above 07:00 base target (18°C)")]
    // Evening - room unoccupied, RoomNotInUse 21:30 schedule applies
    [DataRow("Games room", "2025-11-24", "22:00", Current_Temp_13, Heating_Is_Off, Room_Unoccupied, Heating_Should_Be_On, "22:00, room unoccupied, RoomNotInUse schedule (14°C at 21:30) applies")]
    [DataRow("Games room", "2025-11-24", "22:00", Current_Temp_14, Heating_Is_Off, Room_Unoccupied, Heating_Should_Be_Off, "22:00, room unoccupied, at RoomNotInUse target (14°C)")]
    [DataRow("Games room", "2025-11-24", "22:00", Current_Temp_15, Heating_Is_On, Room_Unoccupied, Heating_Should_Be_Off, "22:00, room unoccupied, above RoomNotInUse target (14°C)")]
    // Evening - room occupied, RoomNotInUse does NOT apply, stays at 21:00 base schedule
    [DataRow("Games room", "2025-11-24", "22:00", Current_Temp_15, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "22:00, room occupied, RoomNotInUse doesn't apply, maintain 21:00 schedule (16°C)")]
    [DataRow("Games room", "2025-11-24", "22:00", Current_Temp_16, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "22:00, room occupied, at 21:00 target (16°C)")]
    [DataRow("Games room", "2025-11-24", "22:00", Current_Temp_17, Heating_Is_On, Room_Occupied, Heating_Should_Be_Off, "22:00, room occupied, above 21:00 target (16°C)")]
    // Weekend - RoomNotInUse weekday schedule does NOT apply on Saturday
    [DataRow("Games room", "2025-11-22", "07:30", Current_Temp_17, Heating_Is_Off, Room_Unoccupied, Heating_Should_Be_On, "Saturday 07:30, room unoccupied, weekday RoomNotInUse doesn't apply, use Saturday 07:00 (18°C)")]
    [DataRow("Games room", "2025-11-22", "07:30", Current_Temp_18, Heating_Is_Off, Room_Unoccupied, Heating_Should_Be_Off, "Saturday 07:30, at Saturday target (18°C)")]
    // Kitchen test cases
    [DataRow("Kitchen", "2025-11-24", "06:00", Current_Temp_18, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "Kitchen at 06:00 weekday, target 19°C, needs heating")]
    [DataRow("Kitchen", "2025-11-24", "06:00", Current_Temp_19, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Kitchen at 06:00 weekday, at target 19°C")]
    [DataRow("Kitchen", "2025-11-24", "12:00", Current_Temp_18, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "Kitchen midday, target 18.5°C, needs heating")]
    [DataRow("Kitchen", "2025-11-24", "12:00", Current_Temp_19, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Kitchen midday, above target 18.5°C")]
    [DataRow("Kitchen", "2025-11-24", "19:00", Current_Temp_18, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "Kitchen evening 19:00, target 19°C, needs heating")]
    [DataRow("Kitchen", "2025-11-24", "22:00", Current_Temp_17, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Kitchen late evening, target 16°C, above target")]
    // Hysteresis/anti-flapping test cases
    [DataRow("Games room", "2025-11-23", "07:00", 22.0, Heating_Is_On, Room_Occupied, Heating_Should_Be_On, "Hysteresis: Target 22°C, at exact target with heating ON, stays ON until 22.2°C")]
    [DataRow("Games room", "2025-11-23", "07:00", 22.1, Heating_Is_On, Room_Occupied, Heating_Should_Be_On, "Hysteresis: Target 22°C, at 22.1°C with heating ON, stays ON until 22.2°C")]
    [DataRow("Games room", "2025-11-23", "07:00", 22.2, Heating_Is_On, Room_Occupied, Heating_Should_Be_Off, "Hysteresis: Target 22°C, at 22.2°C with heating ON, turns OFF")]
    [DataRow("Games room", "2025-11-23", "07:00", 22.0, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Hysteresis: Target 22°C, at exact target with heating OFF, stays OFF until 21.8°C")]
    [DataRow("Games room", "2025-11-23", "07:00", 21.9, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Hysteresis: Target 22°C, at 21.9°C with heating OFF, stays OFF until 21.8°C")]
    [DataRow("Games room", "2025-11-23", "07:00", 21.8, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "Hysteresis: Target 22°C, at 21.8°C with heating OFF, turns ON")]
    [DataRow("Kitchen", "2025-11-24", "19:00", 19.0, Heating_Is_On, Room_Occupied, Heating_Should_Be_On, "Hysteresis: Kitchen target 19°C, at exact target with heating ON, stays ON")]
    [DataRow("Kitchen", "2025-11-24", "19:00", 19.0, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Hysteresis: Kitchen target 19°C, at exact target with heating OFF, stays OFF")]
    [DataRow("Kitchen", "2025-11-24", "19:00", 19.2, Heating_Is_On, Room_Occupied, Heating_Should_Be_Off, "Hysteresis: Kitchen target 19°C, at 19.2°C with heating ON, turns OFF")]
    [DataRow("Kitchen", "2025-11-24", "19:00", 18.8, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "Hysteresis: Kitchen target 19°C, at 18.8°C with heating OFF, turns ON")]
    // GMT to BST transition tests (Spring forward: 1:00 AM GMT becomes 2:00 AM BST on last Sunday of March)
    [DataRow("Games room", "2025-03-30", "00:30", Current_Temp_15, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "DST Spring: 30 mins before clocks go forward, 01:00 schedule (15°C) active, need heating")]
    [DataRow("Games room", "2025-03-30", "00:30", Current_Temp_16, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Spring: 30 mins before clocks go forward, above 01:00 target (15°C)")]
    [DataRow("Games room", "2025-03-30", "02:30", Current_Temp_14, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "DST Spring: After clocks went forward, 01:00 schedule (15°C) should still be active")]
    [DataRow("Games room", "2025-03-30", "02:30", Current_Temp_16, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Spring: After clocks went forward, above 01:00 target (15°C)")]
    [DataRow("Games room", "2025-03-30", "05:30", Current_Temp_18, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "DST Spring: Pre-heating for 06:00 schedule after DST transition")]
    [DataRow("Games room", "2025-03-30", "06:30", Current_Temp_19, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "DST Spring: Sunday 06:30, pre-heating for 07:00 schedule (22°C)")]
    [DataRow("Games room", "2025-03-30", "06:30", Current_Temp_20, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "DST Spring: Sunday 06:30, pre-heating for 07:00 (22°C), needs heating")]
    [DataRow("Games room", "2025-03-30", "06:30", Current_Temp_22, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Spring: Sunday 06:30, at pre-heat target (22°C)")]
    [DataRow("Kitchen", "2025-03-30", "00:30", Current_Temp_17, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Spring: Kitchen before transition, 21:30 schedule (16°C) from previous day active")]
    [DataRow("Kitchen", "2025-03-30", "02:30", Current_Temp_17, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Spring: Kitchen after transition, still using 21:30 schedule from previous day")]
    // BST to GMT transition tests (Fall back: 2:00 AM BST becomes 1:00 AM GMT on last Sunday of October)
    [DataRow("Games room", "2025-10-26", "00:30", Current_Temp_15, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "DST Fall: 30 mins before clocks go back, 01:00 schedule (15°C) not yet active, using 21:00 (16°C)")]
    [DataRow("Games room", "2025-10-26", "00:30", Current_Temp_16, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Fall: Before clocks go back, at 21:00 target (16°C)")]
    [DataRow("Games room", "2025-10-26", "01:30", Current_Temp_15, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Fall: During repeated hour, 01:00 schedule (15°C) should be active, at target")]
    [DataRow("Games room", "2025-10-26", "01:30", Current_Temp_14, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "DST Fall: During repeated hour, below 01:00 target (15°C)")]
    [DataRow("Games room", "2025-10-26", "02:30", Current_Temp_15, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Fall: After clocks went back, 01:00 schedule (15°C) active")]
    [DataRow("Games room", "2025-10-26", "06:30", Current_Temp_19, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "DST Fall: Sunday 06:30, pre-heating for 07:00 schedule (22°C)")]
    [DataRow("Games room", "2025-10-26", "06:30", Current_Temp_20, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "DST Fall: Sunday 06:30, pre-heating for 07:00 (22°C), needs heating")]
    [DataRow("Games room", "2025-10-26", "06:30", Current_Temp_22, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Fall: Sunday 06:30, at pre-heat target (22°C)")]
    [DataRow("Kitchen", "2025-10-26", "01:30", Current_Temp_17, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Fall: Kitchen during repeated hour, still on 21:30 schedule (16°C)")]
    [DataRow("Kitchen", "2025-10-26", "06:30", Current_Temp_18, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "DST Fall: Kitchen morning after DST transition, 06:00 schedule (19°C) active")]
    // Edge case: Schedules that would fall in the missing hour during spring forward
    [DataRow("Games room", "2025-03-30", "00:15", Current_Temp_18, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Spring: Before transition, 21:00 schedule (16°C) active, above target")]
    [DataRow("Games room", "2025-03-30", "00:15", Current_Temp_15, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "DST Spring: Before transition, below 21:00 target (16°C)")]
    [DataRow("Games room", "2025-03-30", "03:00", Current_Temp_15, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Spring: Well after transition, 01:00 schedule (15°C) still active")]
    // Heating state transitions around DST changes
    [DataRow("Games room", "2025-03-30", "00:55", Current_Temp_20, Heating_Is_On, Room_Occupied, Heating_Should_Be_Off, "DST Spring: 5 mins before spring forward, above 21:00 target (16°C), turn off")]
    [DataRow("Games room", "2025-10-26", "00:55", Current_Temp_15, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "DST Fall: 5 mins before fall back, below 21:00 target (16°C), turn on")]
    public async Task Test1(string room, string date, string time, double? currentTemperature, bool currentHeatingState, bool roomOccupied, bool expectedHeatingState, string reason)
    {
        ServiceProvider serviceProvider = GetServiceProvider();

        FakeTimeProvider timeProvider = serviceProvider.GetRequiredService<FakeTimeProvider>();

        DateOnly d = DateOnly.ParseExact(date, "yyyy-MM-dd");
        TimeOnly t = TimeOnly.ParseExact(time, "HH:mm");

        timeProvider.SetSpecificDateTime(new DateTimeOffset(d.ToDateTime(t)));

        MockPresenceService presenceService = (MockPresenceService)serviceProvider.GetRequiredService<IPresenceService>();
        presenceService.SetRoomValue(room, roomOccupied);

        // Configure the mock schedule persistence service to return test schedules
        ISchedulePersistenceService schedulePersistence = serviceProvider.GetRequiredService<ISchedulePersistenceService>();
        schedulePersistence.GetSchedulesAsync().Returns(Task.FromResult(new RoomSchedules { Rooms = GetSampleSchedule() }));

        HeatingControlService sut = ActivatorUtilities.CreateInstance<HeatingControlService>(serviceProvider);

        FakeNamedEntities namedEntities = serviceProvider.GetRequiredService<FakeNamedEntities>();

        // Set temperature for the room being tested
        ICustomNumericSensorEntity temperatureSensor = room switch
        {
            "Games room" => namedEntities.GamesRoomDeskTemperature,
            "Kitchen" => namedEntities.KitchenTemperature,
            "Bedroom 1" => namedEntities.Bedroom1Temperature,
            "Dining room" => namedEntities.DiningRoomClimateTemperature,
            _ => throw new NotSupportedException($"Room {room} not supported in tests")
        };
        ((FakeCustomNumericSensorEntity)temperatureSensor).State = currentTemperature;

        // Set heating state for the room being tested
        ICustomSwitchEntity heaterPlug = room switch
        {
            "Games room" => namedEntities.GamesRoomHeaterSmartPlugOnOff,
            "Kitchen" => namedEntities.KitchenHeaterSmartPlugOnOff,
            "Bedroom 1" => namedEntities.Bedroom1HeaterSmartPlugOnOff,
            "Dining room" => namedEntities.DiningRoomHeaterSmartPlugOnOff,
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

        await sut.EvaluateAllSchedules("unit test");

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
        services.AddSingleton(Substitute.For<ILogger<ISchedulePersistenceService>>());
        services.AddSingleton(Substitute.For<ISchedulePersistenceService>());
        services.AddSingleton(Substitute.For<IRoomStatePersistenceService>());

        return services.BuildServiceProvider();
    }

    private List<RoomSchedule> GetSampleSchedule()
    {
        return [
            new RoomSchedule(){
                 Name ="Games room",
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
                 Name = "Kitchen",
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
    private readonly Dictionary<string, bool> _presenceValue = [];

    public void SetRoomValue(string roomName, bool value)
    {
        _presenceValue[roomName] = value;
    }

    public Task<bool> IsRoomInUse(string roomName)
    {
        if (_presenceValue.TryGetValue(roomName, out bool result))
        {
            return Task.FromResult(result);
        }

        return Task.FromResult(false);
    }

    public bool CanDetectIfRoomInUse(string roomName)
    {
        return _presenceValue.ContainsKey(roomName);
    }
}

internal class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _specificDateTime;

    public void SetSpecificDateTime(DateTimeOffset specificDateTime) => _specificDateTime = specificDateTime;

    public override DateTimeOffset GetUtcNow() => _specificDateTime;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.FindSystemTimeZoneById("GMT");
}
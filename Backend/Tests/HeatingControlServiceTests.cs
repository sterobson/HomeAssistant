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
    [DataRow("Games room", "2025-11-23", "00:59", Current_Temp_15, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Games room", "2025-11-23", "00:00", Current_Temp_16, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Games room", "2025-11-23", "00:00", Current_Temp_15, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Games room", "2025-11-23", "00:45", Current_Temp_14, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Outside allowed hours (7-21), plug won't turn on")]
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
    [DataRow("Games room", "2025-11-23", "01:00", Current_Temp_16, Heating_Is_On, Room_Occupied, Heating_Should_Be_Off, "Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Games room", "2025-11-23", "01:00", Current_Temp_14, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Games room", "2025-11-23", "10:00", Current_Temp_19, Heating_Is_On, Room_Occupied, Heating_Should_Be_Off, "At exactly 10:00, target dropped to 15°C, turn off heating")]
    [DataRow("Games room", "2025-11-23", "10:00", Current_Temp_14, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "At exactly 10:00, below new target of 15°C")]
    [DataRow("Games room", "2025-11-23", "21:00", Current_Temp_20, Heating_Is_On, Room_Occupied, Heating_Should_Be_Off, "Outside allowed hours (7-21), plug won't turn on")]
    // Just before ramp-up starts
    [DataRow("Games room", "2025-11-23", "00:29", Current_Temp_15, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Games room", "2025-11-23", "05:29", Current_Temp_15, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Outside allowed hours (7-21), plug won't turn on")]
    // Pre-heating for higher upcoming target
    [DataRow("Games room", "2025-11-23", "05:45", Current_Temp_18, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Outside allowed hours (7-21), plug won't turn on - no pre-heating before 7am")]
    [DataRow("Games room", "2025-11-23", "05:45", Current_Temp_19, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Games room", "2025-11-23", "05:45", Current_Temp_20, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Games room", "2025-11-23", "17:45", Current_Temp_18, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "Pre-heating for 18:00 schedule (21°C) from 10:00 schedule (15°C)")]
    // Day boundary tests
    [DataRow("Games room", "2025-11-23", "00:01", Current_Temp_15, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Games room", "2025-11-24", "00:01", Current_Temp_15, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Outside allowed hours (7-21), plug won't turn on")]
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
    [DataRow("Games room", "2025-11-23", "21:01", Current_Temp_20, Heating_Is_On, Room_Occupied, Heating_Should_Be_Off, "Outside allowed hours (7-21), plug won't turn on")]
    // Room occupied - RoomInUse condition applies (but 06:30 is outside allowed hours)
    [DataRow("Games room", "2025-11-24", "06:30", Current_Temp_18, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Games room", "2025-11-24", "06:30", Current_Temp_19, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Games room", "2025-11-24", "06:30", Current_Temp_20, Heating_Is_On, Room_Occupied, Heating_Should_Be_Off, "Outside allowed hours (7-21), plug won't turn on")]
    // Room unoccupied - RoomInUse condition does NOT apply (but 06:30 is outside allowed hours)
    [DataRow("Games room", "2025-11-24", "06:30", Current_Temp_19, Heating_Is_Off, Room_Unoccupied, Heating_Should_Be_Off, "Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Games room", "2025-11-24", "06:30", Current_Temp_20, Heating_Is_Off, Room_Unoccupied, Heating_Should_Be_Off, "Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Games room", "2025-11-24", "06:30", Current_Temp_21, Heating_Is_On, Room_Unoccupied, Heating_Should_Be_Off, "Outside allowed hours (7-21), plug won't turn on")]
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
    // Evening - room unoccupied (22:00 is outside allowed hours)
    [DataRow("Games room", "2025-11-24", "22:00", Current_Temp_13, Heating_Is_Off, Room_Unoccupied, Heating_Should_Be_Off, "Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Games room", "2025-11-24", "22:00", Current_Temp_14, Heating_Is_Off, Room_Unoccupied, Heating_Should_Be_Off, "Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Games room", "2025-11-24", "22:00", Current_Temp_15, Heating_Is_On, Room_Unoccupied, Heating_Should_Be_Off, "Outside allowed hours (7-21), plug won't turn on")]
    // Evening - room occupied (22:00 is outside allowed hours)
    [DataRow("Games room", "2025-11-24", "22:00", Current_Temp_15, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Games room", "2025-11-24", "22:00", Current_Temp_16, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Games room", "2025-11-24", "22:00", Current_Temp_17, Heating_Is_On, Room_Occupied, Heating_Should_Be_Off, "Outside allowed hours (7-21), plug won't turn on")]
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
    // Bedroom 1 - Two device tests (smart plug + climate control)
    [DataRow("Bedroom 1", "2025-11-24", "08:00", Current_Temp_17, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "Bedroom 1 with two devices: Both off, temp below target, both should turn on")]
    [DataRow("Bedroom 1", "2025-11-24", "08:00", Current_Temp_18, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Bedroom 1 with two devices: Both off, at target, both should stay off")]
    [DataRow("Bedroom 1", "2025-11-24", "08:00", Current_Temp_19, Heating_Is_On, Room_Occupied, Heating_Should_Be_Off, "Bedroom 1 with two devices: Both on, above target + hysteresis, both should turn off")]
    [DataRow("Bedroom 1", "2025-11-24", "08:00", 18.2, Heating_Is_On, Room_Occupied, Heating_Should_Be_Off, "Bedroom 1 with two devices: Exactly at target + hysteresis (18.2°C), turn off")]
    [DataRow("Bedroom 1", "2025-11-24", "08:00", 17.8, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "Bedroom 1 with two devices: Exactly at target - hysteresis (17.8°C), turn on")]
    [DataRow("Bedroom 1", "2025-11-24", "23:00", Current_Temp_15, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "Bedroom 1 evening schedule: 22:00 target is 16°C, needs heating")]
    [DataRow("Bedroom 1", "2025-11-24", "23:00", Current_Temp_16, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "Bedroom 1 evening schedule: At 16°C target")]
    // GMT to BST transition tests (Spring forward: 1:00 AM GMT becomes 2:00 AM BST on last Sunday of March)
    [DataRow("Games room", "2025-03-30", "00:30", Current_Temp_15, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Spring: Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Games room", "2025-03-30", "00:30", Current_Temp_16, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Spring: Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Games room", "2025-03-30", "02:30", Current_Temp_14, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Spring: Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Games room", "2025-03-30", "02:30", Current_Temp_16, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Spring: Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Games room", "2025-03-30", "05:30", Current_Temp_18, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Spring: Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Games room", "2025-03-30", "06:30", Current_Temp_19, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Spring: Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Games room", "2025-03-30", "06:30", Current_Temp_20, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Spring: Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Games room", "2025-03-30", "06:30", Current_Temp_22, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Spring: Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Kitchen", "2025-03-30", "00:30", Current_Temp_17, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Spring: Kitchen before transition, 21:30 schedule (16°C) from previous day active")]
    [DataRow("Kitchen", "2025-03-30", "02:30", Current_Temp_17, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Spring: Kitchen after transition, still using 21:30 schedule from previous day")]
    // BST to GMT transition tests (Fall back: 2:00 AM BST becomes 1:00 AM GMT on last Sunday of October)
    [DataRow("Games room", "2025-10-26", "00:30", Current_Temp_15, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Fall: Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Games room", "2025-10-26", "00:30", Current_Temp_16, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Fall: Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Games room", "2025-10-26", "01:30", Current_Temp_15, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Fall: Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Games room", "2025-10-26", "01:30", Current_Temp_14, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Fall: Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Games room", "2025-10-26", "02:30", Current_Temp_15, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Fall: Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Games room", "2025-10-26", "06:30", Current_Temp_19, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Fall: Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Games room", "2025-10-26", "06:30", Current_Temp_20, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Fall: Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Games room", "2025-10-26", "06:30", Current_Temp_22, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Fall: Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Kitchen", "2025-10-26", "01:30", Current_Temp_17, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Fall: Kitchen during repeated hour, still on 21:30 schedule (16°C)")]
    [DataRow("Kitchen", "2025-10-26", "06:30", Current_Temp_18, Heating_Is_Off, Room_Occupied, Heating_Should_Be_On, "DST Fall: Kitchen morning after DST transition, 06:00 schedule (19°C) active")]
    // Edge case: Schedules that would fall in the missing hour during spring forward
    [DataRow("Games room", "2025-03-30", "00:15", Current_Temp_18, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Spring: Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Games room", "2025-03-30", "00:15", Current_Temp_15, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Spring: Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Games room", "2025-03-30", "03:00", Current_Temp_15, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Spring: Outside allowed hours (7-21), plug won't turn on")]
    // Heating state transitions around DST changes
    [DataRow("Games room", "2025-03-30", "00:55", Current_Temp_20, Heating_Is_On, Room_Occupied, Heating_Should_Be_Off, "DST Spring: Outside allowed hours (7-21), plug won't turn on")]
    [DataRow("Games room", "2025-10-26", "00:55", Current_Temp_15, Heating_Is_Off, Room_Occupied, Heating_Should_Be_Off, "DST Fall: Outside allowed hours (7-21), plug won't turn on")]
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

        FakeNamedEntities namedEntities = serviceProvider.GetRequiredService<FakeNamedEntities>();

        // Set up house presence by configuring desk plug power
        // The HousePresenceSensor checks if desk power > 30 for either games room or dining room
        // "House presence" (someone is home) is different from "room occupied" (specific room in use)
        // Use dining room desk for house presence so it works even when games room is unoccupied
        ((FakeCustomSwitchEntity)namedEntities.DiningRoomDeskPlugOnOff).TurnOn();
        ((FakeCustomNumericSensorEntity)namedEntities.DiningRoomDeskPlugPower).State = 50;

        HeatingControlService sut = ActivatorUtilities.CreateInstance<HeatingControlService>(serviceProvider);

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
        ICustomSwitchEntity? heaterPlug = room switch
        {
            "Games room" => namedEntities.GamesRoomHeaterSmartPlugOnOff,
            "Kitchen" => namedEntities.KitchenHeaterSmartPlugOnOff,
            "Bedroom 1" => namedEntities.Bedroom1HeaterSmartPlugOnOff,
            "Dining room" => namedEntities.DiningRoomHeaterSmartPlugOnOff,
            _ => throw new NotSupportedException($"Room {room} not supported in tests")
        };

        // For rooms with climate control (thermostat), set up the climate entity
        // These rooms have thermostats that are NOT time-restricted, unlike the plugs
        ICustomClimateControlEntity? climateControl = room switch
        {
            "Bedroom 1" => namedEntities.Bedroom1RadiatorThermostat,
            "Dining room" => namedEntities.DiningRoomRadiatorThermostat,
            _ => null
        };

        if (currentHeatingState)
        {
            heaterPlug.TurnOn();
            if (climateControl != null)
            {
                FakeCustomClimateControlEntity fakeClimate = (FakeCustomClimateControlEntity)climateControl;
                fakeClimate.CurrentTemperature = currentTemperature;
                fakeClimate.SetTargetTemperature(currentTemperature.GetValueOrDefault() + 5);
            }
        }
        else
        {
            heaterPlug.TurnOff();
            if (climateControl != null)
            {
                FakeCustomClimateControlEntity fakeClimate = (FakeCustomClimateControlEntity)climateControl;
                fakeClimate.CurrentTemperature = currentTemperature;
                fakeClimate.SetTargetTemperature(currentTemperature.GetValueOrDefault() - 5);
            }
        }

        await sut.EvaluateAllSchedules("unit test");

        // Determine heating state for each device type
        bool plugIsHeating = heaterPlug.IsOn();
        bool thermostatIsHeating = climateControl != null && climateControl.TargetTemperature > climateControl.CurrentTemperature;

        // For rooms with both plug and thermostat, at least one should be heating
        // Plugs are time-restricted, thermostats are not - so thermostat can heat during restricted hours
        bool isAnyDeviceHeating = plugIsHeating || thermostatIsHeating;

        if (expectedHeatingState == Heating_Should_Be_On)
        {
            if (climateControl != null)
            {
                // Room has both plug and thermostat - at least one should be heating
                isAnyDeviceHeating.ShouldBeTrue($"{reason} - At least one heating device (plug or thermostat) should be active");
            }
            else
            {
                // Room only has plug - plug must be on
                heaterPlug.IsOn().ShouldBeTrue(reason);
            }
        }
        else
        {
            if (climateControl != null)
            {
                // Room has both devices - neither should be heating
                isAnyDeviceHeating.ShouldBeFalse($"{reason} - No heating device should be active");
            }
            else
            {
                // Room only has plug - plug must be off
                heaterPlug.IsOff().ShouldBeTrue(reason);
            }
        }
    }

    private static ServiceProvider GetServiceProvider()
    {
        ServiceCollection services = new();

        // Use mock HistoryService to avoid HTTP calls
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
            },
            new RoomSchedule(){
                 Name = "Bedroom 1",
                 ScheduleTracks = [
                    new HeatingScheduleTrack(){
                          TargetTime = new TimeOnly(07,00),
                          Temperature = 18
                    },
                    new HeatingScheduleTrack(){
                          TargetTime = new TimeOnly(22,00),
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

internal class MockHistoryService : HistoryService
{
    public MockHistoryService(HomeAssistantConfiguration settings, ILogger<HistoryService> logger)
        : base(settings, logger)
    {
    }

    public override Task<IReadOnlyList<NumericHistoryEntry>> GetEntityNumericHistory(string entityId, DateTime from, DateTime to)
    {
        // Return empty history for tests - we don't need actual historical data
        return Task.FromResult<IReadOnlyList<NumericHistoryEntry>>(new List<NumericHistoryEntry>());
    }

    public override Task<IReadOnlyList<HistoryTextEntry>> GetEntityTextHistory(string entityId, DateTime from, DateTime to)
    {
        // Return empty history for tests
        return Task.FromResult<IReadOnlyList<HistoryTextEntry>>(new List<HistoryTextEntry>());
    }
}
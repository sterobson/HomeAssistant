using HomeAssistant.apps;
using HomeAssistant.Devices.Batteries;
using HomeAssistant.Devices.Meters;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Threading.Tasks;

namespace HomeAssistant.Services.Climate;

internal class HeatingControlService
{
    private readonly IScheduler _scheduler;
    private readonly ILogger<HeatingControlService> _logger;
    private readonly IHomeBattery _homeBattery;
    private readonly ISolarPanels _solarPanels;
    private readonly IElectricityMeter _electricityMeter;
    private readonly IPresenceService _presenceService;
    private readonly TimeProvider _timeProvider;
    private readonly INamedEntities _namedEntities;
    private const int _receheckEveryXMinutes = 5;
    public List<Schedule> Schedules { get; } = [];

    public HeatingControlService(INamedEntities namedEntities, HistoryService historyService, IScheduler scheduler, ILogger<HeatingControlService> logger,
        IHomeBattery homeBattery, ISolarPanels solarPanels, IElectricityMeter electricityMeter, IPresenceService presenceService,
        TimeProvider timeProvider)
    {
        _namedEntities = namedEntities;
        _scheduler = scheduler;
        _logger = logger;
        _homeBattery = homeBattery;
        _solarPanels = solarPanels;
        _electricityMeter = electricityMeter;
        _presenceService = presenceService;
        _timeProvider = timeProvider;
        Schedules = [
            new()
            {
                Condition = () => true,
                Room = Room.Kitchen,
                ScheduleTracks = [
                    new HeatingScheduleTrack { TargetTime = new TimeOnly(5,30), Temperature = 19, Conditions = ConditionType.PlentyOfPowerAvailable },
                    new HeatingScheduleTrack { TargetTime = new TimeOnly(6,30), Temperature = 18.5 },
                    new HeatingScheduleTrack { TargetTime = new TimeOnly(6,30), Temperature = 19, Conditions = ConditionType.PlentyOfPowerAvailable },
                    new HeatingScheduleTrack { TargetTime = new TimeOnly(18,00), Temperature = 19 },
                    new HeatingScheduleTrack { TargetTime = new TimeOnly(21,30), Temperature = 16 }
                ]
            },
            new()
            {
                Condition = () => true,
                Room = Room.GamesRoom,
                ScheduleTracks = [
                    new HeatingScheduleTrack{ TargetTime = new TimeOnly(0,00), Temperature = 14 },
                    new HeatingScheduleTrack{ TargetTime = new TimeOnly(7,00), Temperature = 18, Days = Days.Weekdays}, // Preheat on a weekday morning, anticipating use
                    new HeatingScheduleTrack{ TargetTime = new TimeOnly(6,00), Temperature = 19, Conditions = ConditionType.RoomInUse }, // Only if the desk has been on and in use
                    new HeatingScheduleTrack{ TargetTime = new TimeOnly(9,00), Temperature = 16, Conditions = ConditionType.RoomNotInUse  }, // Only if the desk has not been in use
                    new HeatingScheduleTrack{ TargetTime = new TimeOnly(21,30), Temperature = 14, Conditions = ConditionType.RoomNotInUse }
                ]
            },
            new()
            {
                Condition = () => true,
                Room = Room.Bedroom1,
                ScheduleTracks = [
                    new HeatingScheduleTrack{ TargetTime = new TimeOnly(8,00), Temperature = 19, Days = Days.Weekdays},
                    new HeatingScheduleTrack{ TargetTime = new TimeOnly(8,30), Temperature = 16, Days = Days.Weekdays},
                    new HeatingScheduleTrack{ TargetTime = new TimeOnly(7,30), Temperature = 19, Days = Days.Saturday},
                    new HeatingScheduleTrack{ TargetTime = new TimeOnly(8,00), Temperature = 16, Days = Days.Saturday},
                    new HeatingScheduleTrack{ TargetTime = new TimeOnly(9,00), Temperature = 19, Days = Days.Sunday},
                    new HeatingScheduleTrack{ TargetTime = new TimeOnly(9,30), Temperature = 16, Days = Days.Sunday},
                    new HeatingScheduleTrack{ TargetTime = new TimeOnly(21,30), Temperature = 19 },
                    new HeatingScheduleTrack{ TargetTime = new TimeOnly(21,31), Temperature = 14 },
                ]
            }
        ];
    }

    public void Start()
    {
        _scheduler.SchedulePeriodic(TimeSpan.FromMinutes(_receheckEveryXMinutes), async () => await EvaluateAllSchedules(Schedules));

        _namedEntities.GamesRoomDeskTemperature.SubscribeToStateChangesAsync(async change => await EvaluateSchedule(Schedules.First(s => s.Room == Room.GamesRoom)));
        _namedEntities.GamesRoomDeskPlugOnOff.SubscribeToStateChangesAsync(async change => await EvaluateSchedule(Schedules.First(s => s.Room == Room.GamesRoom)));
        _namedEntities.KitchenTemperature.SubscribeToStateChangesAsync(async change => await EvaluateSchedule(Schedules.First(s => s.Room == Room.Kitchen)));
        _namedEntities.Bedroom1Temperature.SubscribeToStateChangesAsync(async change => await EvaluateSchedule(Schedules.First(s => s.Room == Room.Bedroom1)));

        _homeBattery.OnBatteryChargePercentChanged(async _ => await EvaluateAllSchedules(Schedules));
        _electricityMeter.OnCurrentRatePerKwhChanged(async _ => await EvaluateAllSchedules(Schedules));

        Task.Delay(1000).ContinueWith(async (value) => await EvaluateAllSchedules(Schedules));
    }

    public async Task EvaluateAllSchedules(List<Schedule> schedules)
    {
        foreach (Schedule schedule in schedules)
        {
            await EvaluateSchedule(schedule);
        }
    }

    private async Task EvaluateSchedule(Schedule roomHeatingSchedule)
    {
        DateTime now = _timeProvider.GetLocalNow().DateTime;
        TimeOnly currentTime = TimeOnly.FromDateTime(now);

        Days currentDay = now.DayOfWeek switch
        {
            DayOfWeek.Monday => Days.Monday,
            DayOfWeek.Tuesday => Days.Tuesday,
            DayOfWeek.Wednesday => Days.Wednesday,
            DayOfWeek.Thursday => Days.Thursday,
            DayOfWeek.Friday => Days.Friday,
            DayOfWeek.Saturday => Days.Saturday,
            DayOfWeek.Sunday => Days.Sunday,
            _ => Days.Everyday
        };

        // Get the current temperature, either from the supplied delegate, or the service's own function.
        double? currentTemperature = roomHeatingSchedule.GetCurrentTemperature != null ? await roomHeatingSchedule.GetCurrentTemperature() : GetCurrentTemperatureForRoom(roomHeatingSchedule);
        if (currentTemperature == null)
        {
            return;
        }

        // Get the toggle action, either from the supplied action, or the service's own function.
        Func<bool, Task<bool>>? onToggleHeating = roomHeatingSchedule.OnToggleHeating ?? GetOnToggleFunc(roomHeatingSchedule);
        if (onToggleHeating == null)
        {
            return;
        }

        // We want to find the last TargetTemperature schedule track that evaluates to true.
        bool evaluateYesterdayTimes = false;
        for (int i = roomHeatingSchedule.ScheduleTracks.Count - 1; i >= 0; i--)
        {
            HeatingScheduleTrack heatingScheduleTrack = roomHeatingSchedule.ScheduleTracks[i];
            double desiredTemperature = heatingScheduleTrack.Temperature;

            if (heatingScheduleTrack == null)
            {
                RollOverToYesterdayIfNecessary(roomHeatingSchedule, ref evaluateYesterdayTimes, ref i);
                continue;
            }

            // Is this schedule valid for today?
            if (heatingScheduleTrack.Days != Days.Everyday && (heatingScheduleTrack.Days & currentDay) == 0)
            {
                RollOverToYesterdayIfNecessary(roomHeatingSchedule, ref evaluateYesterdayTimes, ref i);
                continue;
            }

            // What time should this track start, including the ramp-up time?
            TimeSpan targetStartTime = heatingScheduleTrack.TargetTime.ToTimeSpan().Add(TimeSpan.FromMinutes(-heatingScheduleTrack.RampUpMinutes));

            // If we've rolled over to comparing the previous day's schedule,
            // then do this by adding 1 day to the current time to make the maths work.
            TimeSpan effectiveCurrentTime = now.TimeOfDay.Add(TimeSpan.FromDays(evaluateYesterdayTimes ? 1 : 0));
            if (targetStartTime > effectiveCurrentTime)
            {
                RollOverToYesterdayIfNecessary(roomHeatingSchedule, ref evaluateYesterdayTimes, ref i);
                continue;
            }

            // Is it time to run this schedule yet (including the ramp-up time)?
            int minutesUntilTargetTime = MinutesUntil(currentTime, heatingScheduleTrack.TargetTime);
            if (heatingScheduleTrack.TargetTime < currentTime)
            {
                minutesUntilTargetTime = 0;
            }

            if (minutesUntilTargetTime > heatingScheduleTrack.RampUpMinutes)
            {
                RollOverToYesterdayIfNecessary(roomHeatingSchedule, ref evaluateYesterdayTimes, ref i);
                continue;
            }

            // Within the ramp-up time but already warmer than target temperature, so skip
            // to previous rule.
            if (minutesUntilTargetTime > 0 && currentTemperature >= desiredTemperature)
            {
                RollOverToYesterdayIfNecessary(roomHeatingSchedule, ref evaluateYesterdayTimes, ref i);
                continue;
            }

            // If there are special confitions set, then we need to make sure we meet them.
            if (!await MeetsSpecialConditions(roomHeatingSchedule.Room, heatingScheduleTrack))
            {
                RollOverToYesterdayIfNecessary(roomHeatingSchedule, ref evaluateYesterdayTimes, ref i);
                continue;
            }

            if (currentTemperature >= desiredTemperature)
            {
                // Turn off
                if (await onToggleHeating(false))
                {
                    _logger.LogInformation("Turning off heating in {Room} as current temperature {CurrentTemperature}°C >= target temperature {TargetTemperature}°C, looked at schedule {i}",
                         roomHeatingSchedule.Room, currentTemperature, desiredTemperature, i);
                }
            }
            else
            {
                // Turn on
                if (await onToggleHeating(true))
                {
                    _logger.LogInformation("Turning on heating in {Room} as current temperature {CurrentTemperature}°C < target temperature {TargetTemperature}°C, looked at schedule {i}",
                         roomHeatingSchedule.Room, currentTemperature, desiredTemperature, i);
                }
            }

            break;
        }
    }

    private Func<bool, Task<bool>>? GetOnToggleFunc(Schedule roomHeatingSchedule)
    {
        ICustomSwitchEntity? plug = roomHeatingSchedule.Room switch
        {
            Room.Kitchen => _namedEntities.KitchenHeaterSmartPlugOnOff,
            Room.GamesRoom => _namedEntities.GamesRoomHeaterSmartPlugOnOff,
            Room.DiningRoom => null,
            Room.Lounge => null,
            Room.DownstairsBathroom => null,
            Room.Bedroom1 => _namedEntities.Bedroom1HeaterSmartPlugOnOff,
            Room.Bedroom2 => null,
            Room.Bedroom3 => null,
            Room.UpstairsBathroom => null,
            _ => null
        };

        if (plug == null)
        {
            return null;
        }

        return async (value) =>
        {
            if (value && !plug.IsOn())
            {
                plug.TurnOn();
                return true;
            }
            else if (!value && !plug.IsOff())
            {
                plug.TurnOff();
                return true;
            }

            return false;
        };
    }

    private double? GetCurrentTemperatureForRoom(Schedule roomHeatingSchedule)
    {
        return roomHeatingSchedule.Room switch
        {
            Room.Kitchen => _namedEntities.KitchenTemperature.State,
            Room.GamesRoom => _namedEntities.GamesRoomDeskTemperature.State,
            Room.DiningRoom => null,
            Room.Lounge => null,
            Room.DownstairsBathroom => null,
            Room.Bedroom1 => _namedEntities.Bedroom1Temperature.State,
            Room.Bedroom2 => null,
            Room.Bedroom3 => null,
            Room.UpstairsBathroom => null,
            _ => null
        };
    }

    private static void RollOverToYesterdayIfNecessary(Schedule roomHeatingSchedule, ref bool evaluateYesterdayTimes, ref int i)
    {
        if (i == 0 && !evaluateYesterdayTimes)
        {
            i = roomHeatingSchedule.ScheduleTracks.Count;
            evaluateYesterdayTimes = true;
        }
    }

    private async Task<bool> MeetsSpecialConditions(Room room, HeatingScheduleTrack heatingScheduleTrack)
    {
        bool meetsAnyConditions = heatingScheduleTrack.Conditions == ConditionType.None;
        bool meetsAllConditions = true;
        if (heatingScheduleTrack.Conditions.HasFlag(ConditionType.PlentyOfPowerAvailable))
        {
            bool havePlentyOfPower = await HavePlentyOfPowerAvailable();
            meetsAnyConditions |= havePlentyOfPower;
            meetsAllConditions &= havePlentyOfPower;
        }

        if (heatingScheduleTrack.Conditions.HasFlag(ConditionType.LowPowerAvailable))
        {
            bool haveLowPower = !await HavePlentyOfPowerAvailable();
            meetsAnyConditions |= haveLowPower;
            meetsAllConditions &= haveLowPower;
        }

        if (heatingScheduleTrack.Conditions.HasFlag(ConditionType.RoomInUse))
        {
            bool roomInUse = await _presenceService.IsRoomInUse(room);
            meetsAnyConditions |= roomInUse;
            meetsAllConditions &= roomInUse;
        }

        if (heatingScheduleTrack.Conditions.HasFlag(ConditionType.RoomNotInUse))
        {
            bool roomNotInUse = !await _presenceService.IsRoomInUse(room);
            meetsAnyConditions |= roomNotInUse;
            meetsAllConditions &= roomNotInUse;
        }

        // Check if we meet the conditions
        if ((heatingScheduleTrack.ConditionOperator == ConditionOperatorType.Or && !meetsAnyConditions)
            || (heatingScheduleTrack.ConditionOperator == ConditionOperatorType.And && !meetsAllConditions))
        {
            return false;
        }

        return true;
    }

    private async Task<bool> HavePlentyOfPowerAvailable()
    {
        DateTime now = _timeProvider.GetLocalNow().DateTime;
        double dischargePercentPerMinute = 0;

        // What's the battery's rate of discharge looking like?
        IReadOnlyList<NumericHistoryEntry> batteryChargeHistory = await _homeBattery.GetTotalBatteryPowerChargeHistoryEntriesAsync(now.AddHours(-1), now);
        if (batteryChargeHistory.Count >= 2)
        {
            NumericHistoryEntry first = batteryChargeHistory[0];
            NumericHistoryEntry last = batteryChargeHistory[^1];
            double totalMinutes = last.LastChanged.Subtract(first.LastChanged).TotalMinutes;
            dischargePercentPerMinute = (first.State - last.State) / totalMinutes;
        }

        // We need 20% battery by 11pm. If we're on track for that, then we have no problem.
        double minutesUntil11pm = (23 - now.Hour) * 60 - now.Minute;
        double projectedBatteryLevelAt11pm = _homeBattery.CurrentChargePercent.GetValueOrDefault() - (dischargePercentPerMinute * minutesUntil11pm);

        if (projectedBatteryLevelAt11pm > 20)
        {
            return true;
        }

        // Don't have lots of charge, but energy is currently cheap, so we can afford to use some.
        if (_electricityMeter.CurrentRatePerKwh < 0.1)
        {
            return true;
        }

        // If we've made more than 1kWh of solar power in the last hour, then we have no problem.
        IReadOnlyList<NumericHistoryEntry> historyOneHour = await _solarPanels.GetTotalSolarPanelPowerHistoryEntriesAsync(now.AddHours(-1), now);
        double pvkWh = HistoryIntegrator.Integrate(historyOneHour, now.AddHours(-1), now) / 3600_000;
        if (pvkWh > 1)
        {
            return true;
        }

        return false;
    }

    public static int MinutesUntil(TimeOnly from, TimeOnly to)
    {
        // Convert both to TimeSpan for easy math
        TimeSpan fromSpan = from.ToTimeSpan();
        TimeSpan toSpan = to.ToTimeSpan();

        // If 'to' is later in the same day
        if (toSpan >= fromSpan)
        {
            return (int)(toSpan - fromSpan).TotalMinutes;
        }
        else
        {
            // Wrap to next day: add 24h
            return (int)((toSpan - fromSpan + TimeSpan.FromDays(1)).TotalMinutes);
        }
    }
}
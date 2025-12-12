using HomeAssistant.apps;
using HomeAssistant.Devices.Batteries;
using HomeAssistant.Devices.Meters;
using HomeAssistant.Shared.Climate;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Threading.Tasks;

namespace HomeAssistant.Services.Climate;

internal class HeatingControlService
{
    private readonly IScheduler _scheduler;
    private readonly ILogger<HeatingControlService> _logger;
    private readonly IHomeBattery _homeBattery;
    private readonly IElectricityMeter _electricityMeter;
    private readonly IPresenceService _presenceService;
    private readonly TimeProvider _timeProvider;
    private readonly INamedEntities _namedEntities;
    private readonly ISchedulePersistenceService _schedulePersistence;
    private readonly IRoomStatePersistenceService _statePersistence;
    private readonly HistoryService _historyService;
    private const int _recheckEveryXMinutes = 5;
    internal const double HysteresisOffset = 0.2;
    private readonly Dictionary<int, RoomState> _roomStates = [];
    private bool _hasUploadedState = false;

    public HeatingControlService(
        INamedEntities namedEntities,
        IScheduler scheduler,
        ILogger<HeatingControlService> logger,
        IHomeBattery homeBattery,
        IElectricityMeter electricityMeter,
        IPresenceService presenceService,
        TimeProvider timeProvider,
        ISchedulePersistenceService schedulePersistence,
        IRoomStatePersistenceService statePersistence)
    {
        _namedEntities = namedEntities;
        _scheduler = scheduler;
        _logger = logger;
        _homeBattery = homeBattery;
        _electricityMeter = electricityMeter;
        _presenceService = presenceService;
        _timeProvider = timeProvider;
        _schedulePersistence = schedulePersistence;
        _statePersistence = statePersistence;

        //// Initialize with default schedules (will be replaced if API client is available)
        //Schedules = new()
        //{
        //    Rooms = [
        //        new()
        //        {
        //            Id = 1,
        //            Name = "Kitcken",
        //            ScheduleTracks = [
        //                new HeatingScheduleTrack { TargetTime = new TimeOnly(5,30), Temperature = 17 },
        //                new HeatingScheduleTrack { TargetTime = new TimeOnly(6,30), Temperature = 18, Days = Days.NotSunday },
        //                new HeatingScheduleTrack { TargetTime = new TimeOnly(18,00), Temperature = 19 },
        //                new HeatingScheduleTrack { TargetTime = new TimeOnly(18,30), Temperature = 17.5 },
        //                new HeatingScheduleTrack { TargetTime = new TimeOnly(21,30), Temperature = 16 }
        //            ]
        //        },
        //        new()
        //        {
        //            Id = 2,
        //            Name = "Games room",
        //            ScheduleTracks = [
        //                new HeatingScheduleTrack{ TargetTime = new TimeOnly(0,00), Temperature = 19, Conditions = ConditionType.RoomInUse }, // Only if the desk has been on and in use
        //                new HeatingScheduleTrack{ TargetTime = new TimeOnly(0,00), Temperature = 14, Conditions = ConditionType.RoomNotInUse },
        //                new HeatingScheduleTrack{ TargetTime = new TimeOnly(7,00), Temperature = 18, Conditions = ConditionType.RoomNotInUse, Days = Days.Weekdays }, // Preheat on a weekday morning, anticipating use
        //                new HeatingScheduleTrack{ TargetTime = new TimeOnly(9,00), Temperature = 16, Conditions = ConditionType.RoomNotInUse }, // Only if the desk has not been in use
        //                new HeatingScheduleTrack{ TargetTime = new TimeOnly(21,30), Temperature = 14, Conditions = ConditionType.RoomNotInUse }
        //            ]
        //        },
        //        new()
        //        {
        //            Id = 3,
        //            Name = "Bedroom 1",
        //            ScheduleTracks = [
        //                new HeatingScheduleTrack{ TargetTime = new TimeOnly(8,00), Temperature = 18, Days = Days.Weekdays},
        //                new HeatingScheduleTrack{ TargetTime = new TimeOnly(8,30), Temperature = 16, Days = Days.Weekdays},
        //                new HeatingScheduleTrack{ TargetTime = new TimeOnly(7,30), Temperature = 19, Days = Days.Saturday},
        //                new HeatingScheduleTrack{ TargetTime = new TimeOnly(8,00), Temperature = 16, Days = Days.Saturday},
        //                new HeatingScheduleTrack{ TargetTime = new TimeOnly(9,00), Temperature = 19, Days = Days.Sunday},
        //                new HeatingScheduleTrack{ TargetTime = new TimeOnly(9,30), Temperature = 16, Days = Days.Sunday},
        //                new HeatingScheduleTrack{ TargetTime = new TimeOnly(21,30), Temperature = 18 },
        //                new HeatingScheduleTrack{ TargetTime = new TimeOnly(21,31), Temperature = 14 },
        //            ]
        //        },
        //        new()
        //        {
        //            Id = 4,
        //            Name = "Dining room",
        //            ScheduleTracks = [
        //                new HeatingScheduleTrack{ TargetTime = new TimeOnly(0,00), Temperature = 19, Conditions = ConditionType.RoomInUse }, // Only if the desk has been on and in use
        //                new HeatingScheduleTrack{ TargetTime = new TimeOnly(0,00), Temperature = 14, Conditions = ConditionType.RoomNotInUse },
        //                new HeatingScheduleTrack{ TargetTime = new TimeOnly(6,00), Temperature = 17, Conditions = ConditionType.RoomNotInUse },
        //                new HeatingScheduleTrack{ TargetTime = new TimeOnly(21,00), Temperature = 14, Conditions = ConditionType.RoomNotInUse }
        //            ]
        //        },

        //    ]
        //};

        // string s = System.Text.Json.JsonSerializer.Serialize(ScheduleMapper.MapToDto(Schedules));
    }

    public void Start()
    {
        // Subscribe to schedule updates from SignalR
        _schedulePersistence.SchedulesUpdated += async () =>
        {
            await EvaluateAllSchedules("SignalR schedules updated notification");
        };

        // Start the schedule persistence service (loads from local storage and connects to SignalR)
        Task.Run(async () =>
        {
            await _schedulePersistence.StartAsync();

            // Initial evaluation
            await EvaluateAllSchedules("app startup");
        });

        // Schedule periodic evaluation of all schedules
        _scheduler.SchedulePeriodic(TimeSpan.FromMinutes(_recheckEveryXMinutes), async () => await EvaluateAllSchedules($"{_recheckEveryXMinutes} minute period recheck"));

        // Subscribe to temperature changes
        _namedEntities.GamesRoomDeskTemperature.SubscribeToStateChangesAsync(async change => await EvaluateAllSchedules("games room temperature changed"));
        _namedEntities.GamesRoomDeskPlugOnOff.SubscribeToStateChangesAsync(async change => await EvaluateAllSchedules("games room desk plug state changed"));
        _namedEntities.KitchenTemperature.SubscribeToStateChangesAsync(async change => await EvaluateAllSchedules("kitchen temperature changed"));
        _namedEntities.Bedroom1Temperature.SubscribeToStateChangesAsync(async change => await EvaluateAllSchedules("bedroom 1 temperature changed"));
        _namedEntities.DiningRoomDeskPlugOnOff.SubscribeToStateChangesAsync(async change => await EvaluateAllSchedules("dining room desk plug state changed"));
        _namedEntities.DiningRoomClimateTemperature.SubscribeToStateChangesAsync(async change => await EvaluateAllSchedules("dining room temperature changed"));
        _namedEntities.LivingRoomClimateTemperature.SubscribeToStateChangesAsync(async change => await EvaluateAllSchedules("living room temperature changed"));

        // Subscribe to power changes
        _homeBattery.OnBatteryChargePercentChanged(async _ => await EvaluateAllSchedules("home battery charge changed"));
        _electricityMeter.OnCurrentRatePerKwhChanged(async _ => await EvaluateAllSchedules("electricity import rate changed"));
    }

    private async Task<RoomSchedules> LoadSchedulesAsync()
    {
        RoomSchedules? schedules = await _schedulePersistence.GetSchedulesAsync();

        // If we have schedules, set them up
        if (schedules != null && schedules.Rooms.Count > 0)
        {
            // Set up delegates for each schedule
            foreach (RoomSchedule schedule in schedules.Rooms)
            {
                schedule.GetCurrentTemperature = () => Task.FromResult(GetCurrentTemperatureForRoom(schedule));
                schedule.OnToggleHeating = GetOnToggleFunc(schedule);
            }

            _logger.LogDebug("Loaded {Count} schedules", schedules.Rooms.Count);

            // Initialize room states for any new rooms
            foreach (RoomSchedule schedule in schedules.Rooms)
            {
                if (!_roomStates.ContainsKey(schedule.Id))
                {
                    _roomStates[schedule.Id] = new RoomState
                    {
                        RoomId = schedule.Id,
                        CurrentTemperature = GetCurrentTemperatureForRoom(schedule),
                        HeatingActive = false,
                        ActiveScheduleTrackId = 0,
                        LastUpdated = DateTimeOffset.UtcNow,
                        Capabilities = GetRoomCapabilities(schedule)
                    };
                }
            }

            if (!_hasUploadedState)
            {
                await UpdateRoomStatesAsync();
                _hasUploadedState = true;
            }
        }

        return schedules ?? new();
    }

    private async Task UpdateRoomStatesAsync()
    {
        try
        {
            RoomSchedules schedules = await _schedulePersistence.GetSchedulesAsync();
            if (schedules != null)
            {
                foreach (KeyValuePair<int, RoomState> kvp in _roomStates)
                {
                    RoomSchedule? schedule = schedules.Rooms.Find(r => r.Id == kvp.Value.RoomId);
                    if (schedule == null)
                    {
                        continue;
                    }

                    kvp.Value.Capabilities = GetRoomCapabilities(schedule);
                    kvp.Value.CurrentTemperature = GetCurrentTemperatureForRoom(schedule);
                }
            }

            // Upload to API (SignalR notification will be broadcast automatically)
            await _statePersistence.SetStatesAsync(_roomStates);
            _logger.LogDebug("Updated room states");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating room states");
        }
    }

    public async Task EvaluateAllSchedules(string trigger)
    {
        RoomSchedules schedules = await LoadSchedulesAsync();
        bool hasStateChanged = false;
        foreach (RoomSchedule schedule in schedules.Rooms)
        {
            hasStateChanged |= await EvaluateSchedule(schedules.HouseOccupancyState, schedule, trigger);
        }

        if (hasStateChanged)
        {
            await UpdateRoomStatesAsync();
        }
    }

    private async Task<bool> EvaluateSchedule(HouseOccupancyState houseOccupancyState, RoomSchedule? roomHeatingSchedule, string trigger)
    {
        ArgumentNullException.ThrowIfNull(roomHeatingSchedule);

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
            return false;
        }

        // Get the toggle action, either from the supplied action, or the service's own function.
        Func<bool, Task<bool>>? onToggleHeating = roomHeatingSchedule.OnToggleHeating ?? GetOnToggleFunc(roomHeatingSchedule);
        if (onToggleHeating == null)
        {
            return false;
        }

        // Step 0: Is there a boost active?
        bool hasBoost = roomHeatingSchedule.Boost?.EndTime.GetValueOrDefault().DateTime > now && roomHeatingSchedule.Boost.Temperature.HasValue;
        double desiredTemperature;
        string reason;
        HeatingScheduleTrack? effectiveTrack = null;

        if (hasBoost)
        {
            desiredTemperature = roomHeatingSchedule.Boost?.Temperature ?? 0;
            reason = "boost is active";
        }
        else
        {
            // Step 1: Find the current active schedule (what temperature should it be RIGHT NOW?)
            HeatingScheduleTrack? currentActiveTrack = await FindCurrentActiveSchedule(houseOccupancyState, roomHeatingSchedule, now, currentDay);
            if (currentActiveTrack == null)
            {
                return false;
            }

            // Step 2: Check if there's an upcoming schedule we should pre-heat for
            HeatingScheduleTrack? preHeatTrack = await FindPreHeatSchedule(houseOccupancyState, roomHeatingSchedule, now, currentDay, currentActiveTrack.Temperature);

            // Step 3: Determine which schedule to use
            effectiveTrack = currentActiveTrack;
            reason = "current active schedule";

            if (preHeatTrack != null)
            {
                // Only use pre-heat if it has a HIGHER target than current (never pre-cool)
                if (preHeatTrack.Temperature > currentActiveTrack.Temperature)
                {
                    effectiveTrack = preHeatTrack;
                    reason = "pre-heating for upcoming schedule";
                }
            }

            // Step 4: Control heating based on effective track
            desiredTemperature = effectiveTrack.Temperature;
        }

        // Figure out the actual current state of the room.
        object? roomControlDevice = GetSwitchForRoom(roomHeatingSchedule);
        bool? currentHeatingState = null;
        if (roomControlDevice is ICustomSwitchEntity plug)
        {
            if (plug?.IsOn() == true) currentHeatingState = true;
            if (plug?.IsOff() == true) currentHeatingState = false;
        }
        else if (roomControlDevice is ICustomClimateControlEntity climateController)
        {
            if (climateController.TargetTemperature > climateController.CurrentTemperature) currentHeatingState = true;
            if (climateController.TargetTemperature <= climateController.CurrentTemperature) currentHeatingState = false;
        }

        // Ensure that our cached state is updated.
        _roomStates.TryGetValue(roomHeatingSchedule.Id, out RoomState? roomState);
        bool? previousHeatingActive = roomState?.HeatingActive;
        bool stateChanged = false;

        if (roomState == null)
        {
            roomState = new RoomState();
            stateChanged = true;
        }

        double? previousRoomTemperature = roomState.CurrentTemperature;
        roomState.RoomId = roomHeatingSchedule.Id;
        roomState.CurrentTemperature = currentTemperature;
        roomState.TargetTemperature = desiredTemperature;
        roomState.HeatingActive = currentHeatingState ?? false;
        roomState.ActiveScheduleTrackId = effectiveTrack?.Id ?? 0;
        roomState.LastUpdated = DateTimeOffset.UtcNow;
        roomState.Capabilities = GetRoomCapabilities(roomHeatingSchedule);
        _roomStates[roomHeatingSchedule.Id] = roomState;

        // Apply hysteresis to prevent flapping
        // If heating is ON: turn OFF only when temp >= target + offset
        // If heating is OFF: turn ON only when temp <= target - offset
        if (roomState.HeatingActive && currentTemperature >= desiredTemperature + HysteresisOffset)
        {
            // Heating is currently ON
            // Turn off
            if (await onToggleHeating(false))
            {
                _logger.LogInformation("\n * Room {Room}\n" +
                    " * Turning {NewState}\n" +
                    " * Current temperature {CurrentTemperature}°C\n" +
                    " * Target temperature {TargetTemperature}°C + {HysteresisOffset}°C\n" +
                    " * {Reason}\n" +
                    " * Triggered by {Trigger}",
                     roomHeatingSchedule.Name, "off", currentTemperature, desiredTemperature, HysteresisOffset, reason, trigger);

                roomState.HeatingActive = false;
                stateChanged = true;
            }
        }
        else if (currentTemperature <= desiredTemperature - HysteresisOffset)
        {
            // Heating is currently OFF
            // Turn on
            if (await onToggleHeating(true))
            {
                _logger.LogInformation(" * Room {Room}\n" +
                    " * Turning {NewState}\n" +
                    " * Current temperature {CurrentTemperature}°C\n" +
                    " * Target temperature {TargetTemperature}°C - {HysteresisOffset}°C\n" +
                    " * {Reason}\n" +
                    " * Triggered by {Trigger}",
                     roomHeatingSchedule.Name, "on", currentTemperature, desiredTemperature, HysteresisOffset, reason, trigger);

                roomState.HeatingActive = true;
                stateChanged = true;
            }
        }

        // Update temperature and active track if changed
        if (roomState.CurrentTemperature != previousRoomTemperature)
        {
            roomState.CurrentTemperature = currentTemperature;
            stateChanged = true;
        }

        if (roomState.ActiveScheduleTrackId != (effectiveTrack?.Id ?? 0))
        {
            roomState.ActiveScheduleTrackId = (effectiveTrack?.Id ?? 0);
            stateChanged = true;
        }

        if (previousHeatingActive == null || roomState.HeatingActive != previousHeatingActive)
        {
            stateChanged = true;
        }

        // Update timestamp and persist if anything changed
        if (stateChanged)
        {
            roomState.LastUpdated = DateTimeOffset.UtcNow;
        }

        return stateChanged;
    }

    private async Task<HeatingScheduleTrack?> FindCurrentActiveSchedule(HouseOccupancyState houseOccupancyState, RoomSchedule schedule, DateTime now, Days currentDay)
    {
        TimeSpan currentTime = now.TimeOfDay;
        HeatingScheduleTrack? bestTrack = null;

        // Look through schedules for today where TargetTime has already passed
        foreach (HeatingScheduleTrack track in schedule.ScheduleTracks)
        {
            // Check if valid for today
            if (track.Days != Days.Unspecified && (track.Days & currentDay) == 0)
                continue;

            // Has this schedule's target time passed?
            if (track.TargetTime.ToTimeSpan() <= currentTime)
            {
                // Check special conditions
                if (!await MeetsSpecialConditions(houseOccupancyState, schedule, track))
                    continue;

                // Is this the latest qualifying schedule?
                if (bestTrack == null || track.TargetTime > bestTrack.TargetTime)
                {
                    bestTrack = track;
                }
            }
        }

        // If no schedule found for today, look at yesterday's schedules (they wrap to today)
        if (bestTrack == null)
        {
            Days yesterdayDay = GetPreviousDay(currentDay);

            foreach (HeatingScheduleTrack track in schedule.ScheduleTracks)
            {
                // Check if valid for yesterday
                if (track.Days != Days.Unspecified && (track.Days & yesterdayDay) == 0)
                    continue;

                // Check special conditions
                if (!await MeetsSpecialConditions(houseOccupancyState, schedule, track))
                    continue;

                // Find the latest schedule from yesterday
                if (bestTrack == null || track.TargetTime > bestTrack.TargetTime)
                {
                    bestTrack = track;
                }
            }
        }

        return bestTrack;
    }

    private async Task<HeatingScheduleTrack?> FindPreHeatSchedule(HouseOccupancyState houseOccupancyState, RoomSchedule schedule, DateTime now, Days currentDay, double currentActiveTemperature)
    {
        TimeSpan currentTime = now.TimeOfDay;
        HeatingScheduleTrack? bestTrack = null;

        // Look through today's upcoming schedules
        foreach (HeatingScheduleTrack track in schedule.ScheduleTracks)
        {
            // Check if valid for today
            if (track.Days != Days.Unspecified && (track.Days & currentDay) == 0)
                continue;

            // Is this schedule upcoming (not yet arrived)?
            if (track.TargetTime.ToTimeSpan() > currentTime)
            {
                // Calculate ramp-up start time
                TimeSpan rampUpStartTime = track.TargetTime.ToTimeSpan().Add(TimeSpan.FromMinutes(-track.RampUpMinutes));

                // Are we within the ramp-up period?
                if (currentTime >= rampUpStartTime)
                {
                    // Only consider if temperature is higher than current active (never pre-cool)
                    if (track.Temperature <= currentActiveTemperature)
                        continue;

                    // Check special conditions
                    if (!await MeetsSpecialConditions(houseOccupancyState, schedule, track))
                        continue;

                    // Find the earliest upcoming schedule in ramp-up
                    if (bestTrack == null || track.TargetTime < bestTrack.TargetTime)
                    {
                        bestTrack = track;
                    }
                }
            }
        }

        return bestTrack;
    }

    private static Days GetPreviousDay(Days currentDay)
    {
        return currentDay switch
        {
            Days.Monday => Days.Sunday,
            Days.Tuesday => Days.Monday,
            Days.Wednesday => Days.Tuesday,
            Days.Thursday => Days.Wednesday,
            Days.Friday => Days.Thursday,
            Days.Saturday => Days.Friday,
            Days.Sunday => Days.Saturday,
            _ => Days.Monday
        };
    }

    private Func<bool, Task<bool>>? GetOnToggleFunc(RoomSchedule roomHeatingSchedule)
    {
        object? roomControllDevice = GetSwitchForRoom(roomHeatingSchedule);

        if (roomControllDevice is ICustomSwitchEntity plug)
        {
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
        else if (roomControllDevice is ICustomClimateControlEntity climateControl)
        {
            return async (value) =>
            {
                if (value && climateControl.TargetTemperature <= climateControl.CurrentTemperature)
                {
                    climateControl.SetTargetTemperature(climateControl.CurrentTemperature.GetValueOrDefault() + 5);
                    return true;
                }
                else if (!value && climateControl.TargetTemperature > climateControl.CurrentTemperature)
                {
                    climateControl.SetTargetTemperature(climateControl.CurrentTemperature.GetValueOrDefault() - 5);
                    return true;
                }

                return false;
            };
        }
        else
        {
            return null;
        }
    }

    private object? GetSwitchForRoom(RoomSchedule roomHeatingSchedule)
    {
        return roomHeatingSchedule.Name.Trim().ToLower() switch
        {
            "kitchen" => _namedEntities.KitchenHeaterSmartPlugOnOff,
            "games room" => _namedEntities.GamesRoomHeaterSmartPlugOnOff,
            "dining room" => _namedEntities.DiningRoomHeaterSmartPlugOnOff,
            "living room" => _namedEntities.LivingRoomRadiatorThermostat,
            "downstairs bathroom" => null,
            "bedroom 1" => _namedEntities.Bedroom1HeaterSmartPlugOnOff,
            "bedroom 2" => null,
            "bedroom 3" => null,
            "upstairs bathroom" => null,
            _ => null
        };
    }

    private double? GetCurrentTemperatureForRoom(RoomSchedule roomHeatingSchedule)
    {
        return roomHeatingSchedule.Name.Trim().ToLower() switch
        {
            "kitchen" => _namedEntities.KitchenTemperature.State,
            "games room" => _namedEntities.GamesRoomDeskTemperature.State,
            "dining room" => _namedEntities.DiningRoomClimateTemperature.State,
            "living room" => _namedEntities.LivingRoomClimateTemperature.State,
            "downstairs bathroom" => null,
            "bedroom 1" => _namedEntities.Bedroom1Temperature.State,
            "bedroom 2" => _namedEntities.Bedroom2Temperature.State,
            "bedroom 3" => _namedEntities.Bedroom3Temperature.State,
            "upstairs bathroom" => _namedEntities.UpstairsBathroomTemperature.State,
            _ => null
        };
    }

    private async Task<bool> MeetsSpecialConditions(HouseOccupancyState houseOccupancyState, RoomSchedule roomSchedule, HeatingScheduleTrack heatingScheduleTrack)
    {
        bool meetsAllConditions = true;

        // Legacy tracks may not have either of these set, in which case assume they are for occupied only.
        bool trackForHouseOccupied = (heatingScheduleTrack.Conditions & ConditionType.HouseOccupied) != 0
            || ((heatingScheduleTrack.Conditions & ConditionType.HouseOccupied) == 0 && (heatingScheduleTrack.Conditions & ConditionType.HouseUnoccupied) == 0);

        bool trackForHouseUnoccupied = (heatingScheduleTrack.Conditions & ConditionType.HouseUnoccupied) != 0;

        // Don't count the track if it doesn't meet the house occupancy.
        if (houseOccupancyState == HouseOccupancyState.Home && !trackForHouseOccupied)
        {
            return false;
        }

        if (houseOccupancyState == HouseOccupancyState.Away && !trackForHouseUnoccupied)
        {
            return false;
        }

        if (heatingScheduleTrack.Conditions.HasFlag(ConditionType.RoomInUse))
        {
            bool roomInUse = await _presenceService.IsRoomInUse(roomSchedule.Name);
            meetsAllConditions &= roomInUse;
        }

        if (heatingScheduleTrack.Conditions.HasFlag(ConditionType.RoomNotInUse))
        {
            bool roomNotInUse = !await _presenceService.IsRoomInUse(roomSchedule.Name);
            meetsAllConditions &= roomNotInUse;
        }

        // All conditions must be met (AND logic)
        if (!meetsAllConditions)
        {
            return false;
        }

        return true;
    }

    private RoomCapabilities GetRoomCapabilities(RoomSchedule room)
    {
        return (GetSwitchForRoom(room) != null ? RoomCapabilities.CanSetTemperature : RoomCapabilities.None)
             | (_presenceService.CanDetectIfRoomInUse(room.Name) ? RoomCapabilities.CanDetectRoomOccupancy : RoomCapabilities.None);
    }

    public static int MinutesUntil(TimeSpan from, TimeSpan to)
    {
        // If 'to' is later in the same day
        if (to >= from)
        {
            return (int)(to - from).TotalMinutes;
        }
        else
        {
            // Wrap to next day: add 24h
            return (int)((to - from + TimeSpan.FromDays(1)).TotalMinutes);
        }
    }
}
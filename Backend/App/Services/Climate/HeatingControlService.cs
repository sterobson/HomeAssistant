using HomeAssistant.apps;
using HomeAssistant.Devices.Batteries;
using HomeAssistant.Devices.Meters;
using HomeAssistant.Shared;
using HomeAssistant.Shared.Climate;
using NetDaemon.HassModel.Entities;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Text.Json;
using System.Threading;
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
    private readonly IHaContext _ha;
    private readonly INamedEntities _namedEntities;
    private readonly ISchedulePersistenceService _schedulePersistence;
    private readonly IRoomStatePersistenceService _statePersistence;
    private readonly IBoilerVerificationService _boilerVerification;
    private readonly IDeviceSettingsPersistenceService _deviceSettings;
    private const int _recheckEveryXMinutes = 5;
    internal const double HysteresisOffset = 0.2;
    // The boiler is switched on once the rooms with a radiator valve are, between them, this many
    // degrees Celsius short of their targets, and off again only when none is short at all.
    internal const double BoilerDeficitThresholdCelsius = 3;
    private readonly Dictionary<int, RoomState> _roomStates = [];
    private bool _hasUploadedState = false;
    private bool _boilerOn = false;
    private BoilerControlPath? _lastSeenBoilerControlPath = null;
    private static readonly SemaphoreSlim _evaluationSemaphore = new(1, 1);

    private readonly CustomSwitchWithConditions _gamesRoomHeaterSmartPlugOnOffWithConditions;
    private readonly CustomSwitchWithConditions _diningRoomHeaterSmartPlugOnOffWithConditions;
    private readonly CustomSwitchWithConditions _bedroom1HeaterSmartPlugOnOffWithConditions;
    private readonly CustomSwitchWithConditions _kitchenHeaterSmartPlugOnOffWithConditions;
    private readonly HousePresenceSensor _housePresenceSensor;

    public HeatingControlService(
        IHaContext ha,
        INamedEntities namedEntities,
        IScheduler scheduler,
        ILogger<HeatingControlService> logger,
        IHomeBattery homeBattery,
        IElectricityMeter electricityMeter,
        IPresenceService presenceService,
        TimeProvider timeProvider,
        ISchedulePersistenceService schedulePersistence,
        IRoomStatePersistenceService statePersistence,
        IBoilerVerificationService boilerVerification,
        IDeviceSettingsPersistenceService deviceSettings)
    {
        _ha = ha;
        _namedEntities = namedEntities;
        _scheduler = scheduler;
        _logger = logger;
        _homeBattery = homeBattery;
        _electricityMeter = electricityMeter;
        _presenceService = presenceService;
        _timeProvider = timeProvider;
        _schedulePersistence = schedulePersistence;
        _statePersistence = statePersistence;
        _boilerVerification = boilerVerification;
        _deviceSettings = deviceSettings;

        // Entities which depend on other entities.
        _gamesRoomHeaterSmartPlugOnOffWithConditions = new CustomSwitchWithConditions(namedEntities.GamesRoomHeaterSmartPlugOnOff, () => EvaluateHeaterConditions(GamesRoomHeaterConditions));
        _diningRoomHeaterSmartPlugOnOffWithConditions = new CustomSwitchWithConditions(namedEntities.DiningRoomHeaterSmartPlugOnOff, () => EvaluateHeaterConditions(DiningRoomHeaterConditions));
        _bedroom1HeaterSmartPlugOnOffWithConditions = new CustomSwitchWithConditions(namedEntities.Bedroom1HeaterSmartPlugOnOff, () => EvaluateHeaterConditions(Bedroom1HeaterConditions));
        _kitchenHeaterSmartPlugOnOffWithConditions = new CustomSwitchWithConditions(namedEntities.KitchenHeaterSmartPlugOnOff, () => EvaluateHeaterConditions(KitchenHeaterConditions));

        _housePresenceSensor = new(
            namedEntities.GamesRoomDeskPlugOnOff,
            namedEntities.GamesRoomDeskPlugPower,
            namedEntities.DiningRoomDeskPlugOnOff,
            namedEntities.DiningRoomDeskPlugPower);

        EntityExistenceValidator.ValidateEntities(ha, logger,
            namedEntities.GamesRoomHeaterSmartPlugOnOff.EntityId,
            namedEntities.DiningRoomHeaterSmartPlugOnOff.EntityId,
            namedEntities.Bedroom1HeaterSmartPlugOnOff.EntityId,
            namedEntities.KitchenHeaterSmartPlugOnOff.EntityId,
            namedEntities.GamesRoomDeskPlugOnOff.EntityId,
            namedEntities.GamesRoomDeskPlugPower.EntityId,
            namedEntities.DiningRoomDeskPlugOnOff.EntityId,
            namedEntities.DiningRoomDeskPlugPower.EntityId,
            namedEntities.GamesRoomDeskTemperature.EntityId,
            namedEntities.KitchenTemperature.EntityId,
            namedEntities.Bedroom1Temperature.EntityId,
            namedEntities.Bedroom2Temperature.EntityId,
            namedEntities.Bedroom3Temperature.EntityId,
            namedEntities.UpstairsBathroomTemperature.EntityId,
            namedEntities.DiningRoomClimateTemperature.EntityId,
            namedEntities.LivingRoomClimateTemperature.EntityId,
            namedEntities.DenClimateTemperature.EntityId,
            namedEntities.DiningRoomRadiatorThermostat.EntityId,
            namedEntities.LivingRoomRadiatorThermostat.EntityId,
            namedEntities.Bedroom1RadiatorThermostat.EntityId,
            namedEntities.Bedroom2RadiatorThermostat.EntityId,
            namedEntities.Bedroom3RadiatorThermostat.EntityId,
            namedEntities.DenRadiator1Thermostat.EntityId,
            namedEntities.DenRadiator2Thermostat.EntityId,
            namedEntities.BoilerFingerbot.EntityId);
    }

    public void Start()
    {
        // Subscribe to schedule updates from SignalR
        _schedulePersistence.SchedulesUpdated += async () =>
        {
            await EvaluateAllSchedules("SignalR schedules updated notification");
        };

        // Pick up a boiler control path change as soon as the UI saves one, rather than waiting
        // for the next periodic recheck. Delivered over the existing "device-settings-changed"
        // SignalR path.
        _deviceSettings.SettingsUpdated += async () =>
        {
            await EvaluateAllSchedules("device settings updated notification");
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
        _namedEntities.DenClimateTemperature.SubscribeToStateChangesAsync(async change => await EvaluateAllSchedules("den temperature changed"));

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
                        Capabilities = GetRoomCapabilities(schedule),
                        Devices = GetDevicesForRoom(schedule)
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
                    kvp.Value.Devices = GetDevicesForRoom(schedule);
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
        try
        {
            await _evaluationSemaphore.WaitAsync();
            RoomSchedules schedules = await LoadSchedulesAsync();
            bool hasStateChanged = false;
            foreach (RoomSchedule schedule in schedules.Rooms)
            {
                hasStateChanged |= await EvaluateSchedule(schedules.HouseOccupancyState, schedule, trigger);
            }

            await EvaluateBoilerState(schedules, trigger);

            if (hasStateChanged)
            {
                await UpdateRoomStatesAsync();
            }
        }
        finally
        {
            _evaluationSemaphore.Release();
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
        // Compare instants, not clock faces. The end time is a DateTimeOffset (the UI sends it in UTC) and
        // `now` is the local wall clock, so comparing the end's .DateTime to `now` was wrong by the UTC
        // offset: in British Summer Time every boost ended an hour early and any boost shorter than an hour
        // never ran at all.
        bool hasBoost = roomHeatingSchedule.Boost is { EndTime: DateTimeOffset boostEnd, Temperature: not null }
            && boostEnd > _timeProvider.GetUtcNow();
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
        object[] roomControlDevices = GetSwitchesForRoom(roomHeatingSchedule);
        bool? currentHeatingState = null;
        foreach (object roomControlDevice in roomControlDevices)
        {
            bool? thisDeviceHeatingState = null;
            if (roomControlDevice is ICustomSwitchEntity plug)
            {
                if (plug?.IsOn() == true) thisDeviceHeatingState = true;
                if (plug?.IsOff() == true) thisDeviceHeatingState = false;
            }
            else if (roomControlDevice is ICustomClimateControlEntity climateController)
            {
                if (climateController.TargetTemperature >= climateController.CurrentTemperature) thisDeviceHeatingState = true;
                if (climateController.TargetTemperature < climateController.CurrentTemperature) thisDeviceHeatingState = false;
            }

            if (thisDeviceHeatingState != null)
            {
                if (currentHeatingState == null)
                {
                    currentHeatingState = thisDeviceHeatingState;
                }
                else
                {
                    currentHeatingState |= thisDeviceHeatingState;
                }
            }
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
        roomState.Devices = GetDevicesForRoom(roomHeatingSchedule);
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
        List<Func<bool, Task<bool>>?> toggleFuncs = [.. GetSwitchesForRoom(roomHeatingSchedule).Select(GetOnToggleFuncForSingleDevice).Where(f => f != null)];

        if (toggleFuncs.Count == 0)
        {
            return null;
        }

        return async (value) =>
        {
            bool anyChanged = false;
            foreach (Func<bool, Task<bool>>? toggleFunc in toggleFuncs)
            {
                if (toggleFunc != null)
                {
                    bool changed = await toggleFunc(value);
                    anyChanged |= changed;
                }
            }

            return anyChanged;
        };
    }

    private static Func<bool, Task<bool>>? GetOnToggleFuncForSingleDevice(object roomControllDevice)
    {
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
                if (value && climateControl.TargetTemperature < climateControl.CurrentTemperature)
                {
                    climateControl.SetTargetTemperature(climateControl.CurrentTemperature.GetValueOrDefault() + 5);
                    return true;
                }
                else if (!value && climateControl.TargetTemperature >= climateControl.CurrentTemperature)
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

    private object[] GetSwitchesForRoom(RoomSchedule roomHeatingSchedule)
    {
        return roomHeatingSchedule.Name.Trim().ToLower() switch
        {
            "kitchen" => [_kitchenHeaterSmartPlugOnOffWithConditions],

            // The dining room and the den each split into two independently scheduled
            // zones: a desk (heater plug, presence-aware) and the room itself (radiator).
            // See the Heating section of CLAUDE.md.
            "dining desk" => [_diningRoomHeaterSmartPlugOnOffWithConditions],
            "dining room" => [_namedEntities.DiningRoomRadiatorThermostat],
            "games desk" => [_gamesRoomHeaterSmartPlugOnOffWithConditions],

            // The den's two radiators (paired 2026-10-18).
            "den" => [_namedEntities.DenRadiator1Thermostat, _namedEntities.DenRadiator2Thermostat],

            "living room" => [_namedEntities.LivingRoomRadiatorThermostat],
            "downstairs bathroom" => [],
            "bedroom 1" => [_bedroom1HeaterSmartPlugOnOffWithConditions, _namedEntities.Bedroom1RadiatorThermostat],
            "bedroom 2" => [_namedEntities.Bedroom2RadiatorThermostat],
            "bedroom 3" => [_namedEntities.Bedroom3RadiatorThermostat],
            "upstairs bathroom" => [_namedEntities.UpstairsBathroomDummyPlugOnOff],
            _ => []
        };
    }

    private double? GetCurrentTemperatureForRoom(RoomSchedule roomHeatingSchedule)
    {
        return GetThermometerForRoom(roomHeatingSchedule)?.State;
    }

    private ICustomNumericSensorEntity? GetThermometerForRoom(RoomSchedule roomHeatingSchedule)
    {
        return roomHeatingSchedule.Name.Trim().ToLower() switch
        {
            "kitchen" => _namedEntities.KitchenTemperature,

            // Both dining zones read the same physical thermometer — there is only one in
            // that room. They differ in what they drive, not in what they sense. The den
            // has its own room thermometer (since 2026-10-19), separate from Ste's desk.
            "dining desk" => _namedEntities.DiningRoomClimateTemperature,
            "dining room" => _namedEntities.DiningRoomClimateTemperature,
            "games desk" => _namedEntities.GamesRoomDeskTemperature,
            "den" => _namedEntities.DenClimateTemperature,

            "living room" => _namedEntities.LivingRoomClimateTemperature,
            "downstairs bathroom" => null,
            "bedroom 1" => _namedEntities.Bedroom1Temperature,
            "bedroom 2" => _namedEntities.Bedroom2Temperature,
            "bedroom 3" => _namedEntities.Bedroom3Temperature,
            "upstairs bathroom" => _namedEntities.UpstairsBathroomTemperature,
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
        return (GetSwitchesForRoom(room).Length != 0 ? RoomCapabilities.CanSetTemperature : RoomCapabilities.None)
             | (_presenceService.CanDetectIfRoomInUse(room.Name) ? RoomCapabilities.CanDetectRoomOccupancy : RoomCapabilities.None);
    }

    /// <summary>
    /// Every entity a zone is bound to, read from the same switches that bind it, so the list shown in the UI
    /// cannot drift from what the heating code actually drives and reads. Dummy switches are not hardware and
    /// are left out.
    /// </summary>
    private List<RoomDeviceDto> GetDevicesForRoom(RoomSchedule room)
    {
        List<RoomDeviceDto> devices = [];

        ICustomNumericSensorEntity? thermometer = GetThermometerForRoom(room);
        if (thermometer != null)
        {
            devices.Add(DescribeDevice(thermometer.EntityId, RoomDeviceRoles.Thermometer));
        }

        foreach (object device in GetSwitchesForRoom(room))
        {
            switch (device)
            {
                case ICustomClimateControlEntity valve:
                    RoomDeviceDto valveDevice = DescribeDevice(valve.EntityId, RoomDeviceRoles.RadiatorValve);
                    valveDevice.CurrentTemperature = valve.CurrentTemperature;
                    valveDevice.TargetTemperature = valve.TargetTemperature;
                    devices.Add(valveDevice);
                    break;
                case DummySwitchEntity:
                    break;
                case ICustomSwitchEntity plug:
                    devices.Add(DescribeDevice(plug.EntityId, RoomDeviceRoles.HeaterPlug));
                    break;
            }
        }

        foreach ((string entityId, string role) in _presenceService.GetPresenceDevices(room.Name))
        {
            devices.Add(DescribeDevice(entityId, role));
        }

        return devices;
    }

    private RoomDeviceDto DescribeDevice(string entityId, string role)
    {
        RoomDeviceDto device = new() { EntityId = entityId, Role = role };

        try
        {
            EntityState? state = _ha.GetState(entityId);
            device.State = state?.State;

            JsonElement? attributes = state?.AttributesJson;
            if (attributes.HasValue && attributes.Value.ValueKind == JsonValueKind.Object)
            {
                if (attributes.Value.TryGetProperty("friendly_name", out JsonElement name) && name.ValueKind == JsonValueKind.String)
                {
                    device.Name = name.GetString();
                }

                if (attributes.Value.TryGetProperty("unit_of_measurement", out JsonElement unit) && unit.ValueKind == JsonValueKind.String)
                {
                    device.Unit = unit.GetString();
                }
            }
        }
        catch (Exception ex)
        {
            // The list is for display; a malformed entity must never stop the room state being published.
            _logger.LogDebug(ex, "Could not read state for {EntityId}", entityId);
        }

        return device;
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

    private async Task EvaluateBoilerState(RoomSchedules schedules, string trigger)
    {
        BoilerControlPath controlPath = await GetBoilerControlPathAsync();
        bool pathChanged = controlPath != _lastSeenBoilerControlPath;
        _lastSeenBoilerControlPath = controlPath;

        if (controlPath == BoilerControlPath.ExternalScheduler)
        {
            if (pathChanged)
            {
                // Logged once per transition: this runs on every evaluation cycle.
                _logger.LogInformation(
                    "Boiler control is DISABLED: the house is on its external scheduler, which leaves the boiler's button dead, so a fingerbot press would do nothing. Room-level control (valves and heater plugs) continues as normal. Change this in device settings when the switch at the boiler is flipped. Triggered by {Trigger}",
                    trigger);

                // Drop anything left over from fingerbot mode so a stale verification cannot
                // resurface and provoke a corrective press later.
                _boilerVerification.Reset();
            }

            // We have no belief about the boiler to hold while something else is driving it.
            _boilerOn = false;
            return;
        }

        if (pathChanged)
        {
            _logger.LogInformation(
                "Boiler control is ENABLED: device settings say the boiler is wired to the fingerbot. Assuming the boiler is currently OFF. Triggered by {Trigger}",
                trigger);
        }

        // Sum how far each room with a radiator valve is below its target. A room at or above its
        // target contributes nothing: it is disregarded, not a credit against the cold rooms.
        List<(string RoomName, double Deficit)> roomsBelowTarget = [];
        List<(string RoomName, ICustomClimateControlEntity Valve)> activeValves = [];
        double totalDeficit = 0;

        foreach (RoomSchedule roomSchedule in schedules.Rooms)
        {
            object[] devices = GetSwitchesForRoom(roomSchedule);
            List<ICustomClimateControlEntity> radiatorValves = devices.OfType<ICustomClimateControlEntity>().ToList();

            if (radiatorValves.Count == 0)
                continue;

            _roomStates.TryGetValue(roomSchedule.Id, out RoomState? state);

            // A room whose thermometer has no reading has no known deficit, so it adds nothing.
            if (state is { CurrentTemperature: double current, TargetTemperature: double target } && target > current)
            {
                double deficit = target - current;
                roomsBelowTarget.Add((roomSchedule.Name, deficit));
                totalDeficit += deficit;
            }

            if (state is { HeatingActive: true })
            {
                foreach (ICustomClimateControlEntity valve in radiatorValves)
                {
                    activeValves.Add((roomSchedule.Name, valve));
                    _boilerVerification.NotifyValveStartedHeating(roomSchedule.Name, valve);
                }
            }
            else
            {
                _boilerVerification.NotifyValveStopped(roomSchedule.Name);
            }
        }

        // Check if a previous verification has failed and we need to correct the boiler state
        bool? verifiedState = _boilerVerification.GetVerifiedBoilerState();
        if (verifiedState.HasValue && verifiedState.Value != _boilerOn)
        {
            _logger.LogWarning(
                "Boiler verification detected state mismatch: we thought boiler was {Expected}, but it appears to be {Actual}. Correcting by toggling fingerbot",
                _boilerOn ? "ON" : "OFF", verifiedState.Value ? "ON" : "OFF");

            _boilerOn = verifiedState.Value;
            ToggleBoilerFingerbot();
            _boilerOn = !verifiedState.Value;

            _boilerVerification.NotifyBoilerToggled(_boilerOn, activeValves);
        }

        if (!_boilerOn)
        {
            // Boiler is OFF - should we turn it ON?
            if (totalDeficit >= BoilerDeficitThresholdCelsius)
            {
                ToggleBoilerFingerbot();
                _boilerOn = true;
                _logger.LogInformation(
                    "Boiler turned ON (total deficit {Deficit:0.0}°C, threshold {Threshold}°C). Triggered by {Trigger}. Rooms: {Rooms}",
                    totalDeficit, BoilerDeficitThresholdCelsius, trigger,
                    string.Join(", ", roomsBelowTarget.Select(r => $"{r.RoomName} ({r.Deficit:0.0}°C)")));

                _boilerVerification.NotifyBoilerToggled(true, activeValves);
            }
        }
        else
        {
            // Boiler is ON - should we turn it OFF?
            if (totalDeficit <= 0)
            {
                ToggleBoilerFingerbot();
                _boilerOn = false;
                _logger.LogInformation("Boiler turned OFF (no room with a radiator valve is below its target). Triggered by {Trigger}", trigger);

                _boilerVerification.NotifyBoilerToggled(false, activeValves);
            }
        }

        // Run the verification check
        await _boilerVerification.CheckAsync();
    }

    /// <summary>
    /// Which control path the boiler is wired to, according to device settings. Nothing in Home
    /// Assistant can read the physical switch at the boiler, so this is configuration, not a
    /// reading, and anything unset means "leave the boiler alone".
    /// </summary>
    private async Task<BoilerControlPath> GetBoilerControlPathAsync()
    {
        try
        {
            DeviceSettingsDto? settings = await _deviceSettings.GetSettingsAsync();
            return settings?.Heating?.BoilerControlPath ?? BoilerControlPath.ExternalScheduler;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading the boiler control path from device settings - leaving the boiler alone");
            return BoilerControlPath.ExternalScheduler;
        }
    }

    private void ToggleBoilerFingerbot()
    {
        if (_namedEntities.BoilerFingerbot.IsOn())
        {
            _namedEntities.BoilerFingerbot.TurnOff();
        }
        else
        {
            _namedEntities.BoilerFingerbot.TurnOn();
        }
    }

    private bool EvaluateHeaterConditions(HeaterConditions conditions)
    {
        return EvaluateHeaterConditions(conditions, _housePresenceSensor.State == true, _timeProvider.GetLocalNow().TimeOfDay);
    }

    internal static bool EvaluateHeaterConditions(HeaterConditions conditions, bool housePresent, TimeSpan now)
    {
        // Check power override first (if configured)
        // Only trust power reading if the switch is actually on (plugs report stale power when off)
        bool switchIsOn = conditions.PowerOverrideSwitch?.IsOn() == true;
        if (switchIsOn && conditions.PowerOverrideSensor?.State >= conditions.PowerOverrideThreshold)
        {
            return true;
        }

        // House presence check
        if (conditions.RequireHousePresence && !housePresent)
        {
            return false;
        }

        // Time-based check. No windows configured means any time of day.
        if (conditions.AllowedTimeWindows is null)
        {
            return true;
        }

        return conditions.AllowedTimeWindows.Any(window => now >= window.Start && now < window.End);
    }

    private HeaterConditions GamesRoomHeaterConditions => new()
    {
        RequireHousePresence = true,
        PowerOverrideSwitch = _namedEntities.GamesRoomDeskPlugOnOff,
        PowerOverrideSensor = _namedEntities.GamesRoomDeskPlugPower,
        PowerOverrideThreshold = 30
    };

    private HeaterConditions DiningRoomHeaterConditions => new()
    {
        RequireHousePresence = true,
        PowerOverrideSwitch = _namedEntities.DiningRoomDeskPlugOnOff,
        PowerOverrideSensor = _namedEntities.DiningRoomDeskPlugPower,
        PowerOverrideThreshold = 30
    };

    private HeaterConditions KitchenHeaterConditions => new()
    {
        RequireHousePresence = true,
        AllowedTimeWindows = [new(new TimeSpan(6, 0, 0), new TimeSpan(21, 0, 0))]
    };

    private HeaterConditions Bedroom1HeaterConditions => new()
    {
        RequireHousePresence = true,
        AllowedTimeWindows =
        [
            new(new TimeSpan(8, 0, 0), new TimeSpan(8, 30, 0)),
            new(new TimeSpan(21, 0, 0), new TimeSpan(21, 15, 0))
        ]
    };
}

internal record TimeWindow(TimeSpan Start, TimeSpan End);

internal class HeaterConditions
{
    public bool RequireHousePresence { get; init; } = true;
    public ICustomSwitchEntity? PowerOverrideSwitch { get; init; }
    public ICustomNumericSensorEntity? PowerOverrideSensor { get; init; }
    public double PowerOverrideThreshold { get; init; } = 30;
    /// <summary>Times of day the plug may be turned on. Null means any time.</summary>
    public List<TimeWindow>? AllowedTimeWindows { get; init; }
}

internal record HeatingDevice(object Device, HeaterConditions? Conditions = null)
{
    public string DeviceName => Device switch
    {
        ICustomSwitchEntity sw => sw.EntityId,
        ICustomClimateControlEntity cc => cc.EntityId,
        _ => "Unknown"
    };
}
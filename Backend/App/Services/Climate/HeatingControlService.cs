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
    private readonly IBoilerStatePersistence _boilerStatePersistence;
    private const int _recheckEveryXMinutes = 5;
    internal const double HysteresisOffset = 0.2;
    // The boiler is switched on once the rooms with a radiator valve are, between them, this many
    // degrees Celsius short of their targets, and off again only when none is short at all.
    internal const double BoilerDeficitThresholdCelsius = 3;
    private readonly Dictionary<int, RoomState> _roomStates = [];
    private bool _hasUploadedState = false;
    // What the app believes the boiler is doing in fingerbot mode, and since when (null when unknown).
    // The boiler has no feedback; see BoilerBelief.
    private bool _boilerOn = false;
    private DateTimeOffset? _boilerBeliefSince = null;
    // Set by a corrective press, cleared by any other. A verification that disagrees again straight
    // after a correction is not trusted with another press, or a radiator that never cools for some
    // other reason (sun, a second heat source) would have the boiler pressed every 90 minutes.
    private bool _lastBoilerPressWasCorrection = false;
    private bool _hasLoggedFingerbotUnavailable = false;

    // Fingerbot movements the app did not command (the HA UI, a script, an automation). Guarded by
    // _fingerbotLock because state changes arrive outside the evaluation semaphore.
    private readonly object _fingerbotLock = new();
    private bool? _lastSeenFingerbotState;
    private bool? _expectedFingerbotState;
    private int _pressesSeenOutsideTheApp = 0;

    /// <summary>Just over the longest steady-draw rule (the living room TV, Ruth's desk), so a re-evaluation sees it settled.</summary>
    private static readonly TimeSpan PresencePowerSettleDelay =
        (PresenceService.LivingRoomTvSteadyFor > PresenceService.DiningDeskSteadyFor ? PresenceService.LivingRoomTvSteadyFor : PresenceService.DiningDeskSteadyFor)
        + TimeSpan.FromSeconds(1);
    private BoilerControlPath? _lastSeenBoilerControlPath = null;
    private static readonly SemaphoreSlim _evaluationSemaphore = new(1, 1);

    // Set for each pass over the rooms: in fingerbot mode, while the boiler is believed off, radiator valves
    // are not moved at all, neither opened nor shut. With no hot water behind them their position makes no
    // difference, and every move spends the TRV's battery (owner, 2026-10-19). The boiler is decided before
    // the rooms, so they are brought into line in the same pass that turns it on. In boiler-thermostat mode
    // the app cannot know when the boiler runs, so valves follow their rooms as before.
    private bool _valvesWaitForBoiler = false;

    // Set when a turn-off verification finds the radiators still hot, so the boiler may be running although
    // the app believes it off. Until the app next turns the boiler on or off itself, valves follow their rooms
    // again, shutting those that are warm enough: that evidence is the one reason to move them with the
    // boiler believed off.
    private bool _boilerMayBeRunning = false;

    // Valve rooms calling for heat from the boiler. A room starts calling once it is HysteresisOffset below its
    // target (where its valve would open) and stops at its target; the boiler goes off when none is calling.
    // So a room a fraction short that never started calling cannot hold the boiler on with every valve shut.
    private readonly HashSet<int> _roomsCallingForHeat = [];

    // Rooms whose heating EvaluateSchedule switched in the current pass; EnforceDeviceRulesAsync skips them.
    private readonly HashSet<int> _roomsSwitchedThisPass = [];

    // Refreshed at the start of every evaluation pass (PrepareDeviceRulesAsync): the per-device rules in
    // force, whether they are the built-in defaults, and which presence sources are in use right now.
    private Dictionary<string, DeviceRulesDto>? _deviceRules;
    private bool _deviceRulesAreDefault = true;
    private Dictionary<string, bool> _presenceInUse = new(StringComparer.OrdinalIgnoreCase);

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
        IDeviceSettingsPersistenceService deviceSettings,
        IBoilerStatePersistence boilerStatePersistence)
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
        _boilerStatePersistence = boilerStatePersistence;
        _lastSeenFingerbotState = ReadFingerbotState();

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

        // Room presence from TVs and lights (PresenceService). A TV is normally switched with its
        // remote, not its plug, so its power draw is what changes.
        ReevaluateWhenPowerCrosses(_namedEntities.DenTvPlugPower, PresenceService.DenTvInUseWatts, "den TV");
        ReevaluateWhenPowerCrosses(_namedEntities.LivingRoomTvPlugPower, PresenceService.LivingRoomTvInUseWatts, "living room TV");
        ReevaluateWhenPowerCrosses(_namedEntities.DiningRoomDeskPlugPower, PresenceService.DiningDeskInUseWatts, "Ruth's desk");
        _namedEntities.LivingRoomStickLightsPlugOnOff.SubscribeToStateChangesAsync(async change => await EvaluateAllSchedules("living room stick lights switched"));

        // Subscribe to power changes
        _homeBattery.OnBatteryChargePercentChanged(async _ => await EvaluateAllSchedules("home battery charge changed"));
        _electricityMeter.OnCurrentRatePerKwhChanged(async _ => await EvaluateAllSchedules("electricity import rate changed"));

        WatchBoilerFingerbot();
    }

    /// <summary>
    /// Notices the fingerbot moving when the app did not tell it to, so a press from the HA UI, a
    /// script or an automation flips the boiler belief instead of silently inverting it. The
    /// fingerbot's HA state latches (after a press on 2026-10-19 it stayed "on" and never reverted), so any
    /// on/off change the app did not command is someone else's press. A press made by hand on the
    /// boiler itself goes through no device and cannot be seen.
    /// </summary>
    internal void WatchBoilerFingerbot()
    {
        _namedEntities.BoilerFingerbot.SubscribeToStateChangesAsync(async _ => await OnBoilerFingerbotStateChanged());
    }

    private async Task OnBoilerFingerbotStateChanged()
    {
        bool pressedOutsideTheApp = false;

        lock (_fingerbotLock)
        {
            bool? previous = _lastSeenFingerbotState;
            bool? current = ReadFingerbotState();
            _lastSeenFingerbotState = current;

            // Going unavailable or coming back is not a press.
            if (current.HasValue && previous.HasValue && current != previous)
            {
                if (_expectedFingerbotState == current)
                {
                    _expectedFingerbotState = null;
                }
                else
                {
                    _pressesSeenOutsideTheApp++;
                    pressedOutsideTheApp = true;
                }
            }
        }

        if (pressedOutsideTheApp)
        {
            await EvaluateAllSchedules("boiler fingerbot pressed outside the app");
        }
    }

    /// <summary>The fingerbot's reported switch state, or null when it is unavailable or unknown.</summary>
    private bool? ReadFingerbotState()
    {
        if (_namedEntities.BoilerFingerbot.IsOn())
            return true;
        if (_namedEntities.BoilerFingerbot.IsOff())
            return false;
        return null;
    }

    /// <summary>
    /// Re-evaluates schedules shortly after a power reading crosses a presence threshold, in either
    /// direction, rather than on every reading: plugs report every few seconds. The delay lets a "must
    /// have drawn this for N seconds" rule (the living room TV) see the crossing as settled; for a
    /// look-back rule (the den) it is merely harmless.
    /// </summary>
    private void ReevaluateWhenPowerCrosses(ICustomNumericSensorEntity power, double thresholdWatts, string what)
    {
        bool wasDrawing = false;
        power.SubscribeToStateChangesAsync(reading =>
        {
            bool drawing = reading.State >= thresholdWatts;
            if (drawing != wasDrawing)
            {
                wasDrawing = drawing;
                string trigger = drawing ? $"{what} turned on" : $"{what} turned off";
                _scheduler.Schedule(PresencePowerSettleDelay, async () => await EvaluateAllSchedules(trigger));
            }

            return Task.CompletedTask;
        });
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
            await PrepareDeviceRulesAsync();
            RoomSchedules schedules = await LoadSchedulesAsync();

            // 1. The boiler's mode and belief: a mode change, the saved belief at startup, outside presses.
            BoilerControlPath controlPath = await UpdateBoilerControlPathAsync(trigger);
            bool fingerbotMode = controlPath == BoilerControlPath.Fingerbot;

            // 2. What every room wants, before anything moves.
            Dictionary<int, RoomTarget> targets = [];
            foreach (RoomSchedule schedule in schedules.Rooms)
            {
                RoomTarget? target = await ResolveTargetAsync(schedules.HouseOccupancyState, schedule);
                if (target != null)
                {
                    targets[schedule.Id] = target;
                }
            }

            // 3. The boiler, decided and pressed before any valve moves, so that valves are only moved when
            //    there may be hot water behind them.
            if (fingerbotMode)
            {
                DecideBoiler(schedules, targets, trigger);
            }

            // 4. The rooms' devices.
            _valvesWaitForBoiler = fingerbotMode && !_boilerOn && !_boilerMayBeRunning;
            _roomsSwitchedThisPass.Clear();
            bool hasStateChanged = false;
            foreach (RoomSchedule schedule in schedules.Rooms)
            {
                if (targets.TryGetValue(schedule.Id, out RoomTarget? target))
                {
                    hasStateChanged |= await EvaluateSchedule(schedule, target, trigger);
                }

                // A room just switched already set each device by its rules, and HA has not reported the new
                // states back yet, so enforcing now would read the old ones and send every command twice.
                if (!_roomsSwitchedThisPass.Contains(schedule.Id))
                {
                    hasStateChanged |= await EnforceDeviceRulesAsync(schedule);
                }
            }

            // 5. What the valves did this pass, for the boiler verification, and the verification itself.
            if (fingerbotMode)
            {
                await RecordValvesAndVerifyAsync(schedules);
            }

            if (hasStateChanged)
            {
                await UpdateRoomStatesAsync();
            }
        }
        catch (Exception ex)
        {
            // Evaluations run from timers and event handlers, where an exception would vanish without trace.
            _logger.LogError(ex, "Heating evaluation failed. Triggered by {Trigger}", trigger);
        }
        finally
        {
            _evaluationSemaphore.Release();
        }
    }

    /// <summary>A room's target this pass and why, with the thermometer reading it is judged against.</summary>
    private sealed record RoomTarget(double CurrentTemperature, double Temperature, HeatingScheduleTrack? Track, string Reason);

    /// <summary>
    /// Whether a boost (the owner's, a group boost or a guest's QR override) is running now. Compares
    /// instants, not clock faces: the end is a DateTimeOffset (the UI sends it in UTC), and comparing its
    /// .DateTime to the local wall clock ended every boost an hour early in British Summer Time.
    /// </summary>
    private bool IsBoostRunning(RoomSchedule room)
    {
        return room.Boost is { EndTime: DateTimeOffset boostEnd, Temperature: not null }
            && boostEnd > _timeProvider.GetUtcNow();
    }

    /// <summary>
    /// What the room should be at right now (a boost, the active track, or a pre-heat track) and its
    /// current temperature. Null when there is no reading, nothing to control, or no track applies.
    /// </summary>
    private async Task<RoomTarget?> ResolveTargetAsync(HouseOccupancyState houseOccupancyState, RoomSchedule roomHeatingSchedule)
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
            return null;
        }

        // A room with nothing to switch is not evaluated at all.
        if ((roomHeatingSchedule.OnToggleHeating ?? GetOnToggleFunc(roomHeatingSchedule)) == null)
        {
            return null;
        }

        // Step 0: Is there a boost active?
        bool hasBoost = IsBoostRunning(roomHeatingSchedule);
        double desiredTemperature;
        string reason;
        HeatingScheduleTrack? effectiveTrack = null;

        // A boost — the owner's, a group boost, or a guest's QR override — sets the exact temperature, up or
        // down, until it ends (owner, 2026-10-19). Per-device rules still decide whether each device may run.
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
                return null;
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

        return new RoomTarget(currentTemperature.Value, desiredTemperature, effectiveTrack, reason);
    }

    private async Task<bool> EvaluateSchedule(RoomSchedule roomHeatingSchedule, RoomTarget target, string trigger)
    {
        // Get the toggle action, either from the supplied action, or the service's own function.
        Func<bool, Task<bool>>? onToggleHeating = roomHeatingSchedule.OnToggleHeating ?? GetOnToggleFunc(roomHeatingSchedule);
        if (onToggleHeating == null)
        {
            return false;
        }

        double currentTemperature = target.CurrentTemperature;
        double desiredTemperature = target.Temperature;
        HeatingScheduleTrack? effectiveTrack = target.Track;
        string reason = target.Reason;

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
            else if (IsWaitingForBoiler(roomControlDevice))
            {
                // An open valve with the boiler off is not heating the room, whatever its setpoint says.
                thisDeviceHeatingState = false;
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
        double? previousTargetTemperature = roomState.TargetTemperature;
        int previousTrackId = roomState.ActiveScheduleTrackId;
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
                _roomsSwitchedThisPass.Add(roomHeatingSchedule.Id);
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
                _roomsSwitchedThisPass.Add(roomHeatingSchedule.Id);
            }
        }

        // Upload when the temperature, the target or the track changed. Until 2026-10-19 the track was
        // compared after it had been overwritten (so never differed) and the target not at all, so after a
        // restart the UI showed no targets until some thermometer happened to change.
        if (roomState.CurrentTemperature != previousRoomTemperature
            || roomState.TargetTemperature != previousTargetTemperature
            || roomState.ActiveScheduleTrackId != previousTrackId)
        {
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

    /// <summary>
    /// Switches a zone's heating on or off. Each device only comes on if its own rules allow it (a device
    /// asked to come on while its rules forbid it is held off), and a radiator valve is not moved at all
    /// while the boiler is off (see <see cref="_valvesWaitForBoiler"/>). So "on" reports a change only when
    /// at least one device actually started heating.
    /// </summary>
    private Func<bool, Task<bool>>? GetOnToggleFunc(RoomSchedule roomHeatingSchedule)
    {
        List<(object Device, Func<bool, Task<bool>> Toggle)> toggles = [];
        foreach (object device in GetSwitchesForRoom(roomHeatingSchedule))
        {
            Func<bool, Task<bool>>? toggle = GetOnToggleFuncForSingleDevice(device);
            if (toggle != null)
            {
                toggles.Add((device, toggle));
            }
        }

        if (toggles.Count == 0)
        {
            return null;
        }

        return async (value) =>
        {
            bool anyReachedRequestedState = false;
            foreach ((object device, Func<bool, Task<bool>> toggle) in toggles)
            {
                if (IsWaitingForBoiler(device))
                {
                    // With the boiler off a valve's position makes no difference, so it is neither opened
                    // nor shut: either would spend the TRV's battery for nothing. It is brought into line
                    // in the pass that turns the boiler on.
                    continue;
                }

                bool desired = value && IsDeviceAllowed(device);
                bool changed = await toggle(desired);
                anyReachedRequestedState |= changed && desired == value;
            }

            return anyReachedRequestedState;
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
            // Heater plugs are listed bare: whether each may run is decided by its device rules
            // (DeviceRulesEvaluator, defaults in DefaultDeviceRules), not by a wrapper on the switch.
            "kitchen" => [_namedEntities.KitchenHeaterSmartPlugOnOff],

            // The dining room and the den each split into two independently scheduled
            // zones: a desk (heater plug, presence-aware) and the room itself (radiator).
            // See the Heating section of CLAUDE.md.
            "dining desk" => [_namedEntities.DiningRoomHeaterSmartPlugOnOff],
            "dining room" => [_namedEntities.DiningRoomRadiatorThermostat],
            "games desk" => [_namedEntities.GamesRoomHeaterSmartPlugOnOff],

            // The den's two radiators (paired 2026-10-18).
            "den" => [_namedEntities.DenRadiator1Thermostat, _namedEntities.DenRadiator2Thermostat],

            "living room" => [_namedEntities.LivingRoomRadiatorThermostat],
            "downstairs bathroom" => [],
            "bedroom 1" => [_namedEntities.Bedroom1HeaterSmartPlugOnOff, _namedEntities.Bedroom1RadiatorThermostat],
            "bedroom 2" => [_namedEntities.Bedroom2RadiatorThermostat],
            "bedroom 3" => [_namedEntities.Bedroom3RadiatorThermostat],
            // Thermometer only: nothing to drive, so no boost, schedules or share link in the UI (owner,
            // 2026-10-19). It used to have a dummy switch here, which made the UI offer controls that did nothing.
            "upstairs bathroom" => [],
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
    /// cannot drift from what the heating code actually drives and reads.
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
                    AttachRules(valveDevice);
                    devices.Add(valveDevice);
                    break;
                case ICustomSwitchEntity plug:
                    RoomDeviceDto plugDevice = DescribeDevice(plug.EntityId, RoomDeviceRoles.HeaterPlug);
                    AttachRules(plugDevice);
                    devices.Add(plugDevice);
                    break;
            }
        }

        foreach ((string entityId, string role) in _presenceService.GetPresenceDevices(room.Name))
        {
            devices.Add(DescribeDevice(entityId, role));
        }

        return devices;
    }

    /// <summary>Publishes the rules currently applied to a drivable device, so the UI can show and edit them.</summary>
    private void AttachRules(RoomDeviceDto device)
    {
        device.Rules = GetRulesFor(device.EntityId);
        device.RulesAreDefault = _deviceRulesAreDefault;
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

    /// <summary>
    /// Reads the boiler control path and brings the belief up to date with it: a mode change, the saved
    /// belief at startup, and fingerbot movements the app did not command. Runs before the rooms, so they
    /// see the belief this pass starts from.
    /// </summary>
    private async Task<BoilerControlPath> UpdateBoilerControlPathAsync(string trigger)
    {
        BoilerControlPath controlPath = await GetBoilerControlPathAsync();
        BoilerControlPath? previousControlPath = _lastSeenBoilerControlPath;
        bool pathChanged = controlPath != previousControlPath;
        _lastSeenBoilerControlPath = controlPath;
        int pressesSeenOutsideTheApp = TakePressesSeenOutsideTheApp();

        if (controlPath == BoilerControlPath.BoilerThermostat)
        {
            if (pathChanged)
            {
                // Logged once per transition: this runs on every evaluation cycle.
                _logger.LogInformation(
                    "Boiler control is DISABLED: the house is on its boiler thermostat, which leaves the boiler's button dead, so a fingerbot press would do nothing. Room-level control (valves and heater plugs) continues as normal. Change this in device settings when the switch at the boiler is flipped. Triggered by {Trigger}",
                    trigger);

                // Drop anything left over from fingerbot mode so a stale verification cannot
                // resurface and provoke a corrective press later.
                _boilerVerification.Reset();
            }

            // We have no belief about the boiler to hold while something else is driving it, and a
            // fingerbot press seen in this mode hit a dead button. Nothing is saved, so the belief
            // last saved in fingerbot mode is left alone.
            _boilerOn = false;
            _boilerBeliefSince = null;
            _boilerMayBeRunning = false;
            return controlPath;
        }

        if (pathChanged)
        {
            // Only at startup does a saved belief describe the boiler now. Coming from the boiler
            // thermostat, the thermostat has been driving it, so whatever was saved is stale.
            BoilerBelief? savedBelief = previousControlPath == null ? _boilerStatePersistence.Load() : null;

            if (savedBelief != null)
            {
                _boilerOn = savedBelief.BoilerOn;
                _boilerBeliefSince = savedBelief.Since;
                _logger.LogInformation(
                    "Boiler control is ENABLED: device settings say the boiler is wired to the fingerbot. Carrying on from the saved belief that the boiler has been {State} since {Since:O}. Triggered by {Trigger}",
                    _boilerOn ? "ON" : "OFF", savedBelief.Since, trigger);
            }
            else
            {
                SetBoilerBelief(false, _timeProvider.GetUtcNow());
                _logger.LogInformation(
                    "Boiler control is ENABLED: device settings say the boiler is wired to the fingerbot. Assuming the boiler is currently OFF. Triggered by {Trigger}",
                    trigger);
            }

            _lastBoilerPressWasCorrection = false;
        }

        if (pressesSeenOutsideTheApp > 0)
        {
            // Each fingerbot movement is one press of a toggle button.
            if (pressesSeenOutsideTheApp % 2 == 1)
            {
                SetBoilerBelief(!_boilerOn, _timeProvider.GetUtcNow());
            }

            _logger.LogWarning(
                "The boiler fingerbot was pressed {Count} time(s) outside the app. Now believing the boiler is {State}. Triggered by {Trigger}",
                pressesSeenOutsideTheApp, _boilerOn ? "ON" : "OFF", trigger);

            // Any pending verification was judging the old state and would "correct" the new one.
            _boilerVerification.Reset();
            _lastBoilerPressWasCorrection = false;
        }

        return controlPath;
    }

    /// <summary>
    /// Fingerbot mode only. Corrects the belief if a verification has failed, then turns the boiler on or off
    /// from what the rooms want this pass, before any valve has moved, so the rooms can then move their valves
    /// only if there may be hot water behind them.
    /// </summary>
    private void DecideBoiler(RoomSchedules schedules, Dictionary<int, RoomTarget> targets, string trigger)
    {
        // Sum how far each room with a radiator valve is below its target. A room at or above its
        // target contributes nothing: it is disregarded, not a credit against the cold rooms.
        List<(string RoomName, double Deficit)> roomsBelowTarget = [];
        // Open valves that can take heat: the radiators a turn-off verification watches cool.
        List<(string RoomName, ICustomClimateControlEntity Valve)> openValves = [];
        // Valves of the rooms calling for heat: the radiators a turn-on verification watches warm.
        List<(string RoomName, ICustomClimateControlEntity Valve)> callingValves = [];
        // A running boost starts the boiler on its own, whatever the total (owner, 2026-10-19).
        List<string> boostedRoomsCalling = [];
        // So does a room someone is in: its active track needs "room in use", which is only selected while the
        // room's presence says so (the den's TV, a desk). Empty rooms still wait for the total (owner, 2026-10-19).
        List<string> occupiedRoomsCalling = [];
        double totalDeficit = 0;

        _roomsCallingForHeat.RemoveWhere(id => !schedules.Rooms.Exists(r => r.Id == id));

        foreach (RoomSchedule roomSchedule in schedules.Rooms)
        {
            // Only valves their device rules currently allow can take heat from the boiler; a room whose
            // valves are all held off by their rules adds no demand.
            List<ICustomClimateControlEntity> radiatorValves = [.. GetSwitchesForRoom(roomSchedule).OfType<ICustomClimateControlEntity>().Where(IsDeviceAllowed)];

            // A room with no such valve, or no reading or target, has no known demand.
            if (radiatorValves.Count == 0 || !targets.TryGetValue(roomSchedule.Id, out RoomTarget? target))
            {
                _roomsCallingForHeat.Remove(roomSchedule.Id);
                continue;
            }

            openValves.AddRange(radiatorValves.Where(IsValveOpen).Select(valve => (roomSchedule.Name, valve)));

            double current = target.CurrentTemperature;
            if (target.Temperature > current)
            {
                double deficit = target.Temperature - current;
                roomsBelowTarget.Add((roomSchedule.Name, deficit));
                totalDeficit += deficit;
            }

            if (current <= target.Temperature - HysteresisOffset)
            {
                _roomsCallingForHeat.Add(roomSchedule.Id);
            }
            else if (current >= target.Temperature)
            {
                _roomsCallingForHeat.Remove(roomSchedule.Id);
            }

            if (_roomsCallingForHeat.Contains(roomSchedule.Id))
            {
                callingValves.AddRange(radiatorValves.Select(valve => (roomSchedule.Name, valve)));

                if (IsBoostRunning(roomSchedule))
                {
                    boostedRoomsCalling.Add(roomSchedule.Name);
                }
                else if (target.Track is { } track && track.Conditions.HasFlag(ConditionType.RoomInUse))
                {
                    occupiedRoomsCalling.Add(roomSchedule.Name);
                }
            }
        }

        // Check if a previous verification has failed and we need to correct the boiler state
        bool? verifiedState = _boilerVerification.GetVerifiedBoilerState();
        if (verifiedState.HasValue && verifiedState.Value != _boilerOn)
        {
            if (verifiedState.Value)
            {
                // Evidence that a turn-off did not work: the radiators stayed hot. Until the app next turns the
                // boiler on or off itself, valves follow their rooms again, so the warm rooms' valves shut.
                _boilerMayBeRunning = true;
            }

            if (_lastBoilerPressWasCorrection)
            {
                _logger.LogWarning(
                    "Boiler verification disagreed again straight after a corrective press: it thinks the boiler is {Actual}, we think {Expected}. Not pressing again, because a radiator that is not responding for some other reason would otherwise have the boiler pressed every 90 minutes. Keeping the belief until the next normal press",
                    verifiedState.Value ? "ON" : "OFF", _boilerOn ? "ON" : "OFF");
            }
            else
            {
                _logger.LogWarning(
                    "Boiler verification detected state mismatch: we thought boiler was {Expected}, but it appears to be {Actual}. Correcting by toggling fingerbot",
                    _boilerOn ? "ON" : "OFF", verifiedState.Value ? "ON" : "OFF");

                // The boiler has really been in the verified state since the press we believed in.
                DateTimeOffset? actuallySince = _boilerBeliefSince;

                if (PressBoilerFingerbot())
                {
                    // The press puts the boiler where we believed it was.
                    SetBoilerBelief(_boilerOn, _timeProvider.GetUtcNow());
                    _lastBoilerPressWasCorrection = true;
                    _boilerVerification.NotifyBoilerToggled(_boilerOn, _boilerOn ? [.. openValves.Concat(callingValves).DistinctBy(v => v.Valve.EntityId)] : openValves, actuallySince);
                }
                else
                {
                    // Nothing could be pressed, so the verification is the best evidence there is.
                    SetBoilerBelief(verifiedState.Value, actuallySince ?? _timeProvider.GetUtcNow());
                }
            }
        }

        if (!_boilerOn)
        {
            // Boiler is OFF - should we turn it ON?
            bool deficitReached = totalDeficit >= BoilerDeficitThresholdCelsius;
            if (_roomsCallingForHeat.Count > 0 && (deficitReached || boostedRoomsCalling.Count > 0 || occupiedRoomsCalling.Count > 0))
            {
                string why = deficitReached ? "deficit reached the threshold"
                    : boostedRoomsCalling.Count > 0 ? $"boost running in {string.Join(", ", boostedRoomsCalling)}"
                    : $"in use: {string.Join(", ", occupiedRoomsCalling)}";

                DateTimeOffset? offSince = _boilerBeliefSince;

                if (PressBoilerFingerbot())
                {
                    SetBoilerBelief(true, _timeProvider.GetUtcNow());
                    _lastBoilerPressWasCorrection = false;
                    _boilerMayBeRunning = false;
                    _logger.LogInformation(
                        "Boiler turned ON ({Why}; total deficit {Deficit:0.0}°C, threshold {Threshold}°C). Triggered by {Trigger}. Rooms: {Rooms}",
                        why,
                        totalDeficit, BoilerDeficitThresholdCelsius, trigger,
                        string.Join(", ", roomsBelowTarget.Select(r => $"{r.RoomName} ({r.Deficit:0.0}°C)")));

                    // Watch the valves that open with this press as well as any already open: with the boiler
                    // off they had no hot water behind them, so they are the radiators that should now warm.
                    List<(string RoomName, ICustomClimateControlEntity Valve)> valvesToWatch =
                        [.. openValves.Concat(callingValves).DistinctBy(v => v.Valve.EntityId)];
                    _boilerVerification.NotifyBoilerToggled(true, valvesToWatch, offSince);
                }
            }
        }
        else if (_roomsCallingForHeat.Count == 0)
        {
            // Boiler is ON and no room is calling for heat: turn it OFF. The valves are left where they are
            // (see _valvesWaitForBoiler), and the open ones are the radiators the verification watches cool.
            if (PressBoilerFingerbot())
            {
                SetBoilerBelief(false, _timeProvider.GetUtcNow());
                _lastBoilerPressWasCorrection = false;
                _boilerMayBeRunning = false;
                _logger.LogInformation("Boiler turned OFF (no room with a radiator valve is calling for heat). Triggered by {Trigger}", trigger);

                _boilerVerification.NotifyBoilerToggled(false, openValves, null);
            }
        }
    }

    /// <summary>
    /// Tells the verification which radiators have hot water behind them this pass (an open valve while the
    /// boiler is believed on) and which do not, then runs it. Called after any press and after the rooms have
    /// moved their valves, so a valve that has just opened still shows how long its radiator was cold.
    /// </summary>
    private async Task RecordValvesAndVerifyAsync(RoomSchedules schedules)
    {
        foreach (RoomSchedule roomSchedule in schedules.Rooms)
        {
            List<ICustomClimateControlEntity> radiatorValves = [.. GetSwitchesForRoom(roomSchedule).OfType<ICustomClimateControlEntity>()];
            if (radiatorValves.Count == 0)
            {
                continue;
            }

            List<ICustomClimateControlEntity> heating = _boilerOn ? [.. radiatorValves.Where(IsValveOpen)] : [];
            if (heating.Count == 0)
            {
                _boilerVerification.NotifyValveStopped(roomSchedule.Name);
                continue;
            }

            foreach (ICustomClimateControlEntity valve in heating)
            {
                _boilerVerification.NotifyValveStartedHeating(roomSchedule.Name, valve);
            }
        }

        await _boilerVerification.CheckAsync();
    }

    /// <summary>A valve is open when its setpoint is at or above its own reading, which is how the app sets it.</summary>
    private static bool IsValveOpen(ICustomClimateControlEntity valve) => valve.TargetTemperature >= valve.CurrentTemperature;

    private void SetBoilerBelief(bool boilerOn, DateTimeOffset since)
    {
        _boilerOn = boilerOn;
        _boilerBeliefSince = since;
        _boilerStatePersistence.Save(new BoilerBelief(boilerOn, since));
    }

    private int TakePressesSeenOutsideTheApp()
    {
        lock (_fingerbotLock)
        {
            int presses = _pressesSeenOutsideTheApp;
            _pressesSeenOutsideTheApp = 0;
            return presses;
        }
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
            return settings?.Heating?.BoilerControlPath ?? BoilerControlPath.BoilerThermostat;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading the boiler control path from device settings - leaving the boiler alone");
            return BoilerControlPath.BoilerThermostat;
        }
    }

    /// <summary>
    /// Presses the boiler's button by flipping the fingerbot's switch state; each change is one press.
    /// Returns false, pressing nothing, when the fingerbot is unavailable: HA accepts a command for an
    /// unreachable device with HTTP 200, so pressing anyway would flip the belief with nothing moved.
    /// </summary>
    private bool PressBoilerFingerbot()
    {
        bool? fingerbotState = ReadFingerbotState();

        if (fingerbotState == null)
        {
            if (!_hasLoggedFingerbotUnavailable)
            {
                _logger.LogWarning(
                    "The boiler fingerbot is unavailable, so the boiler cannot be pressed. Keeping the belief that it is {State} until the fingerbot comes back",
                    _boilerOn ? "ON" : "OFF");
                _hasLoggedFingerbotUnavailable = true;
            }

            return false;
        }

        _hasLoggedFingerbotUnavailable = false;

        lock (_fingerbotLock)
        {
            _expectedFingerbotState = !fingerbotState.Value;
        }

        if (fingerbotState.Value)
        {
            _namedEntities.BoilerFingerbot.TurnOff();
        }
        else
        {
            _namedEntities.BoilerFingerbot.TurnOn();
        }

        return true;
    }

    /// <summary>
    /// Loads the per-device rules and works out which presence sources are in use, once per evaluation
    /// pass. Saved rules (device settings <c>heating.deviceRules</c>) win; if none have ever been saved, or
    /// the settings cannot be read, the built-in defaults apply, which still keep the heaters restricted.
    /// </summary>
    private async Task PrepareDeviceRulesAsync()
    {
        Dictionary<string, DeviceRulesDto>? saved = null;
        try
        {
            DeviceSettingsDto? settings = await _deviceSettings.GetSettingsAsync();
            saved = settings?.Heating?.DeviceRules;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read device rules from device settings - using the built-in defaults");
        }

        _deviceRulesAreDefault = saved == null;
        _deviceRules = saved != null
            ? new Dictionary<string, DeviceRulesDto>(saved, StringComparer.OrdinalIgnoreCase)
            : DefaultDeviceRules.For(_namedEntities);

        // Presence is only worth looking up (it costs a history query per zone) if some rule asks for it.
        bool anyPresenceRule = _deviceRules.Values.Any(rules => rules.Presence?.Sources.Count > 0);
        _presenceInUse = anyPresenceRule ? await GetPresenceInUseAsync() : new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Each presence zone's in-use state, plus <see cref="PresenceSources.House"/> = any of them. Motion
    /// sensors are deliberately not presence sources. A zone whose check fails counts as not in use.
    /// </summary>
    private async Task<Dictionary<string, bool>> GetPresenceInUseAsync()
    {
        Dictionary<string, bool> inUse = new(StringComparer.OrdinalIgnoreCase);
        foreach (string zone in _presenceService.PresenceZones)
        {
            try
            {
                inUse[zone] = await _presenceService.IsRoomInUse(zone);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not check presence in {Zone} - treating it as not in use", zone);
                inUse[zone] = false;
            }
        }

        // "House" is someone home, so only the zones that mean that: not, say, a desk plug merely switched on.
        inUse[PresenceSources.House] = _presenceService.HouseZones.Any(zone => inUse.TryGetValue(zone, out bool value) && value);
        return inUse;
    }

    private DeviceRulesDto? GetRulesFor(string entityId)
    {
        return _deviceRules != null && _deviceRules.TryGetValue(entityId, out DeviceRulesDto? rules) ? rules : null;
    }

    /// <summary>Whether a drivable device's rules let it run right now. Devices without rules always may.</summary>
    private bool IsDeviceAllowed(object device)
    {
        string? entityId = device switch
        {
            ICustomSwitchEntity plug => plug.EntityId,
            ICustomClimateControlEntity valve => valve.EntityId,
            _ => null
        };

        if (entityId == null)
        {
            return true;
        }

        return DeviceRulesEvaluator.Allows(
            GetRulesFor(entityId),
            _timeProvider.GetLocalNow().DateTime,
            source => _presenceInUse.TryGetValue(source.Trim(), out bool inUse) && inUse);
    }

    /// <summary>
    /// After a zone has been evaluated, makes each of its devices follow its own rules while the zone is
    /// heating: a device whose rules have stopped passing is turned off (the owner's choice — a heater must
    /// not keep running after everyone has gone), and one whose rules pass again is turned back on. If the
    /// rules hold every device off, the zone stops counting as heating. Returns whether anything changed.
    /// </summary>
    private async Task<bool> EnforceDeviceRulesAsync(RoomSchedule room)
    {
        if (!_roomStates.TryGetValue(room.Id, out RoomState? state) || !state.HeatingActive)
        {
            return false;
        }

        bool changed = false;
        bool anyAllowed = false;
        foreach (object device in GetSwitchesForRoom(room))
        {
            Func<bool, Task<bool>>? toggle = GetOnToggleFuncForSingleDevice(device);
            if (toggle == null)
            {
                continue;
            }

            bool allowed = IsDeviceAllowed(device);
            anyAllowed |= allowed;
            if (IsWaitingForBoiler(device))
            {
                // The boiler is off, so the valve's position makes no difference: leave it exactly as it is.
                continue;
            }

            if (await toggle(allowed))
            {
                changed = true;
                _logger.LogInformation("Room {Room}: {Device} turned {State} by its device rules", room.Name, DeviceName(device), allowed ? "on" : "off");
            }
        }

        if (!anyAllowed)
        {
            state.HeatingActive = false;
            changed = true;
        }

        return changed;
    }

    /// <summary>A radiator valve held shut because, in fingerbot mode, the boiler is believed off.</summary>
    private bool IsWaitingForBoiler(object device) => _valvesWaitForBoiler && device is ICustomClimateControlEntity;

    private static string DeviceName(object device) => device switch
    {
        ICustomSwitchEntity plug => plug.EntityId,
        ICustomClimateControlEntity valve => valve.EntityId,
        _ => "unknown device"
    };
}
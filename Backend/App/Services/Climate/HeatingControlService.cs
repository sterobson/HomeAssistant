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

internal partial class HeatingControlService
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
    // Corrective presses since the last ordinary one. A check that disagrees again after a correction is pressed again,
    // either way (owner, 2026-10-23: the app must be certain the boiler did what it was told, even if a corrective press
    // did not work), up to MaxCorrectionsInARow, after which the app stops and raises a notification: presses that keep
    // failing mean something it cannot fix, such as the switch at the boiler being on the boiler thermostat, where every
    // press is dead. Until 2026-10-23 a second disagreement was only logged.
    private int _correctionsInARow = 0;
    // How long after its check fails each corrective press waits: the first at once, the later ones further apart, in
    // case what stops them is a network problem that passes (owner, 2026-10-23). One entry per press allowed.
    internal static readonly TimeSpan[] CorrectionDelays =
        [TimeSpan.Zero, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(60)];
    internal static readonly int MaxCorrectionsInARow = CorrectionDelays.Length;
    // A corrective press waiting its turn (see CorrectionDelays); null if none.
    private DateTimeOffset? _correctionDueAt;
    // Set once MaxCorrectionsInARow presses have not put the boiler where the app believes it is; cleared when the belief
    // is settled some other way.
    private bool _correctionGivenUp = false;
    // The valve held open for the check of a press, so it can show what the boiler is doing (owner, 2026-10-23). See
    // IsHeldForBoilerCheck.
    private string? _valveHeldForBoilerCheck;
    // A held valve's setpoint is kept at least this far above its reading, so its TRV never throttles at its setpoint,
    // which would stop it climbing on a running boiler and so hide that from the check.
    internal const double HeldValveHeadroomCelsius = 2.0;
    private bool _hasLoggedFingerbotUnavailable = false;

    // A press Home Assistant reported as failed leaves the belief alone, and the boiler is decided again a little later
    // rather than waiting for the 5-minute recheck. Each failure in a row waits longer; past the last, the recheck
    // carries on trying (HOME-ASSISTANT.md gotcha 24).
    internal static readonly TimeSpan[] FailedPressRetryDelays = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2)];
    private int _failedPressesInARow = 0;
    private bool _failedPressRetryPending = false;

    // Whether the boiler was believed on when the pass began, before it was decided. A room reaching its target is
    // usually the last one calling, which turns the boiler off in that very pass, and a warm-up that ends that way
    // still had the boiler on right up to the reading that finished it (see WarmUpRates).
    private bool _boilerWasOnAtStartOfPass = false;

    // Fingerbot movements the app did not command (the HA UI, a script, an automation). Guarded by
    // _fingerbotLock because state changes arrive outside the evaluation semaphore.
    private readonly object _fingerbotLock = new();
    private bool? _lastSeenFingerbotState;
    private bool? _expectedFingerbotState;
    private int _pressesSeenOutsideTheApp = 0;

    // A press of the app's own that Home Assistant reported as failed, which may still land: the fingerbot moving to
    // State within LatePressWindow of FailedAt is taken as that press, not one from outside the app. On 2026-10-23 HA
    // answered an "off" press with "The operation has timed out" and the fingerbot reported the move 11 s later
    // (HOME-ASSISTANT.md gotcha 27). Cleared by the app's next press, which supersedes it.
    private (bool State, DateTimeOffset FailedAt, bool WasCorrection)? _unconfirmedPress;
    // How long after HA reported a press as failed a movement to the state it asked for still counts as that press.
    // Shorter than the first retry (FailedPressRetryDelays), so the two never overlap.
    internal static readonly TimeSpan LatePressWindow = TimeSpan.FromSeconds(50);
    // A press reported as failed that the fingerbot has since shown did land, for the next evaluation to take as the
    // app's own; null if none. Seconds is how long after the failure the fingerbot moved.
    private (double Seconds, bool WasCorrection)? _unconfirmedPressLanded;

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

    // A press made this pass, whose verification is armed only after the rooms have moved their valves, with the
    // valves open after the pass (RecordValvesAndVerifyAsync). Until 2026-10-21 it was armed in DecideBoilerAsync with the
    // valves open before the pass, so on 2026-10-20 it watched the dining valve that same pass shut, which could never
    // warm. Null when nothing was pressed.
    private (bool BoilerNowOn, DateTimeOffset? BoilerOffSince)? _pressToVerify;
    // A fingerbot movement the app did not make, which flipped the belief: checked against the radiators in the same
    // pass as a press of the app's own (since 2026-10-22; until then it cancelled every check).
    private (bool BoilerNowOn, DateTimeOffset? BoilerOffSince)? _fingerbotMovementToVerify;

    // The valves commanded this pass and whether each was sent open. HA reports a command only a moment after it is
    // sent, so reading a valve back in the same pass still shows where it was (see IsValveOpenAfterPass).
    private readonly Dictionary<string, bool> _valvesCommandedThisPass = new(StringComparer.OrdinalIgnoreCase);

    // Valve rooms calling for heat from the boiler. A room starts calling once it is HysteresisOffset below its
    // target (where its valve would open) and stops at its target; the boiler goes off when none is calling.
    // So a room a fraction short that never started calling cannot hold the boiler on with every valve shut.
    private readonly HashSet<int> _roomsCallingForHeat = [];

    // Whether each device (by entity ID) wants heat, for its hysteresis. Kept per device rather than read from the
    // device, because a device can want heat while its rules hold it off, and pre-heat differs between devices.
    // Not saved: after a restart each device starts from what it is doing.
    private readonly Dictionary<string, bool> _devicesWantingHeat = new(StringComparer.OrdinalIgnoreCase);

    // Refreshed at the start of every evaluation pass (PrepareDeviceRulesAsync): the per-device rules in
    // force, whether they are the built-in defaults, and which presence sources are in use right now.
    private Dictionary<string, DeviceRulesDto>? _deviceRules;
    private bool _deviceRulesAreDefault = true;
    private Dictionary<string, bool> _presenceInUse = new(StringComparer.OrdinalIgnoreCase);
    private PresenceSnapshot? _presence;

    // The house's replaceable batteries, sent with every room-state upload for the UI's low-battery icons (owner,
    // 2026-10-20). Optional so tests need not supply it. A battery changing is a reason to upload on its own.
    private readonly IBatteryOverview? _batteryOverview;
    private string? _uploadedBatteries;

    // How fast each radiator valve has warmed its room (owner, 2026-10-20), which an Auto pre-heat is worked out from.
    // Optional so tests need not supply it; without it every Auto valve uses the 30-minute fallback.
    private readonly IWarmUpRates? _warmUpRates;

    // The warmer track an Auto valve has begun pre-heating for, by entity ID, so that the pre-heat, once started, is
    // kept until the track begins. Without it the time needed would shrink as the room warmed, and a valve whose rate
    // was a little pessimistic would shut again part-way and start over.
    private readonly Dictionary<string, int> _autoPreHeatTrackIds = new(StringComparer.OrdinalIgnoreCase);

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
        IBoilerStatePersistence boilerStatePersistence,
        IBatteryOverview? batteryOverview = null,
        IWarmUpRates? warmUpRates = null)
    {
        _batteryOverview = batteryOverview;
        _warmUpRates = warmUpRates;
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
            namedEntities.HallwayClimateTemperature.EntityId,
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

        ScheduleValveExercise();

        // Schedule periodic evaluation of all schedules
        _scheduler.SchedulePeriodic(TimeSpan.FromMinutes(_recheckEveryXMinutes), async () => await EvaluateAllSchedules($"{_recheckEveryXMinutes} minute period recheck"));

        // Subscribe to temperature changes
        _namedEntities.GamesRoomDeskTemperature.SubscribeToStateChangesAsync(async change => await EvaluateAllSchedules("games room temperature changed"));
        _namedEntities.GamesRoomDeskPlugOnOff.SubscribeToStateChangesAsync(async change => await EvaluateAllSchedules("games room desk plug state changed"));
        _namedEntities.KitchenTemperature.SubscribeToStateChangesAsync(async change => await EvaluateAllSchedules("kitchen temperature changed"));
        _namedEntities.Bedroom1Temperature.SubscribeToStateChangesAsync(async change => await EvaluateAllSchedules("bedroom 1 temperature changed"));
        // These two used to be left to the 5-minute recheck; a warm-up is timed from the readings, so they count too.
        _namedEntities.Bedroom2Temperature.SubscribeToStateChangesAsync(async change => await EvaluateAllSchedules("bedroom 2 temperature changed"));
        _namedEntities.Bedroom3Temperature.SubscribeToStateChangesAsync(async change => await EvaluateAllSchedules("bedroom 3 temperature changed"));
        _namedEntities.DiningRoomDeskPlugOnOff.SubscribeToStateChangesAsync(async change => await EvaluateAllSchedules("dining room desk plug state changed"));
        _namedEntities.DiningRoomClimateTemperature.SubscribeToStateChangesAsync(async change => await EvaluateAllSchedules("dining room temperature changed"));
        _namedEntities.LivingRoomClimateTemperature.SubscribeToStateChangesAsync(async change => await EvaluateAllSchedules("living room temperature changed"));
        _namedEntities.DenClimateTemperature.SubscribeToStateChangesAsync(async change => await EvaluateAllSchedules("den temperature changed"));
        _namedEntities.HallwayClimateTemperature.SubscribeToStateChangesAsync(async change => await EvaluateAllSchedules("hallway temperature changed"));
        _namedEntities.UpstairsBathroomTemperature.SubscribeToStateChangesAsync(async change => await EvaluateAllSchedules("upstairs bathroom temperature changed"));

        // Room presence from TVs and lights (PresenceService). A TV is normally switched with its
        // remote, not its plug, so its power draw is what changes.
        // The den TV latches on at one threshold and off at another, so a re-evaluation follows either crossing.
        ReevaluateWhenPowerCrosses(_namedEntities.DenTvPlugPower, PresenceService.DenTvInUseWatts, "den TV");
        ReevaluateWhenPowerCrosses(_namedEntities.DenTvPlugPower, PresenceService.DenTvOffWatts, "den TV (30 W)");
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
        string? trigger = null;

        lock (_fingerbotLock)
        {
            bool? previous = _lastSeenFingerbotState;
            bool? current = ReadFingerbotState();
            _lastSeenFingerbotState = current;

            // Going unavailable or coming back is not a press.
            if (current.HasValue && previous.HasValue && current != previous)
            {
                DateTimeOffset now = _timeProvider.GetUtcNow();
                if (_expectedFingerbotState == current)
                {
                    _expectedFingerbotState = null;
                }
                else if (_unconfirmedPress is { } unconfirmed && unconfirmed.State == current && now - unconfirmed.FailedAt <= LatePressWindow)
                {
                    // Moving the way the app's press asked, seconds after HA called that press failed: it is that press.
                    _unconfirmedPress = null;
                    _unconfirmedPressLanded = ((now - unconfirmed.FailedAt).TotalSeconds, unconfirmed.WasCorrection);
                    trigger = "boiler fingerbot press reported as failed landed after all";
                }
                else
                {
                    _unconfirmedPress = null;
                    _pressesSeenOutsideTheApp++;
                    trigger = "boiler fingerbot pressed outside the app";
                }
            }
        }

        if (trigger != null)
        {
            await EvaluateAllSchedules(trigger);
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
                        Devices = GetDevicesForRoom(schedule),
                        Occupancy = DescribeOccupancy(schedule)
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
                    kvp.Value.Occupancy = DescribeOccupancy(schedule);
                    kvp.Value.CurrentTemperature = GetCurrentTemperatureForRoom(schedule);
                }
            }

            // Upload to API (SignalR notification will be broadcast automatically)
            List<BatteryDeviceDto>? batteries = ReadBatteries();
            await _statePersistence.SetStatesAsync(_roomStates, DescribeBoiler(), batteries);
            _uploadedBatteries = DescribeBatteries(batteries);
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
            if (_valveExerciseRunning)
            {
                // The monthly valve exercise owns the valves and the boiler for its 5 minutes; it evaluates when done.
                _logger.LogDebug("Evaluation skipped during the valve exercise. Triggered by {Trigger}", trigger);
                return;
            }

            await PrepareDeviceRulesAsync();
            RoomSchedules schedules = await LoadSchedulesAsync();

            // 1. The boiler's mode and belief: a mode change, the saved belief at startup, outside presses.
            BoilerStateDto boilerBefore = DescribeBoiler();
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
            _boilerWasOnAtStartOfPass = _boilerOn;
            // A press seen outside the app is verified like the app's own; a press the app makes this pass replaces it.
            _pressToVerify = fingerbotMode ? _fingerbotMovementToVerify : null;
            _fingerbotMovementToVerify = null;
            _valvesCommandedThisPass.Clear();
            if (fingerbotMode)
            {
                await DecideBoilerAsync(schedules, targets, trigger);
            }

            // A turn-off to check: keep the warmest open valve open through this pass, so the rooms cannot shut every
            // valve (they would, after a failed check, with the boiler possibly running) and leave nothing to watch.
            // A turn-on: the rooms bring their valves into line first, and the valve to hold is chosen after them.
            if (_pressToVerify is { } pressThisPass)
            {
                _valveHeldForBoilerCheck = pressThisPass.BoilerNowOn
                    ? null
                    : WarmestValve(ValvesOf(schedules).Where(v => IsValveOpen(v.Valve)))?.EntityId;
            }

            // 4. The rooms' devices.
            _valvesWaitForBoiler = fingerbotMode && !_boilerOn && !_boilerMayBeRunning;
            bool hasStateChanged = false;
            foreach (RoomSchedule schedule in schedules.Rooms)
            {
                if (targets.TryGetValue(schedule.Id, out RoomTarget? target))
                {
                    hasStateChanged |= await EvaluateSchedule(schedule, target, trigger);
                }
            }

            // Which tracks are due but held back by their room-occupancy condition, for the UI to explain on the track.
            foreach (RoomSchedule schedule in schedules.Rooms)
            {
                if (_roomStates.TryGetValue(schedule.Id, out RoomState? waitingState))
                {
                    List<int> waiting = await FindTracksWaitingForOccupancyAsync(schedules.HouseOccupancyState, schedule);
                    if (!waiting.SequenceEqual(waitingState.WaitingForOccupancyTrackIds))
                    {
                        waitingState.WaitingForOccupancyTrackIds = waiting;
                        hasStateChanged = true;
                    }
                }
            }

            // A room with nothing to switch (the upstairs bathroom) is never evaluated above, so its reading would only
            // reach the UI when something else happened to upload. A changed reading is worth an upload on its own.
            foreach (RoomSchedule schedule in schedules.Rooms)
            {
                if (_roomStates.TryGetValue(schedule.Id, out RoomState? shown) && shown.CurrentTemperature != GetCurrentTemperatureForRoom(schedule))
                {
                    hasStateChanged = true;
                }
            }

            // 5. What the valves did this pass, for the boiler verification, and the verification itself.
            if (fingerbotMode)
            {
                await RecordValvesAndVerifyAsync(schedules);
            }

            // The UI shows the boiler too, so a change of mode or belief is worth an upload on its own.
            BoilerStateDto boilerAfter = DescribeBoiler();
            hasStateChanged |= boilerAfter.AppControlsBoiler != boilerBefore.AppControlsBoiler
                || boilerAfter.BelievedOn != boilerBefore.BelievedOn
                || boilerAfter.Since != boilerBefore.Since;

            // So does a battery level, area or name changing (checked every pass; levels move slowly).
            hasStateChanged |= _batteryOverview != null && DescribeBatteries(ReadBatteries()) != _uploadedBatteries;

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

    /// <summary>
    /// What a room wants this pass, with the thermometer reading it is judged against. The base is what it wants now
    /// (a boost, or its active track). Each device may start early for a warmer track later today, by its own
    /// pre-heat (<see cref="DeviceRulesDto.PreHeatMinutes"/>), so devices in one room can aim at different
    /// temperatures at the same moment: a radiator 30 minutes ahead, a fan heater 10.
    /// </summary>
    private sealed record RoomTarget(
        double CurrentTemperature,
        DeviceTarget Base,
        IReadOnlyList<(TimeSpan StartsAt, HeatingScheduleTrack Track)> UpcomingWarmer,
        TimeSpan Now)
    {
        /// <summary>The target for a device that pre-heats this many minutes ahead.</summary>
        public DeviceTarget For(int preHeatMinutes)
        {
            foreach ((TimeSpan startsAt, HeatingScheduleTrack track) in UpcomingWarmer)
            {
                if (Now >= startsAt - TimeSpan.FromMinutes(preHeatMinutes))
                {
                    return new DeviceTarget(track.Temperature, track, "pre-heating for upcoming schedule");
                }
            }

            return Base;
        }
    }

    private sealed record DeviceTarget(double Temperature, HeatingScheduleTrack? Track, string Reason);

    private static string? EntityIdOf(object device) => device switch
    {
        ICustomSwitchEntity plug => plug.EntityId,
        ICustomClimateControlEntity valve => valve.EntityId,
        _ => null
    };

    /// <summary>This device's target: the room's, brought forward by the device's own pre-heat.</summary>
    private DeviceTarget TargetFor(RoomTarget target, object device)
    {
        string? entityId = EntityIdOf(device);
        DeviceRulesDto? rules = entityId == null ? null : GetRulesFor(entityId);
        // A towel rail is a radiator too, warming its room as slowly, so it takes a radiator's default pre-heat.
        bool isValve = device is ICustomClimateControlEntity or TowelRail;

        if (entityId != null && DeviceRulesDto.IsAutoPreHeat(rules, isValve) && _warmUpRates?.RateFor(entityId) != null)
        {
            return AutoPreHeatTarget(target, entityId);
        }

        // A fixed pre-heat, or an Auto valve not yet measured (which then uses the 30-minute fallback).
        return target.For(DeviceRulesDto.PreHeatFor(rules, isValve));
    }

    /// <summary>
    /// An Auto valve's target: it starts for a warmer track once the time its measured warm-up rate says the room
    /// needs, from the temperature it is at now, reaches back to now (never more than
    /// <see cref="DeviceRulesDto.MaxAutoPreHeatMinutes"/>). Once started it carries on until the track begins.
    /// </summary>
    private DeviceTarget AutoPreHeatTarget(RoomTarget target, string entityId)
    {
        if (_autoPreHeatTrackIds.TryGetValue(entityId, out int startedTrackId))
        {
            foreach ((TimeSpan startsAt, HeatingScheduleTrack track) in target.UpcomingWarmer)
            {
                if (track.Id == startedTrackId && target.Now < startsAt)
                {
                    return new DeviceTarget(track.Temperature, track, "pre-heating for upcoming schedule");
                }
            }

            // The track has begun, or no longer applies.
            _autoPreHeatTrackIds.Remove(entityId);
        }

        foreach ((TimeSpan startsAt, HeatingScheduleTrack track) in target.UpcomingWarmer)
        {
            int minutes = Math.Min(
                _warmUpRates?.MinutesToRise(entityId, track.Temperature - target.CurrentTemperature) ?? DeviceRulesDto.DefaultValvePreHeatMinutes,
                DeviceRulesDto.MaxAutoPreHeatMinutes);

            if (target.Now >= startsAt - TimeSpan.FromMinutes(minutes))
            {
                _autoPreHeatTrackIds[entityId] = track.Id;
                return new DeviceTarget(track.Temperature, track, "pre-heating for upcoming schedule");
            }
        }

        return target.Base;
    }

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
        if (GetSwitchesForRoom(roomHeatingSchedule).Length == 0)
        {
            return null;
        }

        // A boost — the owner's, a group boost, or a guest's QR override — sets the exact temperature, up or
        // down, until it ends (owner, 2026-10-19), with no pre-heat. Per-device rules still decide whether each
        // device may run.
        if (IsBoostRunning(roomHeatingSchedule))
        {
            return new RoomTarget(currentTemperature.Value,
                new DeviceTarget(roomHeatingSchedule.Boost?.Temperature ?? 0, null, "boost is active"),
                [], now.TimeOfDay);
        }

        // What it should be RIGHT NOW: the latest track that has started.
        HeatingScheduleTrack? currentActiveTrack = await FindCurrentActiveSchedule(houseOccupancyState, roomHeatingSchedule, now, currentDay);
        if (currentActiveTrack == null)
        {
            return null;
        }

        // And the warmer tracks still to come today, which a device may pre-heat for (never pre-cool).
        List<(TimeSpan StartsAt, HeatingScheduleTrack Track)> upcoming =
            await FindUpcomingWarmerTracks(houseOccupancyState, roomHeatingSchedule, now, currentDay, currentActiveTrack.Temperature);

        return new RoomTarget(currentTemperature.Value,
            new DeviceTarget(currentActiveTrack.Temperature, currentActiveTrack, "current active schedule"),
            upcoming, now.TimeOfDay);
    }

    /// <summary>
    /// Decides each of the room's devices on its own: whether it wants heat against its own target (its pre-heat
    /// may have started while another device's has not), with the hysteresis remembered per device, and whether
    /// its rules let it run. A device whose rules stop passing is switched off even if it still wants heat, and back
    /// on when they pass again. A valve waiting for the boiler is not moved at all (see _valvesWaitForBoiler). Each
    /// device gets at most one command per pass. Records the room's state for the UI; returns whether it changed.
    /// </summary>
    private async Task<bool> EvaluateSchedule(RoomSchedule roomHeatingSchedule, RoomTarget target, string trigger)
    {
        double currentTemperature = target.CurrentTemperature;
        DeviceTarget roomTarget = target.Base;
        bool anyHeating = false;

        foreach (object device in GetSwitchesForRoom(roomHeatingSchedule))
        {
            if (device is TowelRail)
            {
                // Nothing to switch: the boiler was already decided for it (DecideBoilerAsync). It sets the room's target
                // all the same, and is heating whenever the app has the boiler on.
                DeviceTarget railTarget = TargetFor(target, device);
                if (railTarget.Temperature > roomTarget.Temperature)
                {
                    roomTarget = railTarget;
                }

                anyHeating |= IsTowelRailHot();
                continue;
            }

            string? entityId = EntityIdOf(device);
            Func<bool, Task<bool>>? toggle = GetOnToggleFuncForSingleDevice(device);
            if (entityId == null || toggle == null)
            {
                continue;
            }

            DeviceTarget deviceTarget = TargetFor(target, device);
            if (deviceTarget.Temperature > roomTarget.Temperature)
            {
                roomTarget = deviceTarget;
            }

            // Hysteresis: once wanting heat, carry on until HysteresisOffset above the target; once not, wait until
            // HysteresisOffset below it. Remembered per device, starting from what the device is doing now.
            bool isOn = IsDeviceOn(device);
            bool wanting = _devicesWantingHeat.TryGetValue(entityId, out bool remembered) ? remembered : isOn;
            if (wanting && currentTemperature >= deviceTarget.Temperature + HysteresisOffset)
            {
                wanting = false;
            }
            else if (!wanting && currentTemperature <= deviceTarget.Temperature - HysteresisOffset)
            {
                wanting = true;
            }

            _devicesWantingHeat[entityId] = wanting;

            bool heldForCheck = IsHeldForBoilerCheck(device);
            if (heldForCheck || IsWaitingForBoiler(device))
            {
                // A valve held open for the check of a press is not the room's to move until the check is over. With
                // the boiler believed on it is open with hot water behind it, so it is heating the room.
                if (heldForCheck)
                {
                    KeepOpenForBoilerCheck(roomHeatingSchedule.Name, (ICustomClimateControlEntity)device, trigger);
                    anyHeating |= _boilerOn;
                }

                // With the boiler off a valve's position makes no difference, so it is neither opened nor shut,
                // and it is not heating the room whatever its setpoint says. The exception is the pass in which the
                // boiler was just turned off: the valve is still open from before, and the reading that finished its
                // warm-up arrived in this pass.
                _warmUpRates?.Observe(
                    entityId,
                    (_boilerOn || _boilerWasOnAtStartOfPass) && wanting && IsDeviceAllowed(device),
                    currentTemperature, deviceTarget.Temperature, _timeProvider.GetUtcNow());
                continue;
            }

            bool allowed = IsDeviceAllowed(device);
            bool desiredOn = wanting && allowed;

            // A valve counts as warming its room while it is meant to be open and the boiler is believed on. The
            // intended state rather than the one read back: HA reports a command a moment after it is sent.
            // A heater plug needs no boiler: it is warming its room whenever it is meant to be on (owner, 2026-10-21).
            if (device is ICustomClimateControlEntity)
            {
                _warmUpRates?.Observe(entityId, (_boilerOn || _boilerWasOnAtStartOfPass) && desiredOn, currentTemperature, deviceTarget.Temperature, _timeProvider.GetUtcNow());
            }
            else if (device is ICustomSwitchEntity)
            {
                _warmUpRates?.Observe(entityId, desiredOn, currentTemperature, deviceTarget.Temperature, _timeProvider.GetUtcNow(), WarmUpRates.HeaterMinimumMinutes);
            }

            if (desiredOn != isOn && await toggle(desiredOn))
            {
                if (device is ICustomClimateControlEntity)
                {
                    _valvesCommandedThisPass[entityId] = desiredOn;
                }

                _logger.LogInformation(
                    "Room {Room}: {Device} turned {State} (current {Current}°C, target {Target}°C, {Reason}{Rules}). Triggered by {Trigger}",
                    roomHeatingSchedule.Name, entityId, desiredOn ? "on" : "off", currentTemperature, deviceTarget.Temperature, deviceTarget.Reason,
                    wanting && !allowed ? "; held off by its device rules" : "", trigger);
            }

            anyHeating |= desiredOn;
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

        // The room shows the highest target any of its devices is aiming at, so a device pre-heating shows.
        roomState.RoomId = roomHeatingSchedule.Id;
        roomState.CurrentTemperature = currentTemperature;
        roomState.TargetTemperature = roomTarget.Temperature;
        roomState.HeatingActive = anyHeating;
        roomState.ActiveScheduleTrackId = roomTarget.Track?.Id ?? 0;
        roomState.LastUpdated = DateTimeOffset.UtcNow;
        RoomCapabilities previousCapabilities = roomState.Capabilities;
        string? previousDetectors = DescribeDetectorChoice(roomState.Occupancy);
        string previousDeviceSetup = DescribeDeviceSetup(roomState.Devices);
        roomState.Capabilities = GetRoomCapabilities(roomHeatingSchedule);
        roomState.Devices = GetDevicesForRoom(roomHeatingSchedule);
        roomState.Occupancy = DescribeOccupancy(roomHeatingSchedule);
        _roomStates[roomHeatingSchedule.Id] = roomState;

        // Upload when the temperature, the target or the track changed. Until 2026-10-19 the track was
        // compared after it had been overwritten (so never differed) and the target not at all, so after a
        // restart the UI showed no targets until some thermometer happened to change.
        if (roomState.CurrentTemperature != previousRoomTemperature
            || roomState.TargetTemperature != previousTargetTemperature
            || roomState.ActiveScheduleTrackId != previousTrackId
            || previousHeatingActive == null
            || roomState.HeatingActive != previousHeatingActive
            // Choosing a zone's presence detectors gains or loses its occupancy tracks, which the UI reads from here.
            // Until 2026-10-20 this was not compared, so a new choice reached the UI only when something else changed.
            || roomState.Capabilities != previousCapabilities
            || DescribeDetectorChoice(roomState.Occupancy) != previousDetectors
            // Likewise the Devices list: which devices a zone has and the rules on them, not their live readings.
            || DescribeDeviceSetup(roomState.Devices) != previousDeviceSetup)
        {
            stateChanged = true;
        }

        if (stateChanged)
        {
            roomState.LastUpdated = DateTimeOffset.UtcNow;
        }

        return stateChanged;
    }

    /// <summary>
    /// What the Devices list is made of, for spotting a change: each device, its role and the rules on it. Leaves
    /// out the readings (state, temperatures), which change constantly and have their own reasons to upload.
    /// </summary>
    private static string DescribeDeviceSetup(List<RoomDeviceDto> devices) =>
        string.Join('|', devices.Select(device =>
            $"{device.EntityId}/{device.Role}/{device.RulesAreDefault}/{(device.Rules == null ? "" : JsonSerializer.Serialize(device.Rules))}"
            + $"/{device.PreHeatAuto}/{device.HeatingRateCelsiusPerHour}/{device.HeatingRateSamples}"));

    /// <summary>The zone's chosen detectors and whether they are the defaults, for spotting a change. Null when none is described.</summary>
    private static string? DescribeDetectorChoice(RoomOccupancyDto? occupancy) =>
        occupancy == null ? null : $"{occupancy.IsDefault}:{string.Join(',', occupancy.DetectorIds)}";

    /// <summary>Whether a device is heating: a plug switched on, or a valve whose setpoint is at or above its own reading.</summary>
    private static bool IsDeviceOn(object device) => device switch
    {
        ICustomSwitchEntity plug => plug.IsOn(),
        ICustomClimateControlEntity valve => IsValveOpen(valve),
        _ => false
    };

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

    /// <summary>
    /// The tracks that would be running now but for their room-occupancy condition (owner, 2026-10-21): due today
    /// (started, on a day it runs, for the house's Home/Away mode) and no earlier than the track that is actually
    /// active, so a track that a later one has superseded is not "waiting". With nothing due today, yesterday's tracks
    /// that would have carried over, as <see cref="FindCurrentActiveSchedule"/> does. Empty while a boost runs, since
    /// no track applies then, and for a room with nothing to switch.
    /// </summary>
    private async Task<List<int>> FindTracksWaitingForOccupancyAsync(HouseOccupancyState houseOccupancyState, RoomSchedule schedule)
    {
        if (IsBoostRunning(schedule) || GetSwitchesForRoom(schedule).Length == 0)
        {
            return [];
        }

        DateTime now = _timeProvider.GetLocalNow().DateTime;
        Days today = ToDays(now.DayOfWeek);
        TimeSpan currentTime = now.TimeOfDay;
        HeatingScheduleTrack? active = await FindCurrentActiveSchedule(houseOccupancyState, schedule, now, today);
        bool activeIsToday = active != null && RunsOn(active, today) && active.TargetTime.ToTimeSpan() <= currentTime;

        List<int> waiting = [];
        bool anyDueToday = false;
        foreach (HeatingScheduleTrack track in schedule.ScheduleTracks)
        {
            if (!RunsOn(track, today) || track.TargetTime.ToTimeSpan() > currentTime
                || !await MeetsSpecialConditions(houseOccupancyState, schedule, track, ignoreRoomPresence: true))
            {
                continue;
            }

            anyDueToday = true;
            if ((!activeIsToday || track.TargetTime >= active!.TargetTime) && !await MeetsSpecialConditions(houseOccupancyState, schedule, track))
            {
                waiting.Add(track.Id);
            }
        }

        if (!anyDueToday)
        {
            Days yesterday = GetPreviousDay(today);
            foreach (HeatingScheduleTrack track in schedule.ScheduleTracks)
            {
                if (RunsOn(track, yesterday)
                    && (active == null || track.TargetTime >= active.TargetTime)
                    && await MeetsSpecialConditions(houseOccupancyState, schedule, track, ignoreRoomPresence: true)
                    && !await MeetsSpecialConditions(houseOccupancyState, schedule, track))
                {
                    waiting.Add(track.Id);
                }
            }
        }

        return [.. waiting.Order()];
    }

    private static bool RunsOn(HeatingScheduleTrack track, Days day) => track.Days == Days.Unspecified || (track.Days & day) != 0;

    /// <summary>
    /// Today's tracks still to come that are warmer than now and whose conditions hold, earliest first: what a
    /// device can pre-heat for. Only the earliest one within a device's pre-heat counts (see RoomTarget.For).
    /// Pre-heat is per device since 2026-10-19; a track's own RampUpMinutes is no longer read.
    /// </summary>
    private async Task<List<(TimeSpan StartsAt, HeatingScheduleTrack Track)>> FindUpcomingWarmerTracks(
        HouseOccupancyState houseOccupancyState, RoomSchedule schedule, DateTime now, Days currentDay, double currentActiveTemperature)
    {
        TimeSpan currentTime = now.TimeOfDay;
        List<(TimeSpan StartsAt, HeatingScheduleTrack Track)> upcoming = [];

        foreach (HeatingScheduleTrack track in schedule.ScheduleTracks)
        {
            if (track.Days != Days.Unspecified && (track.Days & currentDay) == 0)
                continue;

            TimeSpan startsAt = track.TargetTime.ToTimeSpan();
            if (startsAt <= currentTime || track.Temperature <= currentActiveTemperature)
                continue;

            // Only look up presence etc. for tracks within the longest pre-heat any device can have.
            if (currentTime < startsAt - TimeSpan.FromMinutes(DeviceRulesDto.LongestPreHeatMinutes))
                continue;

            if (!await MeetsSpecialConditions(houseOccupancyState, schedule, track))
                continue;

            upcoming.Add((startsAt, track));
        }

        return [.. upcoming.OrderBy(u => u.StartsAt)];
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
            // Thermometer only (owner, 2026-10-20): nothing to drive, so no boost, schedules or share link in the UI.
            "hallway" => [],
            "bedroom 1" => [_namedEntities.Bedroom1HeaterSmartPlugOnOff, _namedEntities.Bedroom1RadiatorThermostat],
            "bedroom 2" => [_namedEntities.Bedroom2RadiatorThermostat],
            "bedroom 3" => [_namedEntities.Bedroom3RadiatorThermostat],
            // A towel rail on the boiler's circuit with no valve (owner, 2026-10-20): it cannot be switched, but the
            // room can be heated by running the boiler, so it has a target and calls for the boiler like a valve
            // room. Until then it had nothing here; before 2026-10-19 it had a dummy switch, which offered
            // controls that did nothing. The towel rail is real hardware and its demand really runs the boiler.
            "upstairs bathroom" => [TowelRail.UpstairsBathroom],
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

            // A thermometer and nothing to drive (owner, 2026-10-20): the zone just reports the hallway's temperature.
            "hallway" => _namedEntities.HallwayClimateTemperature,

            "living room" => _namedEntities.LivingRoomClimateTemperature,
            "downstairs bathroom" => null,
            "bedroom 1" => _namedEntities.Bedroom1Temperature,
            "bedroom 2" => _namedEntities.Bedroom2Temperature,
            "bedroom 3" => _namedEntities.Bedroom3Temperature,
            "upstairs bathroom" => _namedEntities.UpstairsBathroomTemperature,
            _ => null
        };
    }

    private async Task<bool> MeetsSpecialConditions(HouseOccupancyState houseOccupancyState, RoomSchedule roomSchedule, HeatingScheduleTrack heatingScheduleTrack, bool ignoreRoomPresence = false)
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

        // From this pass's presence snapshot (PrepareDeviceRulesAsync), so every track and device sees the same answer
        // and each detector is checked only once.
        bool roomInUse = _presenceInUse.TryGetValue(roomSchedule.Name.Trim(), out bool inUse) && inUse;

        // Whether the track would run if its room-occupancy condition were not there (FindTracksWaitingForOccupancyAsync).
        if (ignoreRoomPresence)
        {
            return true;
        }

        if (heatingScheduleTrack.Conditions.HasFlag(ConditionType.RoomInUse))
        {
            meetsAllConditions &= roomInUse;
        }

        if (heatingScheduleTrack.Conditions.HasFlag(ConditionType.RoomNotInUse))
        {
            meetsAllConditions &= !roomInUse;
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
                    AttachWarmUp(valveDevice, room, isRadiatorValve: true);
                    devices.Add(valveDevice);
                    break;
                case ICustomSwitchEntity plug:
                    RoomDeviceDto plugDevice = DescribeDevice(plug.EntityId, RoomDeviceRoles.HeaterPlug);
                    AttachRules(plugDevice);
                    AttachWarmUp(plugDevice, room, isRadiatorValve: false);
                    devices.Add(plugDevice);
                    break;
                case TowelRail rail:
                    // Not in Home Assistant, so nothing to look up. Its state is the boiler's, when the app runs it.
                    devices.Add(new RoomDeviceDto
                    {
                        EntityId = rail.Id,
                        Role = RoomDeviceRoles.TowelRail,
                        Name = rail.Name,
                        State = _lastSeenBoilerControlPath == BoilerControlPath.Fingerbot ? (IsTowelRailHot() ? "on" : "off") : null
                    });
                    break;
            }
        }

        foreach ((string entityId, string role) in _presenceService.GetPresenceDevices(room.Name))
        {
            devices.Add(_presenceService.DescribeOwnDevice(entityId) ?? DescribeDevice(entityId, role));
        }

        return devices;
    }

    /// <summary>Publishes the rules currently applied to a drivable device, so the UI can show and edit them.</summary>
    private void AttachRules(RoomDeviceDto device)
    {
        device.Rules = GetRulesFor(device.EntityId);
        device.RulesAreDefault = _deviceRulesAreDefault;
    }

    /// <summary>
    /// Publishes how fast a radiator valve or heater plug has warmed its room and how long it would likely take to
    /// reach the next warmer schedule track, so the UI can say what an Auto pre-heat amounts to (owner, 2026-10-20;
    /// heater plugs since 2026-10-21).
    /// </summary>
    private void AttachWarmUp(RoomDeviceDto valveDevice, RoomSchedule room, bool isRadiatorValve)
    {
        valveDevice.PreHeatAuto = DeviceRulesDto.IsAutoPreHeat(GetRulesFor(valveDevice.EntityId), isRadiatorValve);

        double? rate = _warmUpRates?.RateFor(valveDevice.EntityId);
        valveDevice.HeatingRateSamples = _warmUpRates?.SampleCount(valveDevice.EntityId) ?? 0;
        valveDevice.HeatingRateCelsiusPerHour = rate == null ? null : Math.Round(rate.Value, 2);

        double? roomTemperature = GetCurrentTemperatureForRoom(room);
        if (rate == null || roomTemperature == null)
        {
            return;
        }

        double? nextTarget = NextWarmerTrackTemperature(room, _timeProvider.GetLocalNow().DateTime, roomTemperature.Value);
        if (nextTarget != null)
        {
            valveDevice.WarmUpMinutes = _warmUpRates?.MinutesToRise(valveDevice.EntityId, nextTarget.Value - roomTemperature.Value);
        }
    }

    /// <summary>
    /// The temperature of the next schedule track, today after now or else tomorrow, that is warmer than the room is
    /// now: what a warm-up estimate is measured against. Conditions (presence and the like) are ignored, since this is
    /// only a guide to how long the radiator needs, not a prediction of which track will run.
    /// </summary>
    internal static double? NextWarmerTrackTemperature(RoomSchedule room, DateTime now, double roomTemperature)
    {
        for (int dayOffset = 0; dayOffset <= 1; dayOffset++)
        {
            Days day = ToDays(now.AddDays(dayOffset).DayOfWeek);
            foreach (HeatingScheduleTrack track in room.ScheduleTracks.OrderBy(t => t.TargetTime))
            {
                if (track.Days != Days.Unspecified && (track.Days & day) == 0)
                    continue;

                if (dayOffset == 0 && track.TargetTime.ToTimeSpan() <= now.TimeOfDay)
                    continue;

                if (track.Temperature > roomTemperature + HysteresisOffset)
                    return track.Temperature;
            }
        }

        return null;
    }

    private static Days ToDays(DayOfWeek dayOfWeek) => dayOfWeek switch
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
        (double Seconds, bool WasCorrection)? unconfirmedPressLanded = TakeUnconfirmedPressLanded();

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
            CancelCorrection();
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

            _correctionsInARow = 0;
        }

        // Each fingerbot movement is taken as one press of a toggle button. It is only the switch's word for it: at 02:42
        // on 2026-10-22 it reported "on" by itself and the boiler never fired. So a changed belief is checked against
        // the radiators like a press the app has confirmed, and a report that moved nothing is caught.
        DateTimeOffset? offSinceBeforeMovement = _boilerOn ? null : _boilerBeliefSince;
        bool beliefChangedByMovement = false;

        if (unconfirmedPressLanded is { } landed)
        {
            // The app's own press, which HA reported as failed, so the belief was left alone; it moves now. Until
            // 2026-10-23 this was taken as a press from outside the app.
            SetBoilerBelief(!_boilerOn, _timeProvider.GetUtcNow());
            beliefChangedByMovement = true;
            _failedPressesInARow = 0;
            _boilerMayBeRunning = false;
            _logger.LogWarning(
                "The boiler fingerbot press Home Assistant reported as failed landed after all: the fingerbot moved {Seconds:F0} s later. Taking it as the app's press: now believing the boiler is {State}, and checking that against the radiators. Triggered by {Trigger}",
                landed.Seconds, _boilerOn ? "ON" : "OFF", trigger);
        }

        if (pressesSeenOutsideTheApp > 0)
        {
            bool flips = pressesSeenOutsideTheApp % 2 == 1;
            if (flips)
            {
                SetBoilerBelief(!_boilerOn, _timeProvider.GetUtcNow());
                beliefChangedByMovement = !beliefChangedByMovement;
            }

            _logger.LogWarning(
                "The boiler fingerbot was pressed {Count} time(s) outside the app. Now believing the boiler is {State}{Check}. Triggered by {Trigger}",
                pressesSeenOutsideTheApp, _boilerOn ? "ON" : "OFF", beliefChangedByMovement ? ", and checking that against the radiators" : "", trigger);
        }

        if (unconfirmedPressLanded != null || pressesSeenOutsideTheApp > 0)
        {
            // Any pending verification was judging the old belief and would "correct" the new one. The valve history
            // is kept: the new belief's check needs to know which radiators have had hot water (until 2026-10-23 it was
            // wiped too, so a movement to "off" could never arm a turn-off check).
            _boilerVerification.DiscardPendingVerification();
            // The movement settles the belief afresh, so a corrective press waiting its turn would act on the old one.
            CancelCorrection();
            _fingerbotMovementToVerify = beliefChangedByMovement ? (_boilerOn, offSinceBeforeMovement) : null;
            // A corrective press that landed late is still a correction, and counts towards the limit.
            _correctionsInARow = pressesSeenOutsideTheApp == 0 && unconfirmedPressLanded!.Value.WasCorrection ? _correctionsInARow + 1 : 0;
        }

        return controlPath;
    }

    /// <summary>
    /// Fingerbot mode only. Corrects the belief if a verification has failed, then turns the boiler on or off
    /// from what the rooms want this pass, before any valve has moved, so the rooms can then move their valves
    /// only if there may be hot water behind them.
    /// </summary>
    private async Task DecideBoilerAsync(RoomSchedules schedules, Dictionary<int, RoomTarget> targets, string trigger)
    {
        // Sum how far each room with a radiator valve is below its target. A room at or above its
        // target contributes nothing: it is disregarded, not a credit against the cold rooms.
        List<(string RoomName, double Deficit)> roomsBelowTarget = [];
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
            object[] devices = GetSwitchesForRoom(roomSchedule);
            List<ICustomClimateControlEntity> radiatorValves = [.. devices.OfType<ICustomClimateControlEntity>().Where(IsDeviceAllowed)];
            // A towel rail has no valve and is always open, so it always takes heat (owner, 2026-10-20).
            List<object> boilerRadiators = [.. radiatorValves, .. devices.OfType<TowelRail>()];

            // A room with no such radiator, or no reading or target, has no known demand.
            if (boilerRadiators.Count == 0 || !targets.TryGetValue(roomSchedule.Id, out RoomTarget? target))
            {
                _roomsCallingForHeat.Remove(roomSchedule.Id);
                continue;
            }

            // The room's demand on the boiler is its radiators' target, brought forward by their own pre-heat:
            // a radiator pre-heating for a warmer track calls for the boiler from the start of its pre-heat.
            DeviceTarget valveTarget = boilerRadiators.Select(radiator => TargetFor(target, radiator)).MaxBy(t => t.Temperature)!;
            double current = target.CurrentTemperature;
            if (valveTarget.Temperature > current)
            {
                double deficit = valveTarget.Temperature - current;
                roomsBelowTarget.Add((roomSchedule.Name, deficit));
                totalDeficit += deficit;
            }

            if (current <= valveTarget.Temperature - HysteresisOffset)
            {
                _roomsCallingForHeat.Add(roomSchedule.Id);
            }
            else if (current >= valveTarget.Temperature)
            {
                _roomsCallingForHeat.Remove(roomSchedule.Id);
            }

            if (_roomsCallingForHeat.Contains(roomSchedule.Id))
            {
                if (IsBoostRunning(roomSchedule))
                {
                    boostedRoomsCalling.Add(roomSchedule.Name);
                }
                else if (valveTarget.Track is { } track && track.Conditions.HasFlag(ConditionType.RoomInUse))
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

            // A corrective press that has not worked is pressed again, each press checked like the first, and the later
            // ones further apart in case the trouble is the network and it passes; after the last, the app stops and
            // raises a notification (owner, 2026-10-23). The same both ways: a boiler left running wastes gas unseen,
            // and one that never fired leaves the house cold.
            if (_correctionDueAt != null)
            {
                // Already waiting to press again; the press due will be checked in its turn.
            }
            else if (_correctionsInARow >= MaxCorrectionsInARow)
            {
                _correctionGivenUp = true;
                string actually = verifiedState.Value ? "still running" : "not running";
                _logger.LogError(
                    "Boiler verification says the boiler is {Actually} after {Count} corrective presses to turn it {Direction}. Not pressing again: something the fingerbot cannot fix is wrong (is the switch at the boiler on the boiler thermostat?). Check the boiler by hand",
                    actually, _correctionsInARow, _boilerOn ? "on" : "off");
                _ha.CallService("notify", "persistent_notification", data: new
                {
                    title = verifiedState.Value ? "Boiler may still be running" : "Boiler may not be running",
                    message = $"The boiler should be {(_boilerOn ? "on" : "off")}, and the heating app has pressed it {_correctionsInARow} more times to correct it, but the radiators still say it is {actually}. It has stopped pressing. Check the boiler, and the switch that chooses between the fingerbot and the boiler thermostat."
                });
            }
            else if (CorrectionDelays[_correctionsInARow] > TimeSpan.Zero)
            {
                TimeSpan wait = CorrectionDelays[_correctionsInARow];
                _correctionDueAt = _timeProvider.GetUtcNow() + wait;
                _scheduler.Schedule(wait, async () => await EvaluateAllSchedules("corrective boiler press due"));
                _logger.LogWarning(
                    "Boiler verification says the boiler is {Actually} after {Count} corrective press(es) to turn it {Direction}. Pressing again in {Minutes} min (corrective press {Next} of {Max})",
                    verifiedState.Value ? "still running" : "not running", _correctionsInARow, _boilerOn ? "on" : "off",
                    wait.TotalMinutes, _correctionsInARow + 1, MaxCorrectionsInARow);
            }
            else
            {
                _logger.LogWarning(
                    "Boiler verification detected state mismatch: we thought boiler was {Expected}, but it appears to be {Actual}. Correcting by toggling fingerbot",
                    _boilerOn ? "ON" : "OFF", verifiedState.Value ? "ON" : "OFF");
                await PressToCorrectAsync(verifiedState.Value);
            }
        }

        // A corrective press that was waiting its turn: it puts the boiler where the app believes it is.
        if (_correctionDueAt is { } due && _timeProvider.GetUtcNow() >= due)
        {
            _correctionDueAt = null;
            _logger.LogWarning(
                "Pressing the boiler {Direction} again (corrective press {Number} of {Max}): the last check said it was {Actually}",
                _boilerOn ? "on" : "off", _correctionsInARow + 1, MaxCorrectionsInARow, _boilerOn ? "not running" : "still running");
            await PressToCorrectAsync(verifiedState: !_boilerOn);
        }

        bool deficitReached = totalDeficit >= BoilerDeficitThresholdCelsius;
        bool roomsWantItOn = _roomsCallingForHeat.Count > 0 && (deficitReached || boostedRoomsCalling.Count > 0 || occupiedRoomsCalling.Count > 0);

        if (_boilerOn && _boilerOnForExercise)
        {
            // On only for the valve exercise: off again, unless the rooms would have turned it on themselves.
            _boilerOnForExercise = false;
            if (roomsWantItOn)
            {
                _logger.LogInformation("Boiler left ON after the valve exercise: the rooms need it. Triggered by {Trigger}", trigger);
            }
            else if (await PressBoilerFingerbotAsync())
            {
                SetBoilerBelief(false, _timeProvider.GetUtcNow());
                _correctionsInARow = 0;
                _logger.LogInformation("Boiler turned OFF after the valve exercise. Triggered by {Trigger}", trigger);
            }
        }
        else if (!_boilerOn)
        {
            // Boiler is OFF - should we turn it ON?
            if (roomsWantItOn)
            {
                string why = deficitReached ? "deficit reached the threshold"
                    : boostedRoomsCalling.Count > 0 ? $"boost running in {string.Join(", ", boostedRoomsCalling)}"
                    : $"in use: {string.Join(", ", occupiedRoomsCalling)}";

                DateTimeOffset? offSince = _boilerBeliefSince;

                if (BeliefStillContradicted)
                {
                    // The last check said the boiler is still running, and nothing has corrected it yet, so pressing "on"
                    // would turn it off. It is taken as already on, and checked against the radiators like a press.
                    CancelCorrection();
                    SetBoilerBelief(true, _timeProvider.GetUtcNow());
                    _correctionsInARow = 0;
                    _boilerMayBeRunning = false;
                    _pressToVerify = (true, offSince);
                    _logger.LogWarning(
                        "Boiler wanted ON ({Why}), and the last check said it is still running after the app turned it off, so it is not pressed: taking it as already on, and checking that against the radiators. Triggered by {Trigger}",
                        why, trigger);
                }
                else if (await PressBoilerFingerbotAsync())
                {
                    SetBoilerBelief(true, _timeProvider.GetUtcNow());
                    _correctionsInARow = 0;
                    _boilerMayBeRunning = false;
                    _logger.LogInformation(
                        "Boiler turned ON ({Why}; total deficit {Deficit:0.0}°C, threshold {Threshold}°C). Triggered by {Trigger}. Rooms: {Rooms}",
                        why,
                        totalDeficit, BoilerDeficitThresholdCelsius, trigger,
                        string.Join(", ", roomsBelowTarget.Select(r => $"{r.RoomName} ({r.Deficit:0.0}°C)")));

                    // Verified against the valves open once the rooms have moved them: those that open with this press
                    // and those left open, which with the boiler off had no hot water behind them.
                    _pressToVerify = (true, offSince);
                }
            }
        }
        else if (_roomsCallingForHeat.Count == 0)
        {
            // Boiler is ON and no room is calling for heat: turn it OFF. The valves are left where they are
            // (see _valvesWaitForBoiler), and the open ones are the radiators the verification watches for heating
            // up again, which they would if the boiler were still running.
            if (BeliefStillContradicted)
            {
                // The last check said the boiler never fired, and nothing has corrected it yet, so pressing "off" would
                // turn it on. It is taken as already off, and checked against the radiators like a press.
                CancelCorrection();
                SetBoilerBelief(false, _timeProvider.GetUtcNow());
                _correctionsInARow = 0;
                _boilerMayBeRunning = false;
                _pressToVerify = (false, null);
                _logger.LogWarning(
                    "Boiler wanted OFF (no room calling), and the last check said it is not running after the app turned it on, so it is not pressed: taking it as already off, and checking that against the radiators. Triggered by {Trigger}",
                    trigger);
            }
            else if (await PressBoilerFingerbotAsync())
            {
                SetBoilerBelief(false, _timeProvider.GetUtcNow());
                _correctionsInARow = 0;
                _boilerMayBeRunning = false;
                _logger.LogInformation("Boiler turned OFF (no room with a radiator valve or towel rail is calling for heat). Triggered by {Trigger}", trigger);

                _pressToVerify = (false, null);
            }
        }
    }

    /// <summary>
    /// A corrective press: puts the boiler back where the app believes it is, after a check found it the other way
    /// (<paramref name="verifiedState"/>), and checks that press like any other.
    /// </summary>
    private async Task PressToCorrectAsync(bool verifiedState)
    {
        // The boiler has really been in the verified state since the press we believed in.
        DateTimeOffset? actuallySince = _boilerBeliefSince;

        if (await PressBoilerFingerbotAsync(correction: true))
        {
            // The press puts the boiler where we believed it was.
            SetBoilerBelief(_boilerOn, _timeProvider.GetUtcNow());
            _correctionsInARow++;
            _pressToVerify = (_boilerOn, actuallySince);
        }
        else
        {
            // Nothing could be pressed, so the verification is the best evidence there is.
            SetBoilerBelief(verifiedState, actuallySince ?? _timeProvider.GetUtcNow());
        }
    }

    /// <summary>
    /// Whether the last check said the boiler is not where the app believes it, and no corrective press has yet answered
    /// it: one is waiting its turn, or the app has given up. Pressing to change the belief would then do the opposite.
    /// </summary>
    private bool BeliefStillContradicted => _correctionDueAt != null || _correctionGivenUp;

    /// <summary>Drops a corrective press waiting its turn, and a give-up, when the belief has been settled some other way.</summary>
    private void CancelCorrection()
    {
        _correctionDueAt = null;
        _correctionGivenUp = false;
    }

    /// <summary>
    /// Arms the verification of this pass's press, if any, with the valves open after the pass; then tells it which
    /// radiators have hot water behind them (an open valve while the boiler is believed on) and which do not, and
    /// runs it. Called after the rooms have moved their valves. The press is armed first, so a valve that has just
    /// opened still shows how long its radiator was cold.
    /// </summary>
    private async Task RecordValvesAndVerifyAsync(RoomSchedules schedules)
    {
        if (_pressToVerify is { } press)
        {
            List<(string RoomName, ICustomClimateControlEntity Valve)> openAfterPass = [.. ValvesOf(schedules).Where(v => IsValveOpenAfterPass(v.Valve))];

            // A press can only be checked against an open valve: a shut one's radiator stays cold whether the boiler runs
            // or not. With none open, one is opened for the check (owner, 2026-10-23).
            // For a turn-off, the warmest: it had hot water most recently, so if the boiler is off it can only cool, and
            // nothing else warming the room can lift it to a new high, while if it is running it heats up again. For a
            // turn-on, the coldest, which shows the clearest rise when hot water reaches it.
            if (!openAfterPass.Exists(v => v.Valve.CurrentTemperature.HasValue))
            {
                List<(string RoomName, ICustomClimateControlEntity Valve)> candidates = [.. ValvesOf(schedules)
                    .Where(v => v.Valve.CurrentTemperature.HasValue && !_valvesCommandedThisPass.ContainsKey(v.Valve.EntityId))];
                // Preferably one its device rules allow, so a room held off is heated only as long as the check needs.
                List<(string RoomName, ICustomClimateControlEntity Valve)> allowed = [.. candidates.Where(v => IsDeviceAllowed(v.Valve))];
                ICustomClimateControlEntity? probe = ValveToWatch(allowed, press.BoilerNowOn) ?? ValveToWatch(candidates, press.BoilerNowOn);
                if (probe != null)
                {
                    (string RoomName, ICustomClimateControlEntity Valve) opened = candidates.First(v => v.Valve == probe);
                    OpenValveForBoilerCheck(opened.RoomName, probe, press.BoilerNowOn, "no valve was open");
                    openAfterPass.Add(opened);
                }
            }

            // The one held open until the check is over: for a turn-off the one kept open through this pass, else the
            // warmest or coldest open now.
            if (press.BoilerNowOn || !openAfterPass.Exists(v => string.Equals(v.Valve.EntityId, _valveHeldForBoilerCheck, StringComparison.OrdinalIgnoreCase)))
            {
                _valveHeldForBoilerCheck = ValveToWatch(openAfterPass, press.BoilerNowOn)?.EntityId;
            }

            _boilerVerification.NotifyBoilerToggled(press.BoilerNowOn, openAfterPass, press.BoilerOffSince);
            _pressToVerify = null;
        }

        foreach (RoomSchedule roomSchedule in schedules.Rooms)
        {
            List<ICustomClimateControlEntity> radiatorValves = [.. GetSwitchesForRoom(roomSchedule).OfType<ICustomClimateControlEntity>()];
            if (radiatorValves.Count == 0)
            {
                continue;
            }

            List<ICustomClimateControlEntity> heating = _boilerOn ? [.. radiatorValves.Where(IsValveOpenAfterPass)] : [];
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

    /// <summary>Every radiator valve, once each, with the room it belongs to.</summary>
    private IEnumerable<(string RoomName, ICustomClimateControlEntity Valve)> ValvesOf(RoomSchedules schedules) => schedules.Rooms
        .SelectMany(room => GetSwitchesForRoom(room).OfType<ICustomClimateControlEntity>().Select(valve => (room.Name, Valve: valve)))
        .DistinctBy(v => v.Valve.EntityId);

    /// <summary>The valve whose own sensor reads warmest (ties by entity ID, so the choice is stable), or null if none reads.</summary>
    private static ICustomClimateControlEntity? WarmestValve(IEnumerable<(string RoomName, ICustomClimateControlEntity Valve)> valves) => valves
        .Where(v => v.Valve.CurrentTemperature.HasValue)
        .OrderByDescending(v => v.Valve.CurrentTemperature)
        .ThenBy(v => v.Valve.EntityId, StringComparer.OrdinalIgnoreCase)
        .Select(v => v.Valve)
        .FirstOrDefault();

    /// <summary>The valve that best shows the check of a press: the warmest for a turn-off, the coldest for a turn-on.</summary>
    private static ICustomClimateControlEntity? ValveToWatch(IEnumerable<(string RoomName, ICustomClimateControlEntity Valve)> valves, bool boilerNowOn) => boilerNowOn
        ? valves
            .Where(v => v.Valve.CurrentTemperature.HasValue)
            .OrderBy(v => v.Valve.CurrentTemperature)
            .ThenBy(v => v.Valve.EntityId, StringComparer.OrdinalIgnoreCase)
            .Select(v => v.Valve)
            .FirstOrDefault()
        : WarmestValve(valves);

    /// <summary>
    /// Whether this is the valve held open for the check of a press (owner, 2026-10-23): while the check is being armed
    /// this pass or is still watching it, and the boiler is still believed the way that press put it. Its room does not
    /// move it, so it cannot be shut (after a failed turn-off check, when valves follow their rooms; or when its room
    /// warms before a turn-on has shown) and leave nothing to watch.
    /// </summary>
    private bool IsHeldForBoilerCheck(object device) =>
        device is ICustomClimateControlEntity valve
        && string.Equals(valve.EntityId, _valveHeldForBoilerCheck, StringComparison.OrdinalIgnoreCase)
        && (_pressToVerify is { } press ? press.BoilerNowOn == _boilerOn : _boilerVerification.IsWatching(valve.EntityId, _boilerOn));

    /// <summary>
    /// Keeps a held valve open with its setpoint at least <see cref="HeldValveHeadroomCelsius"/> above its reading. A TRV
    /// at its setpoint throttles and stops climbing, which on a running boiler is exactly what the check must not miss.
    /// </summary>
    private void KeepOpenForBoilerCheck(string roomName, ICustomClimateControlEntity valve, string trigger)
    {
        if (valve.CurrentTemperature is not double reading || _valvesCommandedThisPass.ContainsKey(valve.EntityId))
        {
            return;
        }

        if (valve.TargetTemperature is double setpoint && setpoint >= reading + HeldValveHeadroomCelsius)
        {
            return;
        }

        OpenValveForBoilerCheck(roomName, valve, _boilerOn, $"its setpoint was within {HeldValveHeadroomCelsius}°C of its reading. Triggered by {trigger}");
    }

    private void OpenValveForBoilerCheck(string roomName, ICustomClimateControlEntity valve, bool boilerNowOn, string why)
    {
        double reading = valve.CurrentTemperature.GetValueOrDefault();
        valve.SetTargetTemperature(reading + 5);
        _valvesCommandedThisPass[valve.EntityId] = true;
        _logger.LogInformation(
            "Room {Room}: {Valve} opened (setpoint {Setpoint}°C, reading {Reading}°C) and held open so the boiler turn-{Direction} can be checked against it: {Why}",
            roomName, valve.EntityId, reading + 5, reading, boilerNowOn ? "on" : "off", why);
    }

    /// <summary>A valve is open when its setpoint is at or above its own reading, which is how the app sets it.</summary>
    private static bool IsValveOpen(ICustomClimateControlEntity valve) => valve.TargetTemperature >= valve.CurrentTemperature;

    /// <summary>Whether a valve is open once this pass's commands have landed: what it was sent, else what it reads.</summary>
    private bool IsValveOpenAfterPass(ICustomClimateControlEntity valve) =>
        _valvesCommandedThisPass.TryGetValue(valve.EntityId, out bool sentOpen) ? sentOpen : IsValveOpen(valve);

    /// <summary>
    /// Whether a towel rail is known to be hot: the app runs the boiler and believes it on. On the boiler thermostat
    /// nobody can tell, so it is not claimed.
    /// </summary>
    private bool IsTowelRailHot() => _lastSeenBoilerControlPath == BoilerControlPath.Fingerbot && _boilerOn;

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

    private (double Seconds, bool WasCorrection)? TakeUnconfirmedPressLanded()
    {
        lock (_fingerbotLock)
        {
            (double Seconds, bool WasCorrection)? landed = _unconfirmedPressLanded;
            _unconfirmedPressLanded = null;
            return landed;
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
    /// Also false when Home Assistant answers the command with an error, such as "device did not respond"
    /// (2026-10-21, HOME-ASSISTANT.md gotcha 24): until then the command was fire-and-forget, so the belief
    /// went to ON with the fingerbot unmoved, and the next "off" then pressed a boiler that was off.
    /// A failed press that the fingerbot shows landed within <see cref="LatePressWindow"/> is still taken as this
    /// press, by the next evaluation; <paramref name="correction"/> says whether it was a corrective press.
    /// </summary>
    private async Task<bool> PressBoilerFingerbotAsync(bool correction = false)
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
            // This press supersedes an earlier one reported as failed: a movement now is this press.
            _unconfirmedPress = null;
        }

        // Never retried by the service call layer: a retry of a press that did land after all would be a second press.
        bool accepted = fingerbotState.Value
            ? await _namedEntities.BoilerFingerbot.TurnOffConfirmedAsync()
            : await _namedEntities.BoilerFingerbot.TurnOnConfirmedAsync();

        if (!accepted && ReadFingerbotState() == !fingerbotState.Value)
        {
            // The answer was lost but the fingerbot did move, and its state change was taken as this press.
            _logger.LogWarning("Home Assistant reported the boiler fingerbot press as failed, but the fingerbot moved, so it counts as pressed");
            accepted = true;
        }

        if (accepted)
        {
            _failedPressesInARow = 0;
            return true;
        }

        lock (_fingerbotLock)
        {
            // Not expected as such any more, but a movement the way this press asked within LatePressWindow is this press
            // landing late (2026-10-23: 11 s after "The operation has timed out"), and the next evaluation takes it as the
            // app's own. Any later movement is a press from outside the app. Either way the belief follows it.
            _expectedFingerbotState = null;
            _unconfirmedPress = (!fingerbotState.Value, _timeProvider.GetUtcNow(), correction);
        }

        _failedPressesInARow++;
        if (_failedPressesInARow <= FailedPressRetryDelays.Length && !_failedPressRetryPending)
        {
            TimeSpan retryIn = FailedPressRetryDelays[_failedPressesInARow - 1];
            _failedPressRetryPending = true;
            _scheduler.Schedule(retryIn, async () => await RetryAfterFailedPressAsync());
            _logger.LogWarning(
                "The boiler fingerbot press failed (Home Assistant reported an error), so the boiler was not pressed. Keeping the belief that it is {State}; deciding again in {Minutes} min",
                _boilerOn ? "ON" : "OFF", retryIn.TotalMinutes);
        }
        else
        {
            _logger.LogWarning(
                "The boiler fingerbot press failed again ({Count} in a row), so the boiler was not pressed. Keeping the belief that it is {State}; the {Recheck}-minute recheck will try again",
                _failedPressesInARow, _boilerOn ? "ON" : "OFF", _recheckEveryXMinutes);
        }

        return false;
    }

    /// <summary>What runs <see cref="FailedPressRetryDelays"/> after a failed press: the boiler is decided again.</summary>
    internal async Task RetryAfterFailedPressAsync()
    {
        _failedPressRetryPending = false;
        await EvaluateAllSchedules("retry after the boiler fingerbot press failed");
    }

    /// <summary>
    /// Loads the per-device rules and each zone's presence detectors, and works out presence, once per evaluation
    /// pass. Saved settings (device settings <c>heating.deviceRules</c> and <c>heating.zoneOccupancy</c>) win; if
    /// none have ever been saved, or the settings cannot be read, the built-in defaults apply.
    /// </summary>
    private async Task PrepareDeviceRulesAsync()
    {
        Dictionary<string, DeviceRulesDto>? saved = null;
        Dictionary<string, List<string>>? zoneOccupancy = null;
        try
        {
            DeviceSettingsDto? settings = await _deviceSettings.GetSettingsAsync();
            saved = settings?.Heating?.DeviceRules;
            zoneOccupancy = settings?.Heating?.ZoneOccupancy;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read device rules from device settings - using the built-in defaults");
        }

        _deviceRulesAreDefault = saved == null;
        _deviceRules = saved != null
            ? new Dictionary<string, DeviceRulesDto>(saved, StringComparer.OrdinalIgnoreCase)
            : DefaultDeviceRules.For(_namedEntities);

        _presenceService.UseZoneOccupancy(zoneOccupancy);
        _presenceInUse = await GetPresenceInUseAsync();
    }

    /// <summary>
    /// Each presence zone's in-use state, plus <see cref="PresenceSources.House"/> = someone is home (any detector
    /// that shows someone is there). Motion sensors are deliberately not presence detectors. If presence cannot be
    /// worked out at all, nothing counts as in use.
    /// </summary>
    private async Task<Dictionary<string, bool>> GetPresenceInUseAsync()
    {
        Dictionary<string, bool> inUse = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            _presence = await _presenceService.GetSnapshotAsync();
            foreach ((string zone, bool zoneInUse) in _presence.Zones)
            {
                inUse[zone] = zoneInUse;
            }

            inUse[PresenceSources.House] = _presence.SomeoneHome;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not check presence - treating every zone as not in use");
            _presence = null;
        }

        return inUse;
    }

    /// <summary>The house's replaceable batteries, or null when there is no overview or it cannot be read.</summary>
    private List<BatteryDeviceDto>? ReadBatteries()
    {
        if (_batteryOverview == null)
        {
            return null;
        }

        try
        {
            return _batteryOverview.Read();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the house's batteries; room states go up without them");
            return null;
        }
    }

    /// <summary>A comparable summary of the batteries: what the UI shows of each, nothing else.</summary>
    private static string DescribeBatteries(List<BatteryDeviceDto>? batteries) =>
        batteries == null
            ? string.Empty
            : string.Join("\n", batteries.Select(battery =>
                $"{battery.EntityId}|{battery.Percent}|{battery.Area}|{battery.Name}|{string.Join(",", battery.DeviceEntityIds)}"));

    /// <summary>What the app believes the boiler is doing, for the UI. Only meaningful in fingerbot mode.</summary>
    private BoilerStateDto DescribeBoiler()
    {
        bool appControls = _lastSeenBoilerControlPath == BoilerControlPath.Fingerbot;
        return new BoilerStateDto
        {
            AppControlsBoiler = appControls,
            BelievedOn = appControls && _boilerOn,
            Since = appControls ? _boilerBeliefSince?.ToString("O") : null
        };
    }

    /// <summary>The zone's presence detectors for the UI: those that count for it, and every one there is.</summary>
    private RoomOccupancyDto DescribeOccupancy(RoomSchedule room)
    {
        return new RoomOccupancyDto
        {
            DetectorIds = [.. _presenceService.DetectorsFor(room.Name)],
            IsDefault = _presenceService.ZoneOccupancyIsDefault,
            Available = [.. _presenceService.Detectors.Select(detector => new PresenceDetectorDto
            {
                Id = detector.Id,
                Label = detector.Label,
                MeansSomeoneHome = detector.MeansSomeoneHome,
                Active = _presence?.Detectors.TryGetValue(detector.Id, out bool active) == true && active
            })]
        };
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

    /// <summary>A radiator valve held shut because, in fingerbot mode, the boiler is believed off.</summary>
    private bool IsWaitingForBoiler(object device) => _valvesWaitForBoiler && device is ICustomClimateControlEntity;

    private static string DeviceName(object device) => device switch
    {
        ICustomSwitchEntity plug => plug.EntityId,
        ICustomClimateControlEntity valve => valve.EntityId,
        _ => "unknown device"
    };
}
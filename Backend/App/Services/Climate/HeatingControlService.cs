using HomeAssistant.apps;
using HomeAssistant.Devices.Batteries;
using HomeAssistant.Devices.Meters;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Threading;
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
    private readonly IScheduleApiClient? _scheduleApiClient;
    private readonly HomeAssistantConfiguration _configuration;
    private const int _recheckEveryXMinutes = 5;
    private const int _scheduleRefreshEveryXMinutes = 10;
    internal const double HysteresisOffset = 0.2;
    private readonly Dictionary<Guid, RoomState> _roomStates = [];
    private Timer? _scheduleRefreshTimer;

    // TODO: SignalR Connection - Add SignalR hub connection here
    // private HubConnection? _hubConnection;

    public List<RoomSchedule> Schedules { get; private set; } = [];

    public HeatingControlService(
        INamedEntities namedEntities,
        HistoryService historyService,
        IScheduler scheduler,
        ILogger<HeatingControlService> logger,
        IHomeBattery homeBattery,
        ISolarPanels solarPanels,
        IElectricityMeter electricityMeter,
        IPresenceService presenceService,
        TimeProvider timeProvider,
        HomeAssistantConfiguration configuration,
        IScheduleApiClient? scheduleApiClient = null)
    {
        _namedEntities = namedEntities;
        _scheduler = scheduler;
        _logger = logger;
        _homeBattery = homeBattery;
        _solarPanels = solarPanels;
        _electricityMeter = electricityMeter;
        _presenceService = presenceService;
        _timeProvider = timeProvider;
        _configuration = configuration;
        _scheduleApiClient = scheduleApiClient;

        // Initialize with default schedules (will be replaced if API client is available)
        Schedules = [
            new()
            {
                Condition = () => true,
                Room = Room.Kitchen,
                ScheduleTracks = [
                    new HeatingScheduleTrack { TargetTime = new TimeOnly(5,30), Temperature = 17 },
                    new HeatingScheduleTrack { TargetTime = new TimeOnly(6,30), Temperature = 18 },
                    new HeatingScheduleTrack { TargetTime = new TimeOnly(18,00), Temperature = 19 },
                    new HeatingScheduleTrack { TargetTime = new TimeOnly(18,30), Temperature = 17.5 },
                    new HeatingScheduleTrack { TargetTime = new TimeOnly(21,30), Temperature = 16 }
                ]
            },
            new()
            {
                Condition = () => true,
                Room = Room.GamesRoom,
                ScheduleTracks = [
                    new HeatingScheduleTrack{ TargetTime = new TimeOnly(0,00), Temperature = 19, Conditions = ConditionType.RoomInUse }, // Only if the desk has been on and in use
                    new HeatingScheduleTrack{ TargetTime = new TimeOnly(0,00), Temperature = 14, Conditions = ConditionType.RoomNotInUse },
                    new HeatingScheduleTrack{ TargetTime = new TimeOnly(7,00), Temperature = 18, Conditions = ConditionType.RoomNotInUse, Days = Days.Weekdays }, // Preheat on a weekday morning, anticipating use
                    new HeatingScheduleTrack{ TargetTime = new TimeOnly(9,00), Temperature = 16, Conditions = ConditionType.RoomNotInUse }, // Only if the desk has not been in use
                    new HeatingScheduleTrack{ TargetTime = new TimeOnly(21,30), Temperature = 14, Conditions = ConditionType.RoomNotInUse }
                ]
            },
            new()
            {
                Condition = () => true,
                Room = Room.Bedroom1,
                ScheduleTracks = [
                    new HeatingScheduleTrack{ TargetTime = new TimeOnly(8,00), Temperature = 18, Days = Days.Weekdays},
                    new HeatingScheduleTrack{ TargetTime = new TimeOnly(8,30), Temperature = 16, Days = Days.Weekdays},
                    new HeatingScheduleTrack{ TargetTime = new TimeOnly(7,30), Temperature = 19, Days = Days.Saturday},
                    new HeatingScheduleTrack{ TargetTime = new TimeOnly(8,00), Temperature = 16, Days = Days.Saturday},
                    new HeatingScheduleTrack{ TargetTime = new TimeOnly(9,00), Temperature = 19, Days = Days.Sunday},
                    new HeatingScheduleTrack{ TargetTime = new TimeOnly(9,30), Temperature = 16, Days = Days.Sunday},
                    new HeatingScheduleTrack{ TargetTime = new TimeOnly(21,30), Temperature = 18 },
                    new HeatingScheduleTrack{ TargetTime = new TimeOnly(21,31), Temperature = 14 },
                ]
            },
            new()
            {
                Condition = () => true,
                Room = Room.DiningRoom,
                ScheduleTracks = [
                    new HeatingScheduleTrack{ TargetTime = new TimeOnly(0,00), Temperature = 19, Conditions = ConditionType.RoomInUse }, // Only if the desk has been on and in use
                    new HeatingScheduleTrack{ TargetTime = new TimeOnly(0,00), Temperature = 14, Conditions = ConditionType.RoomNotInUse },
                    new HeatingScheduleTrack{ TargetTime = new TimeOnly(6,00), Temperature = 17, Conditions = ConditionType.RoomNotInUse },
                    new HeatingScheduleTrack{ TargetTime = new TimeOnly(21,00), Temperature = 14, Conditions = ConditionType.RoomNotInUse }
                ]
            },

        ];
    }

    public void Start()
    {
        // Download schedules from API if available
        if (_scheduleApiClient != null && _configuration.HouseId != Guid.Empty)
        {
            Task.Run(async () =>
            {
                await DownloadSchedulesFromApi();
                // Set up periodic schedule refresh every 10 minutes
                _scheduleRefreshTimer = new Timer(
                    async _ => await DownloadSchedulesFromApi(),
                    null,
                    TimeSpan.FromMinutes(_scheduleRefreshEveryXMinutes),
                    TimeSpan.FromMinutes(_scheduleRefreshEveryXMinutes));
            });
        }

        // TODO: SignalR Connection - Connect to SignalR hub for real-time updates
        // await ConnectToSignalRHub();

        // Schedule periodic evaluation of all schedules
        _scheduler.SchedulePeriodic(TimeSpan.FromMinutes(_recheckEveryXMinutes), async () => await EvaluateAllSchedules(Schedules));

        // Subscribe to temperature changes
        _namedEntities.GamesRoomDeskTemperature.SubscribeToStateChangesAsync(async change => await EvaluateSchedule(Schedules.FirstOrDefault(s => s.Room == Room.GamesRoom)));
        _namedEntities.GamesRoomDeskPlugOnOff.SubscribeToStateChangesAsync(async change => await EvaluateSchedule(Schedules.FirstOrDefault(s => s.Room == Room.GamesRoom)));
        _namedEntities.KitchenTemperature.SubscribeToStateChangesAsync(async change => await EvaluateSchedule(Schedules.FirstOrDefault(s => s.Room == Room.Kitchen)));
        _namedEntities.Bedroom1Temperature.SubscribeToStateChangesAsync(async change => await EvaluateSchedule(Schedules.FirstOrDefault(s => s.Room == Room.Bedroom1)));
        _namedEntities.DiningRoomDeskPlugOnOff.SubscribeToStateChangesAsync(async change => await EvaluateSchedule(Schedules.FirstOrDefault(s => s.Room == Room.DiningRoom)));
        _namedEntities.DiningRoomClimateTemperature.SubscribeToStateChangesAsync(async change => await EvaluateSchedule(Schedules.FirstOrDefault(s => s.Room == Room.DiningRoom)));

        // Subscribe to power changes
        _homeBattery.OnBatteryChargePercentChanged(async _ => await EvaluateAllSchedules(Schedules));
        _electricityMeter.OnCurrentRatePerKwhChanged(async _ => await EvaluateAllSchedules(Schedules));

        // Initial evaluation
        Task.Delay(1000).ContinueWith(async (value) => await EvaluateAllSchedules(Schedules));
    }

    private async Task DownloadSchedulesFromApi()
    {
        if (_scheduleApiClient == null || _configuration.HouseId == Guid.Empty)
            return;

        try
        {
            _logger.LogInformation("Downloading schedules from API for house {HouseId}", _configuration.HouseId);
            List<RoomSchedule> schedules = await _scheduleApiClient.GetSchedulesAsync(_configuration.HouseId);

            if (schedules.Count != 0)
            {
                // Set up delegates for each schedule
                foreach (RoomSchedule schedule in schedules)
                {
                    schedule.Condition = () => true;
                    schedule.GetCurrentTemperature = () => Task.FromResult(GetCurrentTemperatureForRoom(schedule));
                    schedule.OnToggleHeating = GetOnToggleFunc(schedule);
                }

                Schedules = schedules;
                _logger.LogInformation("Successfully loaded {Count} schedules from API", schedules.Count);

                // Initialize room states
                foreach (RoomSchedule schedule in Schedules)
                {
                    if (!_roomStates.ContainsKey(schedule.Id))
                    {
                        _roomStates[schedule.Id] = new RoomState
                        {
                            RoomId = schedule.Id,
                            CurrentTemperature = GetCurrentTemperatureForRoom(schedule),
                            HeatingActive = false,
                            ActiveScheduleTrackId = null,
                            LastUpdated = DateTimeOffset.UtcNow
                        };
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading schedules from API for house {HouseId}", _configuration.HouseId);
        }
    }

    private async Task UpdateRoomStateToApi(RoomState roomState)
    {
        if (_scheduleApiClient == null || _configuration.HouseId == Guid.Empty)
            return;

        try
        {
            List<RoomState> allStates = _roomStates.Values.ToList();
            await _scheduleApiClient.SetRoomStatesAsync(_configuration.HouseId, allStates);
            _logger.LogDebug("Updated room state for room {RoomId}", roomState.RoomId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating room state to API for room {RoomId}", roomState.RoomId);
        }
    }

    // TODO: SignalR Connection - Methods for SignalR connection
    // private async Task ConnectToSignalRHub()
    // {
    //     if (string.IsNullOrEmpty(_configuration.SignalRHubUrl))
    //         return;
    //
    //     _hubConnection = new HubConnectionBuilder()
    //         .WithUrl(_configuration.SignalRHubUrl)
    //         .WithAutomaticReconnect()
    //         .Build();
    //
    //     _hubConnection.On("SchedulesUpdated", async () =>
    //     {
    //         _logger.LogInformation("Received SchedulesUpdated notification from SignalR");
    //         await DownloadSchedulesFromApi();
    //     });
    //
    //     _hubConnection.On("RoomStatesUpdated", async () =>
    //     {
    //         _logger.LogInformation("Received RoomStatesUpdated notification from SignalR");
    //         // Frontend will handle this notification
    //     });
    //
    //     await _hubConnection.StartAsync();
    //     _logger.LogInformation("Connected to SignalR hub");
    // }

    public async Task EvaluateAllSchedules(List<RoomSchedule> schedules)
    {
        foreach (RoomSchedule schedule in schedules)
        {
            await EvaluateSchedule(schedule);
        }
    }

    private async Task EvaluateSchedule(RoomSchedule? roomHeatingSchedule)
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
            return;
        }

        // Get the toggle action, either from the supplied action, or the service's own function.
        Func<bool, Task<bool>>? onToggleHeating = roomHeatingSchedule.OnToggleHeating ?? GetOnToggleFunc(roomHeatingSchedule);
        if (onToggleHeating == null)
        {
            return;
        }

        // Step 1: Find the current active schedule (what temperature should it be RIGHT NOW?)
        HeatingScheduleTrack? currentActiveTrack = await FindCurrentActiveSchedule(roomHeatingSchedule, now, currentDay);
        if (currentActiveTrack == null)
        {
            return;
        }

        // Step 2: Check if there's an upcoming schedule we should pre-heat for
        HeatingScheduleTrack? preHeatTrack = await FindPreHeatSchedule(roomHeatingSchedule, now, currentDay, currentActiveTrack.Temperature);

        // Step 3: Determine which schedule to use
        HeatingScheduleTrack effectiveTrack = currentActiveTrack;
        string reason = "current active schedule";

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
        double desiredTemperature = effectiveTrack.Temperature;

        // Get or create room state
        if (!_roomStates.TryGetValue(roomHeatingSchedule.Id, out RoomState? roomState))
        {
            // Determine current heating state by checking if we would turn it on or off
            // We need to check the actual current state of the heating system
            bool currentHeatingState = false;

            // Try to determine current state by attempting a no-op toggle
            // If onToggleHeating(true) returns false, heating is already on
            // If onToggleHeating(false) returns false, heating is already off
            bool wouldTurnOff = await onToggleHeating(false);
            if (!wouldTurnOff)
            {
                // Heating was already off, restore to off
                await onToggleHeating(false);
                currentHeatingState = false;
            }
            else
            {
                // Heating was on, restore to on
                await onToggleHeating(true);
                currentHeatingState = true;
            }

            roomState = new RoomState
            {
                RoomId = roomHeatingSchedule.Id,
                CurrentTemperature = currentTemperature,
                HeatingActive = currentHeatingState,
                ActiveScheduleTrackId = effectiveTrack.Id,
                LastUpdated = DateTimeOffset.UtcNow
            };
            _roomStates[roomHeatingSchedule.Id] = roomState;
        }

        bool stateChanged = false;

        // Apply hysteresis to prevent flapping
        // If heating is ON: turn OFF only when temp >= target + offset
        // If heating is OFF: turn ON only when temp <= target - offset
        if (roomState.HeatingActive && currentTemperature >= desiredTemperature + HysteresisOffset)
        {
            // Heating is currently ON
            // Turn off
            if (await onToggleHeating(false))
            {
                _logger.LogInformation("Turning off heating in {Room} as current temperature {CurrentTemperature}°C >= target temperature {TargetTemperature}°C + {HysteresisOffset}°C ({Reason})",
                     roomHeatingSchedule.Room, currentTemperature, desiredTemperature, HysteresisOffset, reason);

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
                _logger.LogInformation("Turning on heating in {Room} as current temperature {CurrentTemperature}°C <= target temperature {TargetTemperature}°C - {HysteresisOffset}°C ({Reason})",
                     roomHeatingSchedule.Room, currentTemperature, desiredTemperature, HysteresisOffset, reason);

                roomState.HeatingActive = true;
                stateChanged = true;
            }
        }

        // Update temperature and active track if changed
        if (roomState.CurrentTemperature != currentTemperature)
        {
            roomState.CurrentTemperature = currentTemperature;
            stateChanged = true;
        }

        if (roomState.ActiveScheduleTrackId != effectiveTrack.Id)
        {
            roomState.ActiveScheduleTrackId = effectiveTrack.Id;
            stateChanged = true;
        }

        // Update timestamp and send to API if anything changed
        if (stateChanged)
        {
            roomState.LastUpdated = DateTimeOffset.UtcNow;
            await UpdateRoomStateToApi(roomState);
        }
    }

    private async Task<HeatingScheduleTrack?> FindCurrentActiveSchedule(RoomSchedule schedule, DateTime now, Days currentDay)
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
                if (!await MeetsSpecialConditions(schedule.Room, track))
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
                if (!await MeetsSpecialConditions(schedule.Room, track))
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

    private async Task<HeatingScheduleTrack?> FindPreHeatSchedule(RoomSchedule schedule, DateTime now, Days currentDay, double currentActiveTemperature)
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
                    if (!await MeetsSpecialConditions(schedule.Room, track))
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
        ICustomSwitchEntity? plug = roomHeatingSchedule.Room switch
        {
            Room.Kitchen => _namedEntities.KitchenHeaterSmartPlugOnOff,
            Room.GamesRoom => _namedEntities.GamesRoomHeaterSmartPlugOnOff,
            Room.DiningRoom => _namedEntities.DiningRoomHeaterSmartPlugOnOff,
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

    private double? GetCurrentTemperatureForRoom(RoomSchedule roomHeatingSchedule)
    {
        return roomHeatingSchedule.Room switch
        {
            Room.Kitchen => _namedEntities.KitchenTemperature.State,
            Room.GamesRoom => _namedEntities.GamesRoomDeskTemperature.State,
            Room.DiningRoom => _namedEntities.DiningRoomClimateTemperature.State,
            Room.Lounge => null,
            Room.DownstairsBathroom => null,
            Room.Bedroom1 => _namedEntities.Bedroom1Temperature.State,
            Room.Bedroom2 => null,
            Room.Bedroom3 => null,
            Room.UpstairsBathroom => null,
            _ => null
        };
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
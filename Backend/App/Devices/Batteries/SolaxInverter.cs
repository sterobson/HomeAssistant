using HomeAssistant.Devices.Meters;
using HomeAssistant.Services;
using HomeAssistant.Shared;
using HomeAssistantGenerated;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HomeAssistant.Devices.Batteries;

public class SolaxInverter : IHomeBattery, ISolarPanels
{
    private readonly IHaContext _ha;
    private readonly HistoryService _historyService;
    private readonly IDeviceSettingsPersistenceService _settingsPersistence;
    private readonly ILogger<SolaxInverter> _logger;
    private readonly HomeAssistantGenerated.Services _services;

    private NumericSensorEntity? _batteryChargePercentSensor;
    private SelectEntity? _chargerUseMode;
    private SelectEntity? _chargerManualMode;
    private NumberEntity? _batteryChargeMaxCurrent;
    private NumericSensorEntity? _totalBatteryPowerCharge;
    private NumericSensorEntity? _totalPvPowerSensor;
    private NumberEntity? _exportLimitW;

    private readonly List<Func<ValueChange<double?, NumericSensorEntity>, Task>> _batteryChargePercentCallbacks = [];
    private readonly List<Func<Task>> _batteryUseModeCallbacks = [];
    private List<IDisposable> _subscriptions = [];
    private bool _subscriptionsActive;
    private readonly SemaphoreSlim _rebindLock = new(1, 1);

    public double? CurrentChargePercent => _batteryChargePercentSensor?.State;

    public double BatteryCapacitykWh { get; private set; }

    public int MaxChargeCurrentAmps { get; private set; }

    public double MaximumExportRateW => _exportLimitW?.State ?? 0;

    public SolaxInverter(IHaContext ha, HistoryService historyService, IDeviceSettingsPersistenceService settingsPersistence, ILogger<SolaxInverter> logger)
    {
        _ha = ha;
        _historyService = historyService;
        _settingsPersistence = settingsPersistence;
        _logger = logger;
        _services = new(ha);

        BindEntities(settingsPersistence.GetSettingsAsync().GetAwaiter().GetResult());

        settingsPersistence.SettingsUpdated += async () =>
        {
            try
            {
                await RebindEntitiesAsync();
            }
            catch (ObjectDisposedException)
            {
                _logger.LogDebug("Ignoring SettingsUpdated callback - app context has been disposed");
            }
        };
    }

    private void BindEntities(DeviceSettingsDto settings)
    {
        BatterySettingsDto? battery = settings.Battery;

        _batteryChargePercentSensor = BindSensor(battery?.BatteryChargePercentSensorId, "Battery charge percent sensor");
        _chargerUseMode = BindSelect(battery?.ChargerUseModeSelectorId, "Charger use mode selector");
        _chargerManualMode = BindSelect(battery?.ManualModeSelectorId, "Manual mode selector");
        _batteryChargeMaxCurrent = BindNumber(battery?.BatteryChargeMaxCurrentNumberId, "Battery charge max current");
        _totalBatteryPowerCharge = BindSensor(battery?.TotalBatteryPowerChargeSensorId, "Total battery power charge sensor");
        _totalPvPowerSensor = BindSensor(battery?.TotalPvPowerSensorId, "Total PV power sensor");
        _exportLimitW = BindNumber(battery?.ExportLimitNumberId, "Export control limit");

        BatteryCapacitykWh = battery?.BatteryCapacityKwh ?? 0;
        if (BatteryCapacitykWh == 0)
        {
            _logger.LogWarning("Battery capacity (kWh) not configured in device settings");
        }

        MaxChargeCurrentAmps = battery?.MaxChargeCurrentAmps ?? 0;
        if (MaxChargeCurrentAmps == 0)
        {
            _logger.LogWarning("Max charge current (A) not configured in device settings");
        }
    }

    private NumericSensorEntity? BindSensor(string? entityId, string settingName)
    {
        if (!string.IsNullOrEmpty(entityId))
        {
            return new NumericSensorEntity(_ha, entityId);
        }

        _logger.LogWarning("{SettingName} not configured in device settings", settingName);
        return null;
    }

    private SelectEntity? BindSelect(string? entityId, string settingName)
    {
        if (!string.IsNullOrEmpty(entityId))
        {
            return new SelectEntity(_ha, entityId);
        }

        _logger.LogWarning("{SettingName} not configured in device settings", settingName);
        return null;
    }

    private NumberEntity? BindNumber(string? entityId, string settingName)
    {
        if (!string.IsNullOrEmpty(entityId))
        {
            return new NumberEntity(_ha, entityId);
        }

        _logger.LogWarning("{SettingName} not configured in device settings", settingName);
        return null;
    }

    public void OnBatteryChargePercentChanged(Func<ValueChange<double?, NumericSensorEntity>, Task> action)
    {
        _batteryChargePercentCallbacks.Add(action);
        EnsureSubscribed();
    }

    public void OnBatteryUseModeChanged(Func<Task> action)
    {
        _batteryUseModeCallbacks.Add(action);
        EnsureSubscribed();
    }

    private void EnsureSubscribed()
    {
        if (_subscriptionsActive) return;
        _subscriptionsActive = true;
        SubscribeToBatteryChargePercent();
        SubscribeToBatteryUseMode();
    }

    private void SubscribeToBatteryChargePercent()
    {
        if (_batteryChargePercentCallbacks.Count == 0 || _batteryChargePercentSensor == null) return;

        NumericSensorEntity sensor = _batteryChargePercentSensor;
        IDisposable subscription = sensor.StateChanges().SubscribeAsync(async (value) =>
        {
            ValueChange<double?, NumericSensorEntity> valueChange = new(value.Old?.State, value.New?.State, sensor);
            foreach (Func<ValueChange<double?, NumericSensorEntity>, Task> callback in _batteryChargePercentCallbacks)
            {
                try
                {
                    await callback(valueChange);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in battery charge percent callback");
                }
            }
        });
        _subscriptions.Add(subscription);
    }

    private void SubscribeToBatteryUseMode()
    {
        if (_batteryUseModeCallbacks.Count == 0) return;

        if (_chargerUseMode != null)
        {
            IDisposable useModeSubscription = _chargerUseMode.StateChanges().SubscribeAsync(async (value) =>
            {
                foreach (Func<Task> callback in _batteryUseModeCallbacks)
                {
                    try
                    {
                        await callback();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in battery use mode callback");
                    }
                }
            });
            _subscriptions.Add(useModeSubscription);
        }

        if (_chargerManualMode != null)
        {
            IDisposable manualModeSubscription = _chargerManualMode.StateChanges().SubscribeAsync(async (value) =>
            {
                foreach (Func<Task> callback in _batteryUseModeCallbacks)
                {
                    try
                    {
                        await callback();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in battery manual mode callback");
                    }
                }
            });
            _subscriptions.Add(manualModeSubscription);
        }
    }

    private async Task RebindEntitiesAsync()
    {
        await _rebindLock.WaitAsync();
        try
        {
            foreach (IDisposable subscription in _subscriptions)
            {
                subscription.Dispose();
            }
            _subscriptions = [];

            DeviceSettingsDto settings = await _settingsPersistence.GetSettingsAsync();
            BindEntities(settings);

            SubscribeToBatteryChargePercent();
            SubscribeToBatteryUseMode();
        }
        finally
        {
            _rebindLock.Release();
        }
    }

    public BatteryState GetHomeBatteryState()
    {
        if (_chargerUseMode == null || _chargerManualMode == null)
        {
            _logger.LogWarning("Cannot get battery state: charger use mode or manual mode entity not configured");
            return BatteryState.Unknown;
        }

        string? chargerUseMode = _chargerUseMode.State;
        string? chargerManualMode = _chargerManualMode.State;

        return chargerUseMode switch
        {
            "Smart Schedule" => BatteryState.NormalTOU,
            "Manual Mode" => chargerManualMode switch
            {
                "Stop Charge and Discharge" => BatteryState.Stopped,
                "Force Charge" => BatteryState.ForceCharging,
                "Force Discharge" => BatteryState.ForceDischarging,
                _ => BatteryState.Unknown,
            },
            _ => BatteryState.Unknown,
        };
    }

    public async Task<IReadOnlyList<HistoryEntry<BatteryState>>> GetBatteryStateHistoryEntriesAsync(DateTime from, DateTime to)
    {
        if (_chargerUseMode == null || _chargerManualMode == null)
        {
            return [new HistoryEntry<BatteryState> { LastChanged = from, State = BatteryState.Unknown }];
        }

        Task<IReadOnlyList<HistoryTextEntry>> useModeHistoryTask = _historyService.GetEntityTextHistory(_chargerUseMode.EntityId, from.AddMonths(-1), to);
        Task<IReadOnlyList<HistoryTextEntry>> manualModeHistoryTask = _historyService.GetEntityTextHistory(_chargerManualMode.EntityId, from.AddMonths(-1), to);

        await Task.WhenAll(useModeHistoryTask, manualModeHistoryTask);

        IReadOnlyList<HistoryTextEntry> useModeHistory = await useModeHistoryTask;
        IReadOnlyList<HistoryTextEntry> manualModeHistory = await manualModeHistoryTask;

        List<HistoryEntry<BatteryState>> results = [new HistoryEntry<BatteryState> { LastChanged = from, State = BatteryState.Unknown }];

        List<HistoryTextEntry> allDates = useModeHistory.Union(manualModeHistory).OrderBy(e => e.LastChanged).ToList();

        string currentUseMode = "", currentManualMode = "";
        DateTime lastDate = DateTime.MinValue;
        foreach (HistoryTextEntry entry in allDates)
        {
            if (useModeHistory.Contains(entry))
            {
                currentUseMode = entry.State ?? string.Empty;
            }
            else
            {
                currentManualMode = entry.State ?? string.Empty;
            }

            BatteryState currentBatteryState = currentUseMode switch
            {
                "Smart Schedule" => BatteryState.NormalTOU,
                "Manual Mode" => currentManualMode switch
                {
                    "Stop Charge and Discharge" => BatteryState.Stopped,
                    "Force Charge" => BatteryState.ForceCharging,
                    "Force Discharge" => BatteryState.ForceDischarging,
                    _ => BatteryState.Unknown,
                },
                _ => BatteryState.Unknown,
            };

            if (entry.LastChanged < from)
            {
                results[0].State = currentBatteryState;
            }
            else if (entry.LastChanged == lastDate)
            {
                results[^1].State = currentBatteryState;
            }
            else if (entry.LastChanged < to)
            {
                results.Add(new HistoryEntry<BatteryState>
                {
                    LastChanged = entry.LastChanged,
                    State = currentBatteryState
                });
            }
        }

        return results;
    }

    public void SetHomeBatteryState(BatteryState desiredHomeBatteryState)
    {
        if (_chargerUseMode == null || _chargerManualMode == null)
        {
            _logger.LogWarning("Cannot set battery state to {DesiredState}: charger use mode or manual mode entity not configured", desiredHomeBatteryState);
            return;
        }

        BatteryState currentHomeBatteryState = GetHomeBatteryState();

        if (desiredHomeBatteryState != currentHomeBatteryState)
        {
            switch (desiredHomeBatteryState)
            {
                case BatteryState.NormalTOU:
                case BatteryState.Unknown:
                    _chargerUseMode.CallService("select_option", new { option = "Smart Schedule" });
                    break;

                case BatteryState.ForceCharging:
                case BatteryState.ForceDischarging:
                case BatteryState.Stopped:
                    _chargerUseMode.CallService("select_option", new { option = "Manual Mode" });
                    break;
            }

            switch (desiredHomeBatteryState)
            {
                case BatteryState.ForceCharging:
                    _chargerManualMode.CallService("select_option", new { option = "Force Charge" });
                    break;
                case BatteryState.ForceDischarging:
                    _chargerManualMode.CallService("select_option", new { option = "Force Discharge" });
                    break;
                case BatteryState.Stopped:
                    _chargerManualMode.CallService("select_option", new { option = "Stop Charge and Discharge" });
                    break;
            }
        }
    }

    public void SetMaxChargeCurrentHeadroom(int headroom)
    {
        if (_batteryChargeMaxCurrent == null || MaxChargeCurrentAmps == 0) return;

        _batteryChargeMaxCurrent.CallService("set_value", new { value = (MaxChargeCurrentAmps - headroom).ToString() });
    }

    public async Task<IReadOnlyList<NumericHistoryEntry>> GetTotalBatteryPowerChargeHistoryEntriesAsync(DateTime from, DateTime to)
    {
        if (_totalBatteryPowerCharge == null) return [];

        return await _historyService.GetEntityNumericHistory(_totalBatteryPowerCharge.EntityId, from, to);
    }

    public async Task<IReadOnlyList<NumericHistoryEntry>> GetTotalSolarPanelPowerHistoryEntriesAsync(DateTime from, DateTime to)
    {
        if (_totalPvPowerSensor == null) return [];

        return await _historyService.GetEntityNumericHistory(_totalPvPowerSensor.EntityId, from, to);
    }
}

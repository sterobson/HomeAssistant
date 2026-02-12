using HomeAssistant.Devices.Meters;
using HomeAssistant.Services;
using HomeAssistant.Shared;
using HomeAssistantGenerated;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HomeAssistant.Devices.CarChargers;

internal class HypervoltPro3 : ICarCharger
{
    private readonly IHaContext _ha;
    private readonly HistoryService _historyService;
    private readonly IDeviceSettingsPersistenceService _settingsPersistence;
    private readonly ILogger<HypervoltPro3> _logger;

    private NumericSensorEntity? _chargerCurrentSensor;
    private Func<ValueChange<double?, NumericSensorEntity>, Task>? _chargerCurrentCallback;
    private List<IDisposable> _subscriptions = [];
    private readonly object _rebindLock = new();

    public double? ChargerCurrent => _chargerCurrentSensor?.State;

    public HypervoltPro3(IHaContext ha, HistoryService historyService, IDeviceSettingsPersistenceService settingsPersistence, ILogger<HypervoltPro3> logger)
    {
        _ha = ha;
        _historyService = historyService;
        _settingsPersistence = settingsPersistence;
        _logger = logger;

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
        CarChargerSettingsDto? carCharger = settings.CarCharger;

        if (!string.IsNullOrEmpty(carCharger?.ChargerCurrentSensorId))
        {
            _chargerCurrentSensor = new NumericSensorEntity(_ha, carCharger.ChargerCurrentSensorId);
        }
        else
        {
            _chargerCurrentSensor = null;
            _logger.LogWarning("Car charger current sensor not configured in device settings");
        }
    }

    public void OnChargerCurrentChanged(Func<ValueChange<double?, NumericSensorEntity>, Task> action)
    {
        _chargerCurrentCallback = action;
        SubscribeToChargerCurrent();
    }

    private void SubscribeToChargerCurrent()
    {
        if (_chargerCurrentCallback == null || _chargerCurrentSensor == null) return;

        NumericSensorEntity sensor = _chargerCurrentSensor;
        Func<ValueChange<double?, NumericSensorEntity>, Task> callback = _chargerCurrentCallback;
        IDisposable subscription = sensor.StateChanges().SubscribeAsync(async value =>
        {
            ValueChange<double?, NumericSensorEntity> valueChange = new(value.Old?.State, value.New?.State, sensor);
            await callback(valueChange);
        });
        _subscriptions.Add(subscription);
    }

    private async Task RebindEntitiesAsync()
    {
        lock (_rebindLock)
        {
            foreach (IDisposable subscription in _subscriptions)
            {
                subscription.Dispose();
            }
            _subscriptions = [];

            BindEntities(_settingsPersistence.GetSettingsAsync().GetAwaiter().GetResult());

            SubscribeToChargerCurrent();
        }

        await Task.CompletedTask;
    }

    public async Task<IReadOnlyList<NumericHistoryEntry>> GetChargerCurrentHistoryEntriesAsync(DateTime from, DateTime to)
    {
        if (_chargerCurrentSensor == null) return [];

        return await _historyService.GetEntityNumericHistory(_chargerCurrentSensor.EntityId, from, to);
    }
}

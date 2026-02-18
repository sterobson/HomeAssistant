using HomeAssistant.Services;
using HomeAssistant.Shared;
using HomeAssistantGenerated;
using System.Threading;
using System.Threading.Tasks;

namespace HomeAssistant.Devices.Meters;

internal class OctopusElectricityMeter : IElectricityMeter
{
    private readonly IHaContext _ha;
    private readonly IDeviceSettingsPersistenceService _settingsPersistence;
    private readonly ILogger<OctopusElectricityMeter> _logger;

    private NumericSensorEntity? _currentRateSensor;

    private Func<ValueChange<double?, NumericSensorEntity>, Task>? _currentRateCallback;
    private IDisposable? _subscription;
    private readonly SemaphoreSlim _rebindLock = new(1, 1);

    public double? CurrentRatePerKwh => _currentRateSensor?.State;

    public OctopusElectricityMeter(IHaContext ha, IDeviceSettingsPersistenceService settingsPersistence, ILogger<OctopusElectricityMeter> logger)
    {
        _ha = ha;
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
        BatterySettingsDto? battery = settings.Battery;

        if (!string.IsNullOrEmpty(battery?.ElectricityRateSensorId))
        {
            _currentRateSensor = new NumericSensorEntity(_ha, battery.ElectricityRateSensorId);
        }
        else
        {
            _currentRateSensor = null;
            _logger.LogWarning("Electricity rate sensor not configured in device settings");
        }
    }

    public void OnCurrentRatePerKwhChanged(Func<ValueChange<double?, NumericSensorEntity>, Task> action)
    {
        _currentRateCallback = action;
        SubscribeToCurrentRate();
    }

    private void SubscribeToCurrentRate()
    {
        if (_currentRateCallback == null || _currentRateSensor == null) return;

        NumericSensorEntity sensor = _currentRateSensor;
        Func<ValueChange<double?, NumericSensorEntity>, Task> callback = _currentRateCallback;
        _subscription = sensor.StateChanges().SubscribeAsyncConcurrent(async (value) =>
        {
            try
            {
                ValueChange<double?, NumericSensorEntity> valueChange = new(value.Old?.State, value.New?.State, sensor);
                await callback(valueChange);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in electricity rate change callback");
            }
        });
    }

    private async Task RebindEntitiesAsync()
    {
        await _rebindLock.WaitAsync();
        try
        {
            _subscription?.Dispose();
            _subscription = null;

            DeviceSettingsDto settings = await _settingsPersistence.GetSettingsAsync();
            BindEntities(settings);

            SubscribeToCurrentRate();
        }
        finally
        {
            _rebindLock.Release();
        }
    }
}

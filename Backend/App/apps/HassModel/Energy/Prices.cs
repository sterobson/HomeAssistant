using HomeAssistant.Services;
using HomeAssistant.Shared;
using HomeAssistantGenerated;
using System.Threading.Tasks;

namespace HassModel.Energy;

[NetDaemonApp]
public class Prices
{
    private readonly IHaContext _ha;
    private readonly IDeviceSettingsPersistenceService _settingsPersistence;
    private readonly ILogger<Prices> _logger;

    private NumericSensorEntity? _currentRateSensor;
    private IDisposable? _subscription;
    private readonly object _rebindLock = new();

    public Prices(IHaContext ha, IDeviceSettingsPersistenceService settingsPersistence, ILogger<Prices> logger)
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
            Subscribe();
        }
        else
        {
            _currentRateSensor = null;
            _logger.LogWarning("Electricity rate sensor not configured in device settings - price logging disabled");
        }
    }

    private void Subscribe()
    {
        if (_currentRateSensor == null) return;

        _subscription = _currentRateSensor.StateChanges().SubscribeAsync(async e =>
        {
            _logger.LogDebug("Electricity import prices changed from {OldValue} to {NewValue} per kWh", e.Old?.State, e.New?.State);
        });
    }

    private async Task RebindEntitiesAsync()
    {
        lock (_rebindLock)
        {
            _subscription?.Dispose();
            _subscription = null;

            BindEntities(_settingsPersistence.GetSettingsAsync().GetAwaiter().GetResult());
        }

        await Task.CompletedTask;
    }
}

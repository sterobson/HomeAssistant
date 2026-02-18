using HomeAssistant.Shared;
using HomeAssistantGenerated;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HomeAssistant.Services.Energy;

internal class PowerMonitorService
{
    private readonly IHaContext _ha;
    private readonly IDeviceSettingsPersistenceService _settingsPersistence;
    private readonly ISignalRConnectionService _signalRConnection;
    private readonly IPowerMonitorApiClient _apiClient;
    private readonly WebSynchronisationConfiguration _configuration;
    private readonly ILogger<PowerMonitorService> _logger;

    private NumericSensorEntity? _solarPowerSensor;
    private NumericSensorEntity? _batteryPowerSensor;
    private NumericSensorEntity? _gridImportPowerSensor;
    private NumericSensorEntity? _gridExportPowerSensor;

    private CancellationTokenSource? _monitorCts;
    private readonly object _lock = new();

    public PowerMonitorService(
        IHaContext ha,
        IDeviceSettingsPersistenceService settingsPersistence,
        ISignalRConnectionService signalRConnection,
        IPowerMonitorApiClient apiClient,
        WebSynchronisationConfiguration configuration,
        ILogger<PowerMonitorService> logger)
    {
        _ha = ha;
        _settingsPersistence = settingsPersistence;
        _signalRConnection = signalRConnection;
        _apiClient = apiClient;
        _configuration = configuration;
        _logger = logger;
    }

    public void Initialize()
    {
        DeviceSettingsDto settings = _settingsPersistence.GetSettingsAsync().GetAwaiter().GetResult();
        BindSensors(settings);

        _settingsPersistence.SettingsUpdated += async () =>
        {
            try
            {
                DeviceSettingsDto updatedSettings = await _settingsPersistence.GetSettingsAsync();
                BindSensors(updatedSettings);
                _logger.LogInformation("Power monitor sensors rebound after settings update");
            }
            catch (ObjectDisposedException)
            {
                _logger.LogDebug("Ignoring SettingsUpdated callback - app context has been disposed");
            }
        };

        _signalRConnection.On<JsonElement>("start-power-monitor", async (data) =>
        {
            _logger.LogInformation("Received start-power-monitor signal");
            await StartMonitoringAsync();
        });

        _logger.LogInformation("Power monitor service initialized");
    }

    private void BindSensors(DeviceSettingsDto settings)
    {
        RealtimePowerSettingsDto? power = settings.RealtimePower;

        _solarPowerSensor = BindSensor(power?.SolarPowerSensorId, "Solar power sensor");
        _batteryPowerSensor = BindSensor(power?.BatteryPowerSensorId, "Battery power sensor");
        _gridImportPowerSensor = BindSensor(power?.GridImportPowerSensorId, "Grid import power sensor");
        _gridExportPowerSensor = BindSensor(power?.GridExportPowerSensorId, "Grid export power sensor");
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

    private async Task StartMonitoringAsync()
    {
        lock (_lock)
        {
            _monitorCts?.Cancel();
            _monitorCts?.Dispose();
            _monitorCts = new CancellationTokenSource();
        }

        CancellationToken token = _monitorCts!.Token;

        if (_solarPowerSensor == null || _batteryPowerSensor == null ||
            _gridImportPowerSensor == null || _gridExportPowerSensor == null)
        {
            _logger.LogWarning("Power monitor cannot start — not all sensors are configured");
            return;
        }

        string houseId = _configuration.HouseId;
        if (string.IsNullOrEmpty(houseId))
        {
            _logger.LogWarning("Power monitor cannot start — HouseId not configured");
            return;
        }

        _logger.LogInformation("Starting power monitor for 60 seconds (12 ticks)");

        try
        {
            using PeriodicTimer timer = new(TimeSpan.FromSeconds(5));
            int tickCount = 0;

            while (tickCount < 12 && !token.IsCancellationRequested)
            {
                double solarW = _solarPowerSensor.State ?? 0;
                double batteryW = _batteryPowerSensor.State ?? 0;
                double gridImportW = _gridImportPowerSensor.State ?? 0;
                double gridExportW = _gridExportPowerSensor.State ?? 0;
                double gridW = gridImportW - gridExportW;
                // Calculate house consumption from energy balance:
                // house = solar + grid(net) - battery(charging)
                double houseW = Math.Max(0, solarW + gridW - batteryW);

                try
                {
                    await _apiClient.PostSnapshotAsync(houseId, solarW, batteryW, houseW, gridW);
                    _logger.LogDebug("Power snapshot {Tick}/12: solar={Solar}W battery={Battery}W house={House}W grid={Grid}W",
                        tickCount + 1, solarW, batteryW, houseW, gridW);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to post power snapshot {Tick}/12", tickCount + 1);
                }

                tickCount++;

                if (tickCount < 12)
                {
                    await timer.WaitForNextTickAsync(token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Power monitor cancelled (new session started or service stopping)");
        }

        _logger.LogInformation("Power monitor session complete");
    }
}

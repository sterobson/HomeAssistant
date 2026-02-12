using HomeAssistant.Shared;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace HomeAssistant.Services;

public interface IDeviceSettingsPersistenceService
{
    event Func<Task>? SettingsUpdated;

    Task<DeviceSettingsDto> GetSettingsAsync();

    Task StartAsync();
}

internal class DeviceSettingsPersistenceService : IDeviceSettingsPersistenceService
{
    private readonly ILogger<DeviceSettingsPersistenceService> _logger;
    private readonly IDeviceSettingsApiClient? _apiClient;
    private readonly WebSynchronisationConfiguration _configuration;
    private readonly ISignalRConnectionService? _signalRConnection;
    private readonly string _localStoragePath;
    private const string _storageFileName = "device-settings.json";
    private const int _cacheExpirationMinutes = 10;

    private DeviceSettingsDto? _cachedSettings;
    private DateTimeOffset _lastRefreshTime = DateTimeOffset.MinValue;

    public event Func<Task>? SettingsUpdated;

    public DeviceSettingsPersistenceService(
        ILogger<DeviceSettingsPersistenceService> logger,
        WebSynchronisationConfiguration configuration,
        IDeviceSettingsApiClient? apiClient = null,
        ISignalRConnectionService? signalRConnection = null)
    {
        _logger = logger;
        _configuration = configuration;
        _apiClient = apiClient;
        _signalRConnection = signalRConnection;

        string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appFolder = Path.Combine(appDataPath, "HomeAssistant");
        Directory.CreateDirectory(appFolder);
        _localStoragePath = Path.Combine(appFolder, _storageFileName);
    }

    public async Task StartAsync()
    {
        await LoadFromLocalStorageAsync();

        if (_signalRConnection != null)
        {
            _signalRConnection.On<object>("device-settings-changed", async (data) =>
            {
                _logger.LogDebug("Received 'device-settings-changed' notification from SignalR for house {HouseId}", _configuration.HouseId);
                _lastRefreshTime = DateTimeOffset.MinValue;
                await RefreshSettingsFromApiAsync();

                if (SettingsUpdated != null)
                {
                    await SettingsUpdated.Invoke();
                }
            });

            await _signalRConnection.StartAsync();
        }
    }

    public async Task<DeviceSettingsDto> GetSettingsAsync()
    {
        TimeSpan timeSinceLastRefresh = DateTimeOffset.UtcNow - _lastRefreshTime;
        if (_cachedSettings == null || timeSinceLastRefresh.TotalMinutes >= _cacheExpirationMinutes)
        {
            await RefreshSettingsFromApiAsync();
        }

        return _cachedSettings ?? new();
    }

    private async Task RefreshSettingsFromApiAsync()
    {
        if (_apiClient == null || string.IsNullOrEmpty(_configuration.HouseId))
        {
            _logger.LogWarning("Cannot refresh device settings - API client or HouseId not configured");
            return;
        }

        try
        {
            _logger.LogDebug("Refreshing device settings from API for house {HouseId}", _configuration.HouseId);
            DeviceSettingsDto settings = await _apiClient.GetSettingsAsync(_configuration.HouseId);

            _cachedSettings = settings;
            _lastRefreshTime = DateTimeOffset.UtcNow;
            _logger.LogDebug("Successfully refreshed device settings from API");

            await SaveToLocalStorageAsync(settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing device settings from API for house {HouseId}", _configuration.HouseId);
        }
    }

    private async Task LoadFromLocalStorageAsync()
    {
        try
        {
            if (!File.Exists(_localStoragePath))
            {
                _logger.LogDebug("No local device settings storage found at {Path}", _localStoragePath);
                return;
            }

            _logger.LogDebug("Loading device settings from local storage at {Path}", _localStoragePath);
            string json = await File.ReadAllTextAsync(_localStoragePath);
            DeviceSettingsDto? dto = JsonSerializer.Deserialize<DeviceSettingsDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (dto != null)
            {
                _cachedSettings = dto;
                _lastRefreshTime = DateTimeOffset.UtcNow;
                _logger.LogDebug("Successfully loaded device settings from local storage");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading device settings from local storage at {Path}", _localStoragePath);
        }
    }

    private async Task SaveToLocalStorageAsync(DeviceSettingsDto settings)
    {
        try
        {
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(_localStoragePath, json);
            _logger.LogDebug("Saved device settings to local storage at {Path}", _localStoragePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving device settings to local storage at {Path}", _localStoragePath);
        }
    }
}

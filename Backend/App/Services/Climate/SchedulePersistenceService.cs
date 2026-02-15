using HomeAssistant.Shared.Climate;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace HomeAssistant.Services.Climate;

/// <summary>
/// Handles persistence and caching of heating schedules with SignalR integration
/// </summary>
internal interface ISchedulePersistenceService
{
    /// <summary>
    /// Event fired when schedules have been updated via SignalR notification
    /// Subscribe to this to react to schedule changes
    /// </summary>
    event Func<Task>? SchedulesUpdated;

    /// <summary>
    /// Gets schedules from cache or refreshes from API if cache is stale (>10 minutes old)
    /// </summary>
    Task<RoomSchedules> GetSchedulesAsync();

    /// <summary>
    /// Starts the SignalR connection to listen for schedule updates
    /// </summary>
    Task StartAsync();
}

internal class SchedulePersistenceService : ISchedulePersistenceService
{
    private readonly ILogger<SchedulePersistenceService> _logger;
    private readonly IScheduleApiClient? _scheduleApiClient;
    private readonly WebSynchronisationConfiguration _configuration;
    private readonly ISignalRConnectionService? _signalRConnection;
    private readonly string _scheduleStoragePath;
    private const string _scheduleStorageFileName = "heating-schedules.json";
    private const int _cacheExpirationMinutes = 10;

    private RoomSchedules? _cachedSchedules;
    private DateTimeOffset _lastRefreshTime = DateTimeOffset.MinValue;

    /// <summary>
    /// Event fired when schedules have been updated via SignalR notification
    /// </summary>
    public event Func<Task>? SchedulesUpdated;

    public SchedulePersistenceService(
        ILogger<SchedulePersistenceService> logger,
        WebSynchronisationConfiguration configuration,
        IScheduleApiClient? scheduleApiClient = null,
        ISignalRConnectionService? signalRConnection = null)
    {
        _logger = logger;
        _configuration = configuration;
        _scheduleApiClient = scheduleApiClient;
        _signalRConnection = signalRConnection;

        // Set up local storage path
        string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appFolder = Path.Combine(appDataPath, "HomeAssistant");
        Directory.CreateDirectory(appFolder);
        _scheduleStoragePath = Path.Combine(appFolder, _scheduleStorageFileName);
    }

    public async Task StartAsync()
    {
        // Load from local storage on startup
        await LoadFromLocalStorageAsync();

        // Register SignalR handler and start connection
        if (_signalRConnection != null)
        {
            _signalRConnection.On<object>("schedules-changed", async (data) =>
            {
                _logger.LogDebug("Received 'schedules-changed' notification from SignalR for house {HouseId}", _configuration.HouseId);
                _lastRefreshTime = DateTimeOffset.MinValue; // Invalidate cache
                await RefreshSchedulesFromApiAsync();

                if (SchedulesUpdated != null)
                {
                    await SchedulesUpdated.Invoke();
                }
            });

            _signalRConnection.ConnectionRestored += async () =>
            {
                _logger.LogInformation("SignalR connection restored — refreshing schedules");
                _lastRefreshTime = DateTimeOffset.MinValue;
                await RefreshSchedulesFromApiAsync();

                if (SchedulesUpdated != null)
                {
                    await SchedulesUpdated.Invoke();
                }
            };

            await _signalRConnection.StartAsync();
        }
    }

    public async Task<RoomSchedules> GetSchedulesAsync()
    {
        // Check if cache is still valid (less than 10 minutes old)
        TimeSpan timeSinceLastRefresh = DateTimeOffset.UtcNow - _lastRefreshTime;
        if (_cachedSchedules == null || timeSinceLastRefresh.TotalMinutes >= _cacheExpirationMinutes)
        {
            // Caches is stale or old, so refresh it.
            await RefreshSchedulesFromApiAsync();
        }

        return _cachedSchedules ?? new();
    }

    private async Task RefreshSchedulesFromApiAsync()
    {
        if (_scheduleApiClient == null || string.IsNullOrEmpty(_configuration.HouseId))
        {
            _logger.LogWarning("Cannot refresh schedules - API client or HouseId not configured");
            return;
        }

        try
        {
            _logger.LogDebug("Refreshing schedules from API for house {HouseId}", _configuration.HouseId);
            RoomSchedules schedules = await _scheduleApiClient.GetSchedulesAsync(_configuration.HouseId);

            if (schedules.Rooms.Count > 0)
            {
                _cachedSchedules = schedules;
                _lastRefreshTime = DateTimeOffset.UtcNow;
                _logger.LogDebug("Successfully refreshed {Count} schedules from API", schedules.Rooms.Count);

                // Save to local storage
                await SaveToLocalStorageAsync(schedules);
            }
            else
            {
                _logger.LogWarning("No schedules returned from API for house {HouseId}", _configuration.HouseId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing schedules from API for house {HouseId}", _configuration.HouseId);
        }
    }

    private async Task LoadFromLocalStorageAsync()
    {
        try
        {
            if (!File.Exists(_scheduleStoragePath))
            {
                _logger.LogDebug("No local schedule storage found at {Path}", _scheduleStoragePath);
                return;
            }

            _logger.LogDebug("Loading schedules from local storage at {Path}", _scheduleStoragePath);
            string json = await File.ReadAllTextAsync(_scheduleStoragePath);
            RoomSchedulesDto? dto = JsonSerializer.Deserialize<RoomSchedulesDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (dto != null)
            {
                _cachedSchedules = ScheduleMapper.MapFromDto(dto);
                _lastRefreshTime = DateTimeOffset.UtcNow; // Treat loaded schedules as fresh
                _logger.LogDebug("Successfully loaded {Count} schedules from local storage", _cachedSchedules.Rooms.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading schedules from local storage at {Path}", _scheduleStoragePath);
        }
    }

    private async Task SaveToLocalStorageAsync(RoomSchedules schedules)
    {
        try
        {
            RoomSchedulesDto dto = ScheduleMapper.MapToDto(schedules);
            string json = JsonSerializer.Serialize(dto, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(_scheduleStoragePath, json);
            _logger.LogDebug("Saved schedules to local storage at {Path}", _scheduleStoragePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving schedules to local storage at {Path}", _scheduleStoragePath);
        }
    }
}

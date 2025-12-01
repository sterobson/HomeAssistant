using HomeAssistant.Shared.Climate;
using Microsoft.AspNetCore.SignalR.Client;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace HomeAssistant.Services.Climate;

/// <summary>
/// Handles persistence and caching of heating schedules with SignalR integration
/// </summary>
public interface ISchedulePersistenceService
{
    /// <summary>
    /// Gets schedules from cache or refreshes from API if cache is stale (>10 minutes old)
    /// </summary>
    Task<RoomSchedules?> GetSchedulesAsync();

    /// <summary>
    /// Uploads schedules to Azure API
    /// </summary>
    Task SetSchedulesAsync(RoomSchedules schedules);

    /// <summary>
    /// Starts the SignalR connection to listen for schedule updates
    /// </summary>
    Task StartAsync();
}

public class SchedulePersistenceService : ISchedulePersistenceService
{
    private readonly ILogger<SchedulePersistenceService> _logger;
    private readonly IScheduleApiClient? _scheduleApiClient;
    private readonly WebSynchronisationConfiguration _configuration;
    private readonly string _scheduleStoragePath;
    private const string _scheduleStorageFileName = "heating-schedules.json";
    private const int _cacheExpirationMinutes = 10;

    private RoomSchedules? _cachedSchedules;
    private DateTimeOffset _lastRefreshTime = DateTimeOffset.MinValue;
    private HubConnection? _hubConnection;

    public SchedulePersistenceService(
        ILogger<SchedulePersistenceService> logger,
        WebSynchronisationConfiguration configuration,
        IScheduleApiClient? scheduleApiClient = null)
    {
        _logger = logger;
        _configuration = configuration;
        _scheduleApiClient = scheduleApiClient;

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

        // Connect to SignalR for real-time updates
        await ConnectToSignalRAsync();
    }

    public async Task<RoomSchedules?> GetSchedulesAsync()
    {
        // Check if cache is still valid (less than 10 minutes old)
        TimeSpan timeSinceLastRefresh = DateTimeOffset.UtcNow - _lastRefreshTime;
        if (_cachedSchedules != null && timeSinceLastRefresh.TotalMinutes < _cacheExpirationMinutes)
        {
            _logger.LogDebug("Returning cached schedules (age: {Minutes:F1} minutes)", timeSinceLastRefresh.TotalMinutes);
            return _cachedSchedules;
        }

        // Cache is stale or empty, refresh from API
        await RefreshSchedulesFromApiAsync();
        return _cachedSchedules;
    }

    public async Task SetSchedulesAsync(RoomSchedules schedules)
    {
        if (_scheduleApiClient == null || string.IsNullOrEmpty(_configuration.HouseId))
        {
            _logger.LogWarning("Cannot upload schedules - API client or HouseId not configured");
            return;
        }

        try
        {
            await _scheduleApiClient.SetSchedulesAsync(_configuration.HouseId, schedules);
            _logger.LogInformation("Successfully uploaded schedules to API for house {HouseId}", _configuration.HouseId);

            // Update cache
            _cachedSchedules = schedules;
            _lastRefreshTime = DateTimeOffset.UtcNow;

            // Save to local storage
            await SaveToLocalStorageAsync(schedules);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading schedules to API for house {HouseId}", _configuration.HouseId);
            throw;
        }
    }

    private async Task RefreshSchedulesFromApiAsync()
    {
        if (_scheduleApiClient == null || string.IsNullOrEmpty(_configuration.HouseId))
        {
            _logger.LogDebug("Cannot refresh schedules - API client or HouseId not configured");
            return;
        }

        try
        {
            _logger.LogInformation("Refreshing schedules from API for house {HouseId}", _configuration.HouseId);
            RoomSchedules schedules = await _scheduleApiClient.GetSchedulesAsync(_configuration.HouseId);

            if (schedules.Rooms.Count > 0)
            {
                _cachedSchedules = schedules;
                _lastRefreshTime = DateTimeOffset.UtcNow;
                _logger.LogInformation("Successfully refreshed {Count} schedules from API", schedules.Rooms.Count);

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
                _logger.LogInformation("No local schedule storage found at {Path}", _scheduleStoragePath);
                return;
            }

            _logger.LogInformation("Loading schedules from local storage at {Path}", _scheduleStoragePath);
            string json = await File.ReadAllTextAsync(_scheduleStoragePath);
            RoomSchedulesDto? dto = JsonSerializer.Deserialize<RoomSchedulesDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (dto != null)
            {
                _cachedSchedules = ScheduleMapper.MapFromDto(dto);
                _lastRefreshTime = DateTimeOffset.UtcNow; // Treat loaded schedules as fresh
                _logger.LogInformation("Successfully loaded {Count} schedules from local storage", _cachedSchedules.Rooms.Count);
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

    private async Task ConnectToSignalRAsync()
    {
        if (_scheduleApiClient == null || string.IsNullOrEmpty(_configuration.HouseId))
        {
            _logger.LogInformation("SignalR not configured - API client or HouseId missing");
            return;
        }

        try
        {
            _logger.LogInformation("Connecting to SignalR for schedule updates (house {HouseId})", _configuration.HouseId);
            string connectionInfoJson = await _scheduleApiClient.GetSignalRConnectionInfoAsync(_configuration.HouseId);

            using JsonDocument doc = JsonDocument.Parse(connectionInfoJson);
            JsonElement root = doc.RootElement;

            if (!root.TryGetProperty("url", out JsonElement urlElement) ||
                !root.TryGetProperty("accessToken", out JsonElement tokenElement))
            {
                _logger.LogError("SignalR connection info missing required properties (url or accessToken)");
                return;
            }

            string hubUrl = urlElement.GetString() ?? string.Empty;
            string accessToken = tokenElement.GetString() ?? string.Empty;

            if (string.IsNullOrEmpty(hubUrl) || string.IsNullOrEmpty(accessToken))
            {
                _logger.LogError("SignalR connection info contains empty url or accessToken");
                return;
            }

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                })
                .WithAutomaticReconnect()
                .Build();

            _hubConnection.On("SchedulesUpdated", async () =>
            {
                _logger.LogInformation("Received SchedulesUpdated notification from SignalR - invalidating cache");
                _lastRefreshTime = DateTimeOffset.MinValue; // Invalidate cache
                await RefreshSchedulesFromApiAsync();
            });

            _hubConnection.Closed += async (Exception? error) =>
            {
                if (error != null)
                {
                    _logger.LogError(error, "SignalR connection closed with error");
                }
                else
                {
                    _logger.LogWarning("SignalR connection closed");
                }
            };

            _hubConnection.Reconnecting += (Exception? error) =>
            {
                _logger.LogWarning(error, "SignalR connection lost, attempting to reconnect");
                return Task.CompletedTask;
            };

            _hubConnection.Reconnected += (string? connectionId) =>
            {
                _logger.LogInformation("SignalR reconnected with connection ID: {ConnectionId}", connectionId);
                return Task.CompletedTask;
            };

            await _hubConnection.StartAsync();
            _logger.LogInformation("Connected to SignalR for schedule updates");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting to SignalR for house {HouseId}", _configuration.HouseId);
        }
    }
}

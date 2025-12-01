using HomeAssistant.Shared.Climate;
using Microsoft.AspNetCore.SignalR.Client;
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
    private readonly string _scheduleStoragePath;
    private const string _scheduleStorageFileName = "heating-schedules.json";
    private const int _cacheExpirationMinutes = 10;

    private RoomSchedules? _cachedSchedules;
    private DateTimeOffset _lastRefreshTime = DateTimeOffset.MinValue;
    private HubConnection? _hubConnection;

    /// <summary>
    /// Event fired when schedules have been updated via SignalR notification
    /// </summary>
    public event Func<Task>? SchedulesUpdated;

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

            _hubConnection.On<object>("test-message", (data) =>
            {
                _logger.LogWarning("✅ RECEIVED 'test-message' from SignalR: {Data}", System.Text.Json.JsonSerializer.Serialize(data));
                return Task.CompletedTask;
            });

            _hubConnection.On<object>("test-message-all", (data) =>
            {
                _logger.LogWarning("✅ RECEIVED 'test-message-all' (broadcast to ALL) from SignalR: {Data}", System.Text.Json.JsonSerializer.Serialize(data));
                return Task.CompletedTask;
            });

            _hubConnection.On<object>("schedules-changed", async (data) =>
            {
                _logger.LogInformation("✅ RECEIVED 'schedules-changed' notification from SignalR for house {HouseId}", _configuration.HouseId);
                _lastRefreshTime = DateTimeOffset.MinValue; // Invalidate cache
                await RefreshSchedulesFromApiAsync();

                // Notify subscribers that schedules have been updated
                if (SchedulesUpdated != null)
                {
                    await SchedulesUpdated.Invoke();
                }
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
            _logger.LogInformation("Connected to SignalR for schedule updates (house {HouseId}, ConnectionId: {ConnectionId})",
                _configuration.HouseId, _hubConnection.ConnectionId);

            // Add this connection to the house group
            if (_scheduleApiClient != null && !string.IsNullOrEmpty(_hubConnection.ConnectionId))
            {
                try
                {
                    await AddToGroupAsync(_hubConnection.ConnectionId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to add connection to group - will rely on fallback");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting to SignalR for house {HouseId}", _configuration.HouseId);
        }
    }

    private async Task AddToGroupAsync(string connectionId)
    {
        if (_scheduleApiClient == null || string.IsNullOrEmpty(_configuration.HouseId))
        {
            _logger.LogDebug("Cannot add to group - API client or HouseId not configured");
            return;
        }

        try
        {
            _logger.LogInformation("Adding connection {ConnectionId} to group for house {HouseId}", connectionId, _configuration.HouseId);
            System.Net.Http.HttpResponseMessage response = await _scheduleApiClient.AddToGroupAsync(_configuration.HouseId, connectionId);
            response.EnsureSuccessStatusCode();
            _logger.LogInformation("Successfully added connection to SignalR group for house {HouseId}", _configuration.HouseId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding connection to group for house {HouseId}", _configuration.HouseId);
            throw;
        }
    }
}

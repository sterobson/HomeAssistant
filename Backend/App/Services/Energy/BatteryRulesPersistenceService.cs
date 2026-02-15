using HomeAssistant.Shared.Energy;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HomeAssistant.Services.Energy;

/// <summary>
/// Handles persistence and caching of battery zone rules with SignalR integration
/// </summary>
internal interface IBatteryRulesPersistenceService
{
    /// <summary>
    /// Event fired when battery rules have been updated via SignalR notification
    /// </summary>
    event Func<Task>? RulesUpdated;

    /// <summary>
    /// Gets battery rules from cache or refreshes from API if cache is stale
    /// </summary>
    Task<BatteryZoneRules> GetRulesAsync();

    /// <summary>
    /// Starts the SignalR connection to listen for battery rule updates
    /// </summary>
    Task StartAsync();
}

internal class BatteryRulesPersistenceService : IBatteryRulesPersistenceService
{
    private readonly ILogger<BatteryRulesPersistenceService> _logger;
    private readonly IBatteryRulesApiClient? _apiClient;
    private readonly WebSynchronisationConfiguration _configuration;
    private readonly ISignalRConnectionService? _signalRConnection;
    private readonly string _localStoragePath;
    private const string _storageFileName = "battery-zone-rules.json";
    private static readonly Random _jitterRandom = new();

    private BatteryZoneRules? _cachedRules;
    private Timer? _periodicRefreshTimer;

    public event Func<Task>? RulesUpdated;

    public BatteryRulesPersistenceService(
        ILogger<BatteryRulesPersistenceService> logger,
        WebSynchronisationConfiguration configuration,
        IBatteryRulesApiClient? apiClient = null,
        ISignalRConnectionService? signalRConnection = null)
    {
        _logger = logger;
        _configuration = configuration;
        _apiClient = apiClient;
        _signalRConnection = signalRConnection;

        // Set up local storage path
        string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appFolder = Path.Combine(appDataPath, "HomeAssistant");
        Directory.CreateDirectory(appFolder);
        _localStoragePath = Path.Combine(appFolder, _storageFileName);
    }

    public async Task StartAsync()
    {
        // Load from local storage first for immediate availability
        await LoadFromLocalStorageAsync();

        // Then fetch from API to ensure we have the latest rules
        await RefreshRulesFromApiAsync();

        // Register SignalR handler and start connection
        if (_signalRConnection != null)
        {
            _signalRConnection.On<object>("battery-rules-changed", async (data) =>
            {
                _logger.LogDebug("Received 'battery-rules-changed' notification from SignalR for house {HouseId}", _configuration.HouseId);
                bool success = await RefreshRulesFromApiAsync();

                if (success && RulesUpdated != null)
                {
                    await RulesUpdated.Invoke();
                }
            });

            _signalRConnection.ConnectionRestored += async () =>
            {
                _logger.LogInformation("SignalR connection restored — refreshing battery rules");
                bool success = await RefreshRulesFromApiAsync();

                if (success && RulesUpdated != null)
                {
                    await RulesUpdated.Invoke();
                }
            };

            await _signalRConnection.StartAsync();
        }

        // Start periodic refresh every hour (±30 seconds jitter)
        ScheduleNextPeriodicRefresh();
    }

    public async Task<BatteryZoneRules> GetRulesAsync()
    {
        return _cachedRules ?? new();
    }

    private void ScheduleNextPeriodicRefresh()
    {
        int jitterSeconds = _jitterRandom.Next(-30, 31);
        TimeSpan interval = TimeSpan.FromHours(1) + TimeSpan.FromSeconds(jitterSeconds);
        _logger.LogDebug("Next periodic battery rules refresh in {Interval}", interval);

        _periodicRefreshTimer = new Timer(async _ =>
        {
            _logger.LogDebug("Performing periodic battery rules refresh");
            bool success = await RefreshRulesFromApiAsync();

            if (success && RulesUpdated != null)
            {
                await RulesUpdated.Invoke();
            }

            ScheduleNextPeriodicRefresh();
        }, null, interval, Timeout.InfiniteTimeSpan);
    }

    private async Task<bool> RefreshRulesFromApiAsync()
    {
        if (_apiClient == null || string.IsNullOrEmpty(_configuration.HouseId))
        {
            _logger.LogWarning("Cannot refresh battery rules - API client or HouseId not configured");
            return false;
        }

        try
        {
            _logger.LogDebug("Refreshing battery rules from API for house {HouseId}", _configuration.HouseId);
            BatteryZoneRules rules = await _apiClient.GetRulesAsync(_configuration.HouseId);

            _cachedRules = rules;
            _logger.LogDebug("Successfully refreshed {Count} battery rules from API", rules.Rules.Count);

            // Save to local storage
            await SaveToLocalStorageAsync(rules);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing battery rules from API for house {HouseId}", _configuration.HouseId);
            return false;
        }
    }

    private async Task LoadFromLocalStorageAsync()
    {
        try
        {
            if (!File.Exists(_localStoragePath))
            {
                _logger.LogDebug("No local battery rules storage found at {Path}", _localStoragePath);
                return;
            }

            _logger.LogDebug("Loading battery rules from local storage at {Path}", _localStoragePath);
            string json = await File.ReadAllTextAsync(_localStoragePath);
            BatteryZoneRulesDto? dto = JsonSerializer.Deserialize<BatteryZoneRulesDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (dto != null)
            {
                _cachedRules = BatteryRuleMapper.MapFromDto(dto);
                _logger.LogDebug("Successfully loaded {Count} battery rules from local storage", _cachedRules.Rules.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading battery rules from local storage at {Path}", _localStoragePath);
        }
    }

    private async Task SaveToLocalStorageAsync(BatteryZoneRules rules)
    {
        try
        {
            BatteryZoneRulesDto dto = BatteryRuleMapper.MapToDto(rules);
            string json = JsonSerializer.Serialize(dto, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(_localStoragePath, json);
            _logger.LogDebug("Saved battery rules to local storage at {Path}", _localStoragePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving battery rules to local storage at {Path}", _localStoragePath);
        }
    }
}

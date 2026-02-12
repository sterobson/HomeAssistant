using HomeAssistant.Shared.Energy;
using System.IO;
using System.Text.Json;
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
    private const int _cacheExpirationMinutes = 10;

    private BatteryZoneRules? _cachedRules;
    private DateTimeOffset _lastRefreshTime = DateTimeOffset.MinValue;

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
        // Load from local storage on startup
        await LoadFromLocalStorageAsync();

        // Register SignalR handler and start connection
        if (_signalRConnection != null)
        {
            _signalRConnection.On<object>("battery-rules-changed", async (data) =>
            {
                _logger.LogDebug("Received 'battery-rules-changed' notification from SignalR for house {HouseId}", _configuration.HouseId);
                _lastRefreshTime = DateTimeOffset.MinValue; // Invalidate cache
                await RefreshRulesFromApiAsync();

                if (RulesUpdated != null)
                {
                    await RulesUpdated.Invoke();
                }
            });

            await _signalRConnection.StartAsync();
        }
    }

    public async Task<BatteryZoneRules> GetRulesAsync()
    {
        // Check if cache is still valid
        TimeSpan timeSinceLastRefresh = DateTimeOffset.UtcNow - _lastRefreshTime;
        if (_cachedRules == null || timeSinceLastRefresh.TotalMinutes >= _cacheExpirationMinutes)
        {
            await RefreshRulesFromApiAsync();
        }

        return _cachedRules ?? new();
    }

    private async Task RefreshRulesFromApiAsync()
    {
        if (_apiClient == null || string.IsNullOrEmpty(_configuration.HouseId))
        {
            _logger.LogWarning("Cannot refresh battery rules - API client or HouseId not configured");
            return;
        }

        try
        {
            _logger.LogDebug("Refreshing battery rules from API for house {HouseId}", _configuration.HouseId);
            BatteryZoneRules rules = await _apiClient.GetRulesAsync(_configuration.HouseId);

            _cachedRules = rules;
            _lastRefreshTime = DateTimeOffset.UtcNow;
            _logger.LogDebug("Successfully refreshed {Count} battery rules from API", rules.Rules.Count);

            // Save to local storage
            await SaveToLocalStorageAsync(rules);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing battery rules from API for house {HouseId}", _configuration.HouseId);
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
                _lastRefreshTime = DateTimeOffset.UtcNow;
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

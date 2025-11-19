using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace HomeAssistant.Services;

public class HistoryService
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly HomeAssistantConfiguration _settings;
    private readonly ILogger<HistoryService> _logger;

    public HistoryService(HomeAssistantConfiguration settings, ILogger<HistoryService> logger)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri($"http://{settings.Host}:{settings.Port}")
        };

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyList<NumericHistoryEntry>> GetEntityNumericHistory(string entityId, DateTime from, DateTime to)
    {
        IReadOnlyList<HistoryTextEntry> states = await GetEntityTextHistory(entityId, from, to);
        try
        {
            return [.. states.Select(s => new NumericHistoryEntry
            {
                LastChanged = s.LastChanged,
                State = double.TryParse(s.State, out double value) ? value : 0
            })];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching history for entity {EntityId}", entityId);
            throw;
        }
    }

    public async Task<IReadOnlyList<HistoryTextEntry>> GetEntityTextHistory(string entityId, DateTime from, DateTime to)
    {
        // Normalize to UTC
        DateTime fromUtc = from.ToUniversalTime();
        DateTime toUtc = to.ToUniversalTime();

        // Format as ISO 8601 with Z suffix
        string fromStr = fromUtc.ToString("yyyy-MM-ddTHH:mm:ssZ");
        string toStr = toUtc.ToString("yyyy-MM-ddTHH:mm:ssZ");

        string url = $"http://{_settings.Host}:{_settings.Port}/api/history/period/{fromStr}?end_time={toStr}&filter_entity_id={entityId}";

        try
        {
            string response = await _httpClient.GetStringAsync(url);

            // Home Assistant returns a nested array: [[ {state1}, {state2}, ... ]]
            List<List<HistoryTextEntry>>? outer = JsonSerializer.Deserialize<List<List<HistoryTextEntry>>>(response, _jsonOptions);

            return outer?.FirstOrDefault() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching history for entity {EntityId} - {Url}", entityId, url);
            throw;
        }
    }
}

public class HistoryEntry<T>
{
    [JsonPropertyName("last_changed")]
    public DateTime LastChanged { get; set; }
    public required T State { get; set; }
}

public class HistoryTextEntry : HistoryEntry<string?>
{
}

public class NumericHistoryEntry : HistoryEntry<double>
{
}
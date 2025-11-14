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

    public HistoryService(HomeAssistantConfiguration settings)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri($"http://{settings.Host}:{settings.Port}")
        };
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", settings.Token);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task<IReadOnlyList<NumericHistoryEntry>> GetEntityHistory(string entityId, DateTime from, DateTime to)
    {
        // Normalize to UTC
        DateTime fromUtc = from.ToUniversalTime();
        DateTime toUtc = to.ToUniversalTime();

        // Format as ISO 8601 with Z suffix
        string fromStr = fromUtc.ToString("yyyy-MM-ddTHH:mm:ssZ");
        string toStr = toUtc.ToString("yyyy-MM-ddTHH:mm:ssZ");

        string url = $"/api/history/period/{fromStr}?end_time={toStr}&filter_entity_id={entityId}";
        string response = await _httpClient.GetStringAsync(url);

        // Home Assistant returns a nested array: [[ {state1}, {state2}, ... ]]
        List<List<HistoryEntry>>? outer = JsonSerializer.Deserialize<List<List<HistoryEntry>>>(response, _jsonOptions);

        List<HistoryEntry> states = outer?.FirstOrDefault() ?? [];

        return [.. states.Select(s => new NumericHistoryEntry
        {
            LastChanged = s.LastChanged,
            State = double.TryParse(s.State, out double value) ? value : 0
        })];
    }
}

public class HistoryEntry
{
    [JsonPropertyName("last_changed")]
    public DateTime LastChanged { get; set; }
    public string? State { get; set; }
}

public class NumericHistoryEntry : HistoryEntry
{
    public new double State { get; set; }
}
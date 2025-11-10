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

    public async Task<IReadOnlyList<HistoryEntry>> GetEntityHistory(string entityId, DateTime from)
    {
        string url = $"/api/history/period/{from:o}?filter_entity_id={entityId}";
        string response = await _httpClient.GetStringAsync(url);

        // Home Assistant returns a nested array: [[ {state1}, {state2}, ... ]]
        List<List<HistoryEntry>>? outer = JsonSerializer.Deserialize<List<List<HistoryEntry>>>(response, _jsonOptions);

        return outer?.FirstOrDefault() ?? [];
    }
}

public class HistoryEntry
{
    [JsonPropertyName("last_changed")]
    public DateTime LastChanged { get; set; }
    public string? State { get; set; }
}
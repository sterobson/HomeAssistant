using HomeAssistant.Services.Energy;
using HomeAssistant.Shared.Energy;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace HomeAssistant.Services;

public interface IBatteryRulesApiClient
{
    Task<BatteryZoneRules> GetRulesAsync(string houseId);
    Task SetRulesAsync(string houseId, BatteryZoneRules rules);
}

public class BatteryRulesApiClient : IBatteryRulesApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BatteryRulesApiClient> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public BatteryRulesApiClient(HttpClient httpClient, ILogger<BatteryRulesApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<BatteryZoneRules> GetRulesAsync(string houseId)
    {
        try
        {
            HttpResponseMessage response = await _httpClient.GetAsync($"/api/battery-rules?houseId={houseId}");
            response.EnsureSuccessStatusCode();

            BatteryZoneRulesDto? dto = await response.Content.ReadFromJsonAsync<BatteryZoneRulesDto>(_jsonOptions);
            if (dto == null)
            {
                _logger.LogWarning("Received null response when getting battery rules for house {HouseId}", houseId);
                return new();
            }

            return BatteryRuleMapper.MapFromDto(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting battery rules from API for house {HouseId}", houseId);
            throw;
        }
    }

    public async Task SetRulesAsync(string houseId, BatteryZoneRules rules)
    {
        try
        {
            BatteryZoneRulesDto dto = BatteryRuleMapper.MapToDto(rules);
            string json = JsonSerializer.Serialize(dto, _jsonOptions);
            StringContent content = new(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await _httpClient.PostAsync($"/api/battery-rules?houseId={houseId}", content);
            response.EnsureSuccessStatusCode();

            _logger.LogDebug("Successfully set battery rules for house {HouseId}", houseId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting battery rules to API for house {HouseId}", houseId);
            throw;
        }
    }

}

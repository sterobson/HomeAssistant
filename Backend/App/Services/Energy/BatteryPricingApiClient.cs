using HomeAssistant.Services.Energy;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace HomeAssistant.Services;

public interface IBatteryPricingApiClient
{
    Task PostPricingAsync(string houseId, string date, List<PricingSlot> slots);
}

public class BatteryPricingApiClient : IBatteryPricingApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BatteryPricingApiClient> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public BatteryPricingApiClient(HttpClient httpClient, ILogger<BatteryPricingApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task PostPricingAsync(string houseId, string date, List<PricingSlot> slots)
    {
        try
        {
            string json = JsonSerializer.Serialize(slots, _jsonOptions);
            StringContent content = new(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await _httpClient.PostAsync(
                $"/api/battery-pricing?houseId={houseId}&date={date}", content);
            response.EnsureSuccessStatusCode();

            _logger.LogDebug("Successfully posted {Count} pricing slots for house {HouseId} on {Date}",
                slots.Count, houseId, date);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error posting battery pricing to API for house {HouseId} on {Date}", houseId, date);
            throw;
        }
    }
}

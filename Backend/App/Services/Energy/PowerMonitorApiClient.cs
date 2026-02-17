using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace HomeAssistant.Services.Energy;

public interface IPowerMonitorApiClient
{
    Task PostSnapshotAsync(string houseId, double solarW, double batteryW, double houseW, double gridW);
}

public class PowerMonitorApiClient : IPowerMonitorApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PowerMonitorApiClient> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public PowerMonitorApiClient(HttpClient httpClient, ILogger<PowerMonitorApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task PostSnapshotAsync(string houseId, double solarW, double batteryW, double houseW, double gridW)
    {
        try
        {
            object body = new { solarW, batteryW, houseW, gridW };
            string json = JsonSerializer.Serialize(body, _jsonOptions);
            StringContent content = new(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await _httpClient.PostAsync(
                $"/api/power-monitor/snapshot?houseId={houseId}", content);
            response.EnsureSuccessStatusCode();

            _logger.LogDebug("Successfully posted power snapshot for house {HouseId}", houseId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error posting power snapshot to API for house {HouseId}", houseId);
            throw;
        }
    }
}

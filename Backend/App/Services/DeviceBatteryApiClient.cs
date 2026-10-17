using HomeAssistant.Shared;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace HomeAssistant.Services;

public interface IDeviceBatteryApiClient
{
    Task PostSnapshotAsync(string houseId, DeviceBatterySnapshotDto snapshot);
}

public class DeviceBatteryApiClient : IDeviceBatteryApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DeviceBatteryApiClient> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public DeviceBatteryApiClient(HttpClient httpClient, ILogger<DeviceBatteryApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task PostSnapshotAsync(string houseId, DeviceBatterySnapshotDto snapshot)
    {
        try
        {
            string json = JsonSerializer.Serialize(snapshot, _jsonOptions);
            StringContent content = new(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await _httpClient.PostAsync($"/api/device-batteries?houseId={houseId}", content);
            response.EnsureSuccessStatusCode();

            _logger.LogDebug("Pushed {Count} device battery readings for house {HouseId}",
                snapshot.Readings.Count, houseId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pushing device battery readings for house {HouseId}", houseId);
            throw;
        }
    }
}

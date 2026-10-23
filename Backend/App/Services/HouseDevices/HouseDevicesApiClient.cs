using HomeAssistant.Shared;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace HomeAssistant.Services.HouseDevices;

public interface IHouseDevicesApiClient
{
    Task PostSnapshotAsync(string houseId, HouseDevicesDto snapshot);
    Task PostStatesAsync(string houseId, HouseDeviceStatesDto states);
    Task PostCommandResultAsync(string houseId, HouseDeviceCommandResultDto result);
}

/// <summary>Sends the 3D house view's data to the Functions app, which stores the snapshot and relays the rest.</summary>
public class HouseDevicesApiClient : IHouseDevicesApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HouseDevicesApiClient> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public HouseDevicesApiClient(HttpClient httpClient, ILogger<HouseDevicesApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public Task PostSnapshotAsync(string houseId, HouseDevicesDto snapshot) =>
        PostAsync($"/api/house-devices?houseId={Uri.EscapeDataString(houseId)}", snapshot);

    public Task PostStatesAsync(string houseId, HouseDeviceStatesDto states) =>
        PostAsync($"/api/house-devices/states?houseId={Uri.EscapeDataString(houseId)}", states);

    public Task PostCommandResultAsync(string houseId, HouseDeviceCommandResultDto result) =>
        PostAsync($"/api/house-devices/command-result?houseId={Uri.EscapeDataString(houseId)}", result);

    private async Task PostAsync<T>(string path, T body)
    {
        string json = JsonSerializer.Serialize(body, _jsonOptions);
        StringContent content = new(json, Encoding.UTF8, "application/json");
        HttpResponseMessage response = await _httpClient.PostAsync(path, content);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("POST {Path} failed with {Status}", path.Split('?')[0], (int)response.StatusCode);
        }

        response.EnsureSuccessStatusCode();
    }
}

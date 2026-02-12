using HomeAssistant.Shared;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace HomeAssistant.Services;

public interface IDeviceSettingsApiClient
{
    Task<DeviceSettingsDto> GetSettingsAsync(string houseId);
}

public class DeviceSettingsApiClient : IDeviceSettingsApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DeviceSettingsApiClient> _logger;

    private static readonly System.Text.Json.JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public DeviceSettingsApiClient(HttpClient httpClient, ILogger<DeviceSettingsApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<DeviceSettingsDto> GetSettingsAsync(string houseId)
    {
        try
        {
            HttpResponseMessage response = await _httpClient.GetAsync($"/api/device-settings?houseId={houseId}");
            response.EnsureSuccessStatusCode();

            DeviceSettingsDto? dto = await response.Content.ReadFromJsonAsync<DeviceSettingsDto>(_jsonOptions);
            if (dto == null)
            {
                _logger.LogWarning("Received null response when getting device settings for house {HouseId}", houseId);
                return new();
            }

            return dto;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting device settings from API for house {HouseId}", houseId);
            throw;
        }
    }
}

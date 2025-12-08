using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace HomeAssistant.Services.WasteManagement;

internal class YorkWasteCollectionService : IWasteCollectionService
{
    private readonly HttpClient _httpClient;
    private readonly YorkBinServiceConfiguration _configuration;
    private readonly ILogger<YorkWasteCollectionService> _logger;
    private readonly JsonSerializerOptions _jsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };

    public YorkWasteCollectionService(HttpClient httpClient, YorkBinServiceConfiguration configuration,
        ILogger<YorkWasteCollectionService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<List<BinServiceDto>> GetBinCollectionsAsync(string uprn)
    {
        try
        {
            string url = $"{_configuration.CollectionDataEndpoint}/{uprn}";

            using HttpResponseMessage response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();

            ResponseObject? responseObject = JsonSerializer.Deserialize<ResponseObject>(json, _jsonSerializerOptions);

            if (responseObject?.Services?.Count > 0)
            {
                _logger.LogDebug("Fetched {Count} bin collection services for {Uprn}", responseObject?.Services?.Count ?? 0, uprn);
            }
            else
            {
                _logger.LogWarning("Fetched {Count} bin collection services for {Uprn}", responseObject?.Services?.Count ?? 0, uprn);
            }

            return responseObject?.Services ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching bin collection data");
            return [];
        }
    }

    private class ResponseObject
    {
        public List<BinServiceDto> Services { get; set; } = [];
    }
}
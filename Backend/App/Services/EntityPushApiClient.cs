using HomeAssistant.Shared;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace HomeAssistant.Services;

public interface IEntityPushApiClient
{
    Task PostEntitiesAsync(string houseId, EntityListDto entities);
}

public class EntityPushApiClient : IEntityPushApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<EntityPushApiClient> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public EntityPushApiClient(HttpClient httpClient, ILogger<EntityPushApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task PostEntitiesAsync(string houseId, EntityListDto entities)
    {
        try
        {
            string json = JsonSerializer.Serialize(entities, _jsonOptions);
            StringContent content = new(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await _httpClient.PostAsync($"/api/entities?houseId={houseId}", content);
            response.EnsureSuccessStatusCode();

            _logger.LogDebug("Successfully pushed {Count} entities for house {HouseId}", entities.Entities.Count, houseId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pushing entities to API for house {HouseId}", houseId);
            throw;
        }
    }
}

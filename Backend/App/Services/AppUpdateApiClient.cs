using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace HomeAssistant.Services;

public interface IAppUpdateApiClient
{
    Task<string?> GetDesiredVersionAsync(string houseId);
    Task<string?> GetDownloadUrlAsync(string version);
    Task ReportRunningVersionAsync(string houseId, string version);
    Task AcknowledgeUpdateAsync(string houseId);
}

public class AppUpdateApiClient : IAppUpdateApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AppUpdateApiClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AppUpdateApiClient(HttpClient httpClient, ILogger<AppUpdateApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<string?> GetDesiredVersionAsync(string houseId)
    {
        try
        {
            HttpResponseMessage response = await _httpClient.GetAsync($"/api/app-release/desired?houseId={houseId}");
            response.EnsureSuccessStatusCode();

            JsonDocument doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("version", out JsonElement versionElement) &&
                versionElement.ValueKind != JsonValueKind.Null)
            {
                return versionElement.GetString();
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting desired version for house {HouseId}", houseId);
            throw;
        }
    }

    public async Task<string?> GetDownloadUrlAsync(string version)
    {
        try
        {
            HttpResponseMessage response = await _httpClient.GetAsync($"/api/app-release/download?version={version}");
            response.EnsureSuccessStatusCode();

            JsonDocument doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("url", out JsonElement urlElement))
            {
                return urlElement.GetString();
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting download URL for version {Version}", version);
            throw;
        }
    }

    public async Task ReportRunningVersionAsync(string houseId, string version)
    {
        try
        {
            HttpResponseMessage response = await _httpClient.PostAsync(
                $"/api/app-release/running?houseId={houseId}&version={version}", null);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reporting running version {Version} for house {HouseId}", version, houseId);
            throw;
        }
    }

    public async Task AcknowledgeUpdateAsync(string houseId)
    {
        try
        {
            HttpResponseMessage response = await _httpClient.PostAsync(
                $"/api/app-release/acknowledge?houseId={houseId}", null);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error acknowledging update for house {HouseId}", houseId);
            throw;
        }
    }
}

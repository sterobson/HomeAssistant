using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using HomeAssistant.Functions.Models;
using System.Text;
using System.Text.Json;

namespace HomeAssistant.Functions.Services;

public class BatteryRulesStorageService
{
    private readonly BlobContainerClient _containerClient;
    private const string ContainerName = "battery-rules";
    private readonly JsonSerializerOptions _deserialiserOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly JsonSerializerOptions _serialiserOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public BatteryRulesStorageService(string connectionString)
    {
        BlobServiceClient blobServiceClient = new(connectionString);
        _containerClient = blobServiceClient.GetBlobContainerClient(ContainerName);
    }

    public async Task<BatteryZoneRulesDto?> GetRulesAsync(string houseId)
    {
        BlobClient blobClient = _containerClient.GetBlobClient($"{houseId}.json");

        if (!await blobClient.ExistsAsync())
        {
            return null;
        }

        Response<BlobDownloadResult> response = await blobClient.DownloadContentAsync();
        string json = response.Value.Content.ToString();

        return JsonSerializer.Deserialize<BatteryZoneRulesDto>(json, _deserialiserOptions);
    }

    public async Task SaveRulesAsync(string houseId, BatteryZoneRulesDto rules)
    {
        await _containerClient.CreateIfNotExistsAsync(PublicAccessType.None);

        BlobClient blobClient = _containerClient.GetBlobClient($"{houseId}.json");

        string json = JsonSerializer.Serialize(rules, _serialiserOptions);

        BinaryData content = new(Encoding.UTF8.GetBytes(json));

        await blobClient.UploadAsync(content, overwrite: true);
    }
}

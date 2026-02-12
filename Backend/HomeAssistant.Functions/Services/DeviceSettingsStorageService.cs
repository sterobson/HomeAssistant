using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using HomeAssistant.Functions.Models;
using System.Text;
using System.Text.Json;

namespace HomeAssistant.Functions.Services;

public class DeviceSettingsStorageService
{
    private readonly BlobContainerClient _containerClient;
    private const string ContainerName = "device-settings";
    private readonly JsonSerializerOptions _deserialiserOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly JsonSerializerOptions _serialiserOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public DeviceSettingsStorageService(string connectionString)
    {
        BlobServiceClient blobServiceClient = new(connectionString);
        _containerClient = blobServiceClient.GetBlobContainerClient(ContainerName);
    }

    public async Task<DeviceSettingsDto?> GetSettingsAsync(string houseId)
    {
        BlobClient blobClient = _containerClient.GetBlobClient($"{houseId}.json");

        if (!await blobClient.ExistsAsync())
        {
            return null;
        }

        Response<BlobDownloadResult> response = await blobClient.DownloadContentAsync();
        string json = response.Value.Content.ToString();

        return JsonSerializer.Deserialize<DeviceSettingsDto>(json, _deserialiserOptions);
    }

    public async Task SaveSettingsAsync(string houseId, DeviceSettingsDto settings)
    {
        await _containerClient.CreateIfNotExistsAsync(PublicAccessType.None);

        BlobClient blobClient = _containerClient.GetBlobClient($"{houseId}.json");

        string json = JsonSerializer.Serialize(settings, _serialiserOptions);

        BinaryData content = new(Encoding.UTF8.GetBytes(json));

        await blobClient.UploadAsync(content, overwrite: true);
    }
}

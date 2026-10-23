using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using HomeAssistant.Shared;
using System.Text;
using System.Text.Json;

namespace HomeAssistant.Functions.Services;

/// <summary>The newest device snapshot for the 3D house view, one blob per house, replaced on every upload.</summary>
public class HouseDevicesStorageService
{
    private readonly BlobContainerClient _containerClient;
    private const string ContainerName = "house-devices";

    public HouseDevicesStorageService(string connectionString)
    {
        BlobServiceClient blobServiceClient = new(connectionString);
        _containerClient = blobServiceClient.GetBlobContainerClient(ContainerName);
    }

    public async Task<HouseDevicesDto?> GetAsync(string houseId)
    {
        BlobClient blobClient = _containerClient.GetBlobClient($"{houseId}.json");
        if (!await blobClient.ExistsAsync())
        {
            return null;
        }

        Response<BlobDownloadResult> response = await blobClient.DownloadContentAsync();
        return JsonSerializer.Deserialize<HouseDevicesDto>(response.Value.Content.ToString(), JsonConfiguration.CreateOptions());
    }

    public async Task SaveAsync(string houseId, HouseDevicesDto snapshot)
    {
        await _containerClient.CreateIfNotExistsAsync(PublicAccessType.None);
        BlobClient blobClient = _containerClient.GetBlobClient($"{houseId}.json");
        string json = JsonSerializer.Serialize(snapshot, JsonConfiguration.CreateOptions());
        await blobClient.UploadAsync(new BinaryData(Encoding.UTF8.GetBytes(json)), overwrite: true);
    }
}

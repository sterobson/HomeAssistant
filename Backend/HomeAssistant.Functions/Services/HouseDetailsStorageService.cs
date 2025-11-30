using Azure.Storage.Blobs;
using HomeAssistant.Shared.Climate;
using System.Text.Json;

namespace HomeAssistant.Functions.Services;

public class HouseDetailsStorageService
{
    private readonly BlobContainerClient _containerClient;
    private const string ContainerName = "house-details";

    public HouseDetailsStorageService(string connectionString)
    {
        BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString);
        _containerClient = blobServiceClient.GetBlobContainerClient(ContainerName);
        _containerClient.CreateIfNotExists();
    }

    public async Task<HouseDetailsDto?> GetHouseDetailsAsync(string houseId)
    {
        var blobName = $"{houseId}/details.json";
        var blobClient = _containerClient.GetBlobClient(blobName);

        if (!await blobClient.ExistsAsync())
        {
            return null;
        }

        var response = await blobClient.DownloadContentAsync();
        var json = response.Value.Content.ToString();
        return JsonSerializer.Deserialize<HouseDetailsDto>(json);
    }

    public async Task SaveHouseDetailsAsync(string houseId, HouseDetailsDto houseDetails)
    {
        var blobName = $"{houseId}/details.json";
        var blobClient = _containerClient.GetBlobClient(blobName);

        var json = JsonSerializer.Serialize(houseDetails, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await blobClient.UploadAsync(
            BinaryData.FromString(json),
            overwrite: true);
    }
}

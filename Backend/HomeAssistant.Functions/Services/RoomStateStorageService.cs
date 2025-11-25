using Azure.Storage.Blobs;
using HomeAssistant.Functions.Models;
using System.Text.Json;

namespace HomeAssistant.Functions.Services;

public class RoomStateStorageService
{
    private readonly BlobContainerClient _containerClient;
    private const string ContainerName = "room-states";

    public RoomStateStorageService(string connectionString)
    {
        var blobServiceClient = new BlobServiceClient(connectionString);
        _containerClient = blobServiceClient.GetBlobContainerClient(ContainerName);
        _containerClient.CreateIfNotExists();
    }

    public async Task<RoomStatesResponse?> GetRoomStatesAsync(Guid houseId)
    {
        var blobName = $"{houseId}/state.json";
        var blobClient = _containerClient.GetBlobClient(blobName);

        if (!await blobClient.ExistsAsync())
        {
            return null;
        }

        var response = await blobClient.DownloadContentAsync();
        var json = response.Value.Content.ToString();
        return JsonSerializer.Deserialize<RoomStatesResponse>(json);
    }

    public async Task SaveRoomStatesAsync(Guid houseId, RoomStatesResponse roomStates)
    {
        var blobName = $"{houseId}/state.json";
        var blobClient = _containerClient.GetBlobClient(blobName);

        var json = JsonSerializer.Serialize(roomStates, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await blobClient.UploadAsync(
            BinaryData.FromString(json),
            overwrite: true);
    }
}

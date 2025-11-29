using Azure.Storage.Blobs;
using HomeAssistant.Shared.Climate;
using System.Text.Json;

namespace HomeAssistant.Functions.Services;

public class RoomStateStorageService
{
    private readonly BlobContainerClient _containerClient;
    private const string ContainerName = "room-states";

    public RoomStateStorageService(string connectionString)
    {
        BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString);
        _containerClient = blobServiceClient.GetBlobContainerClient(ContainerName);
        _containerClient.CreateIfNotExists();
    }

    public async Task<RoomStatesResponse?> GetRoomStatesAsync(string houseId)
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

    public async Task SaveRoomStatesAsync(string houseId, RoomStatesResponse roomStates)
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

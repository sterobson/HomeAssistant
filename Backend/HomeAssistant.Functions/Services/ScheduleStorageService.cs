using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using HomeAssistant.Shared.Climate;
using System.Text;
using System.Text.Json;

namespace HomeAssistant.Functions.Services;

public class ScheduleStorageService
{
    private readonly BlobContainerClient _containerClient;
    private const string ContainerName = "schedules";
    private readonly JsonSerializerOptions _deserialiserOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly JsonSerializerOptions _serialiserOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public ScheduleStorageService(string connectionString)
    {
        BlobServiceClient blobServiceClient = new(connectionString);
        _containerClient = blobServiceClient.GetBlobContainerClient(ContainerName);
    }

    public async Task<RoomSchedulesDto?> GetSchedulesAsync(string houseId)
    {
        BlobClient blobClient = _containerClient.GetBlobClient($"{houseId}.json");

        if (!await blobClient.ExistsAsync())
        {
            return null;
        }

        Response<BlobDownloadResult> response = await blobClient.DownloadContentAsync();
        string json = response.Value.Content.ToString();

        return JsonSerializer.Deserialize<RoomSchedulesDto>(json, _deserialiserOptions);
    }

    public async Task SaveSchedulesAsync(string houseId, RoomSchedulesDto schedules)
    {
        // Ensure container exists
        await _containerClient.CreateIfNotExistsAsync(PublicAccessType.None);

        BlobClient blobClient = _containerClient.GetBlobClient($"{houseId}.json");

        string json = JsonSerializer.Serialize(schedules, _serialiserOptions);

        BinaryData content = new(Encoding.UTF8.GetBytes(json));

        await blobClient.UploadAsync(content, overwrite: true);
    }
}

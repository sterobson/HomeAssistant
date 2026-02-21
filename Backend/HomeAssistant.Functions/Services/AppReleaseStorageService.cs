using Azure;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using System.IO;
using System.Text;
using System.Text.Json;

namespace HomeAssistant.Functions.Services;

public class AppReleaseStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly BlobContainerClient _containerClient;
    private readonly TableClient _tableClient;
    private const string ContainerName = "app-releases";
    private const string TableName = "AppReleaseState";

    public AppReleaseStorageService(string connectionString)
    {
        _blobServiceClient = new BlobServiceClient(connectionString);
        _containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);
        _tableClient = new TableClient(connectionString, TableName);
    }

    public async Task UploadReleaseAsync(string version, Stream zipStream)
    {
        await _containerClient.CreateIfNotExistsAsync(PublicAccessType.None);

        BlobClient blobClient = _containerClient.GetBlobClient($"{version}.zip");
        await blobClient.UploadAsync(zipStream, overwrite: true);
    }

    public async Task<string?> GetLatestVersionAsync()
    {
        return await GetTableValueAsync("global", "latest");
    }

    public async Task SetLatestVersionAsync(string version)
    {
        await SetTableValueAsync("global", "latest", version);
    }

    public async Task<string?> GetDesiredVersionAsync(string houseId)
    {
        return await GetTableValueAsync("house", houseId);
    }

    public async Task SetDesiredVersionAsync(string houseId, string version)
    {
        await SetTableValueAsync("house", houseId, version);
    }

    public async Task ClearDesiredVersionAsync(string houseId)
    {
        await _tableClient.CreateIfNotExistsAsync();

        try
        {
            await _tableClient.DeleteEntityAsync("house", houseId);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Already cleared
        }
    }

    public async Task<(string? Version, DateTimeOffset? ReportedAt)> GetRunningVersionAsync(string houseId)
    {
        await _tableClient.CreateIfNotExistsAsync();

        try
        {
            Response<TableEntity> response = await _tableClient.GetEntityAsync<TableEntity>("running", houseId);
            TableEntity entity = response.Value;
            string? version = entity.GetString("Value");
            DateTimeOffset? reportedAt = entity.GetDateTimeOffset("ReportedAt");
            return (version, reportedAt);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return (null, null);
        }
    }

    public async Task SetRunningVersionAsync(string houseId, string version)
    {
        await _tableClient.CreateIfNotExistsAsync();

        TableEntity entity = new("running", houseId)
        {
            { "Value", version },
            { "ReportedAt", DateTimeOffset.UtcNow }
        };

        await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
    }

    public string GenerateDownloadSasUrl(string version)
    {
        BlobClient blobClient = _containerClient.GetBlobClient($"{version}.zip");

        BlobSasBuilder sasBuilder = new()
        {
            BlobContainerName = ContainerName,
            BlobName = $"{version}.zip",
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(5)
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        Uri sasUri = blobClient.GenerateSasUri(sasBuilder);
        return sasUri.ToString();
    }

    private async Task<string?> GetTableValueAsync(string partitionKey, string rowKey)
    {
        await _tableClient.CreateIfNotExistsAsync();

        try
        {
            Response<TableEntity> response = await _tableClient.GetEntityAsync<TableEntity>(partitionKey, rowKey);
            return response.Value.GetString("Value");
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private async Task SetTableValueAsync(string partitionKey, string rowKey, string value)
    {
        await _tableClient.CreateIfNotExistsAsync();

        TableEntity entity = new(partitionKey, rowKey)
        {
            { "Value", value }
        };

        await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
    }
}

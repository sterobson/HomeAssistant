using Azure.Data.Tables;
using HomeAssistant.Functions.Models;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Functions.Services;

/// <summary>
/// Service for storing and retrieving temperature history data in Azure Table Storage
/// </summary>
public class RoomHistoryStorageService
{
    private readonly TableClient _tableClient;
    private readonly ILogger _logger;
    private const string TableName = "roomhistory";

    public RoomHistoryStorageService(string connectionString, ILogger logger)
    {
        _logger = logger;
        TableServiceClient serviceClient = new TableServiceClient(connectionString);
        _tableClient = serviceClient.GetTableClient(TableName);

        // Create table if it doesn't exist
        _tableClient.CreateIfNotExists();
    }

    /// <summary>
    /// Saves a temperature history point to storage
    /// </summary>
    public async Task SaveHistoryPointAsync(
        string houseId,
        int roomId,
        double? currentTemperature,
        double? targetTemperature,
        bool heatingActive,
        DateTimeOffset recordedAt)
    {
        try
        {
            TemperatureHistoryPoint point = new TemperatureHistoryPoint
            {
                PartitionKey = $"{houseId}_{roomId}",
                RowKey = GenerateRowKey(recordedAt),
                HouseId = houseId,
                RoomId = roomId,
                CurrentTemperature = currentTemperature,
                TargetTemperature = targetTemperature,
                HeatingActive = heatingActive,
                RecordedAt = recordedAt
            };

            await _tableClient.AddEntityAsync(point);
            _logger.LogDebug("Saved temperature history point for room {RoomId} at {RecordedAt}", roomId, recordedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save temperature history point for room {RoomId}", roomId);
            throw;
        }
    }

    /// <summary>
    /// Retrieves temperature history for a room within the specified time range
    /// </summary>
    public async Task<List<TemperatureHistoryPoint>> GetHistoryAsync(
        string houseId,
        int roomId,
        DateTimeOffset startDate,
        DateTimeOffset endDate)
    {
        try
        {
            string partitionKey = $"{houseId}_{roomId}";

            // Convert dates to row keys (inverted ticks)
            // Start date has larger inverted ticks (earlier time)
            // End date has smaller inverted ticks (later time)
            string startRowKey = GenerateRowKey(endDate);   // Smaller value (newer)
            string endRowKey = GenerateRowKey(startDate);     // Larger value (older)

            // Query for all records in the partition where RowKey is between start and end
            // (inverted ticks: smaller RowKey = newer, larger RowKey = older)
            List<TemperatureHistoryPoint> results = [];

            await foreach (TemperatureHistoryPoint point in _tableClient.QueryAsync<TemperatureHistoryPoint>(
                filter: $"PartitionKey eq '{partitionKey}' and RowKey ge '{startRowKey}' and RowKey le '{endRowKey}'"))
            {
                results.Add(point);
            }

            _logger.LogDebug("Retrieved {Count} history points for room {RoomId} between {StartDate} and {EndDate}",
                results.Count, roomId, startDate, endDate);
            return results.OrderBy(p => p.RecordedAt).ToList(); // Order chronologically for display
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve temperature history for room {RoomId}", roomId);
            throw;
        }
    }

    /// <summary>
    /// Deletes history data older than the specified retention period
    /// </summary>
    public async Task CleanupOldHistoryAsync(string houseId, int roomId, int retentionDays = 7)
    {
        try
        {
            string partitionKey = $"{houseId}_{roomId}";
            DateTimeOffset cutoffTime = DateTimeOffset.UtcNow.AddDays(-retentionDays);
            string cutoffRowKey = GenerateRowKey(cutoffTime);

            // Query for all records older than retention period
            // (RowKey > cutoffRowKey means older records due to inverted ticks)
            List<TemperatureHistoryPoint> oldRecords = [];

            await foreach (TemperatureHistoryPoint point in _tableClient.QueryAsync<TemperatureHistoryPoint>(
                filter: $"PartitionKey eq '{partitionKey}' and RowKey gt '{cutoffRowKey}'"))
            {
                oldRecords.Add(point);
            }

            // Delete in batches
            foreach (TemperatureHistoryPoint record in oldRecords)
            {
                await _tableClient.DeleteEntityAsync(record.PartitionKey, record.RowKey);
            }

            _logger.LogInformation("Cleaned up {Count} old history records for room {RoomId}", oldRecords.Count, roomId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup old history for room {RoomId}", roomId);
            throw;
        }
    }

    /// <summary>
    /// Generates a row key using inverted ticks for reverse chronological ordering
    /// </summary>
    private static string GenerateRowKey(DateTimeOffset timestamp)
    {
        // Invert ticks so newer records come first when querying
        long invertedTicks = long.MaxValue - timestamp.UtcTicks;
        return invertedTicks.ToString("D19"); // Pad to 19 digits for proper string sorting
    }
}

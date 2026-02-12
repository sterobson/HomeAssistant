using Azure.Data.Tables;
using HomeAssistant.Functions.Models;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Functions.Services;

public class BatteryHistoryStorageService
{
    private readonly TableClient _tableClient;
    private readonly ILogger _logger;
    private const string TableName = "batteryhistory";

    public BatteryHistoryStorageService(string connectionString, ILogger logger)
    {
        _logger = logger;
        TableServiceClient serviceClient = new TableServiceClient(connectionString);
        _tableClient = serviceClient.GetTableClient(TableName);

        _tableClient.CreateIfNotExists();
    }

    public async Task SaveHistoryPointAsync(
        string houseId,
        string date,
        double batteryPercent,
        double? powerWatts,
        DateTimeOffset recordedAt)
    {
        try
        {
            BatteryHistoryPoint point = new BatteryHistoryPoint
            {
                PartitionKey = $"{houseId}_{date}",
                RowKey = GenerateRowKey(recordedAt),
                HouseId = houseId,
                Date = date,
                BatteryPercent = batteryPercent,
                PowerWatts = powerWatts,
                RecordedAt = recordedAt
            };

            await _tableClient.AddEntityAsync(point);
            _logger.LogDebug("Saved battery history point for house {HouseId} at {RecordedAt}", houseId, recordedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save battery history point for house {HouseId}", houseId);
            throw;
        }
    }

    public async Task<List<BatteryHistoryPoint>> GetHistoryAsync(string houseId, string date)
    {
        try
        {
            string partitionKey = $"{houseId}_{date}";
            List<BatteryHistoryPoint> results = [];

            await foreach (BatteryHistoryPoint point in _tableClient.QueryAsync<BatteryHistoryPoint>(
                filter: $"PartitionKey eq '{partitionKey}'"))
            {
                results.Add(point);
            }

            _logger.LogDebug("Retrieved {Count} battery history points for house {HouseId} on {Date}",
                results.Count, houseId, date);
            return results.OrderBy(p => p.RecordedAt).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve battery history for house {HouseId} on {Date}", houseId, date);
            throw;
        }
    }

    public async Task CleanupOldHistoryAsync(string houseId, int retentionDays = 30)
    {
        try
        {
            DateTimeOffset cutoffDate = DateTimeOffset.UtcNow.AddDays(-retentionDays);
            int deletedCount = 0;

            // Query all partitions for this house that are older than the cutoff
            // Partition keys are formatted as {houseId}_{date}
            await foreach (BatteryHistoryPoint point in _tableClient.QueryAsync<BatteryHistoryPoint>(
                filter: $"HouseId eq '{houseId}' and RecordedAt lt datetime'{cutoffDate:O}'"))
            {
                await _tableClient.DeleteEntityAsync(point.PartitionKey, point.RowKey);
                deletedCount++;
            }

            _logger.LogInformation("Cleaned up {Count} old battery history records for house {HouseId}", deletedCount, houseId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup old battery history for house {HouseId}", houseId);
            throw;
        }
    }

    public async Task DeletePointsAsync(List<BatteryHistoryPoint> points)
    {
        try
        {
            int deletedCount = 0;
            foreach (BatteryHistoryPoint point in points)
            {
                await _tableClient.DeleteEntityAsync(point.PartitionKey, point.RowKey);
                deletedCount++;
            }

            _logger.LogDebug("Deleted {Count} battery history points", deletedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete battery history points");
            throw;
        }
    }

    /// <summary>
    /// Removes collinear points that lie on (or near) a straight line between their neighbors.
    /// Keeps the first and last points always. For each intermediate point, interpolates
    /// between the last kept point and the next point — if the actual value is within
    /// the tolerance, it is discarded.
    /// </summary>
    public static List<BatteryHistoryPoint> SimplifyHistory(List<BatteryHistoryPoint> points, double tolerancePercent = 1.0)
    {
        if (points.Count <= 2)
        {
            return new List<BatteryHistoryPoint>(points);
        }

        List<BatteryHistoryPoint> simplified = [points[0]];

        for (int i = 1; i < points.Count - 1; i++)
        {
            BatteryHistoryPoint lastKept = simplified[^1];
            BatteryHistoryPoint current = points[i];
            BatteryHistoryPoint next = points[i + 1];

            double totalTime = (next.RecordedAt - lastKept.RecordedAt).TotalSeconds;
            double currentTime = (current.RecordedAt - lastKept.RecordedAt).TotalSeconds;

            if (totalTime <= 0)
            {
                // Degenerate case: keep the point
                simplified.Add(current);
                continue;
            }

            double ratio = currentTime / totalTime;
            double interpolated = lastKept.BatteryPercent + (next.BatteryPercent - lastKept.BatteryPercent) * ratio;

            if (Math.Abs(current.BatteryPercent - interpolated) > tolerancePercent)
            {
                simplified.Add(current);
            }
        }

        simplified.Add(points[^1]);
        return simplified;
    }

    public async Task ReplaceHistoryAsync(string houseId, string date, List<BatteryHistoryPoint> newPoints)
    {
        try
        {
            string partitionKey = $"{houseId}_{date}";

            // 1. Load all existing points for this partition
            List<BatteryHistoryPoint> existingPoints = [];
            await foreach (BatteryHistoryPoint point in _tableClient.QueryAsync<BatteryHistoryPoint>(
                filter: $"PartitionKey eq '{partitionKey}'"))
            {
                existingPoints.Add(point);
            }

            // 2. Delete all existing points
            if (existingPoints.Count > 0)
            {
                _logger.LogInformation("Deleting {Count} existing battery history points for {PartitionKey}",
                    existingPoints.Count, partitionKey);
                await DeletePointsAsync(existingPoints);
            }

            // 3. Save all new points
            foreach (BatteryHistoryPoint point in newPoints)
            {
                point.PartitionKey = partitionKey;
                point.RowKey = GenerateRowKey(point.RecordedAt);
                point.HouseId = houseId;
                point.Date = date;
                await _tableClient.AddEntityAsync(point);
            }

            _logger.LogInformation("Replaced battery history for {PartitionKey}: {OldCount} → {NewCount} points",
                partitionKey, existingPoints.Count, newPoints.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to replace battery history for house {HouseId} on {Date}", houseId, date);
            throw;
        }
    }

    private static string GenerateRowKey(DateTimeOffset timestamp)
    {
        long invertedTicks = long.MaxValue - timestamp.UtcTicks;
        return invertedTicks.ToString("D19");
    }
}

using Azure.Data.Tables;
using HomeAssistant.Functions.Models;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Functions.Services;

public class EnergyHistoryStorageService
{
    private readonly TableClient _tableClient;
    private readonly ILogger _logger;
    private const string TableName = "energyhistory";

    public EnergyHistoryStorageService(string connectionString, ILogger logger)
    {
        _logger = logger;
        TableServiceClient serviceClient = new TableServiceClient(connectionString);
        _tableClient = serviceClient.GetTableClient(TableName);
        _tableClient.CreateIfNotExists();
    }

    public async Task SaveHourAsync(string houseId, string date, EnergyHistoryPoint point)
    {
        try
        {
            point.PartitionKey = $"{houseId}_{date}";
            point.RowKey = point.Hour.ToString("D2");
            point.HouseId = houseId;
            point.Date = date;

            await _tableClient.UpsertEntityAsync(point);
            _logger.LogDebug("Saved energy history hour {Hour} for house {HouseId} on {Date}", point.Hour, houseId, date);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save energy history hour {Hour} for house {HouseId} on {Date}", point.Hour, houseId, date);
            throw;
        }
    }

    public async Task<List<EnergyHistoryPoint>> GetHistoryAsync(string houseId, string date)
    {
        try
        {
            string partitionKey = $"{houseId}_{date}";
            List<EnergyHistoryPoint> results = [];

            await foreach (EnergyHistoryPoint point in _tableClient.QueryAsync<EnergyHistoryPoint>(
                filter: $"PartitionKey eq '{partitionKey}'"))
            {
                results.Add(point);
            }

            _logger.LogDebug("Retrieved {Count} energy history points for house {HouseId} on {Date}",
                results.Count, houseId, date);
            return results.OrderBy(p => p.Hour).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve energy history for house {HouseId} on {Date}", houseId, date);
            throw;
        }
    }

    public async Task<List<EnergyHistoryPoint>> GetHistoryRangeAsync(string houseId, string fromDate, string toDate)
    {
        try
        {
            string fromKey = $"{houseId}_{fromDate}";
            string toKey = $"{houseId}_{toDate}";
            List<EnergyHistoryPoint> results = [];

            await foreach (EnergyHistoryPoint point in _tableClient.QueryAsync<EnergyHistoryPoint>(
                filter: $"PartitionKey ge '{fromKey}' and PartitionKey le '{toKey}'"))
            {
                results.Add(point);
            }

            _logger.LogDebug("Retrieved {Count} energy history points for house {HouseId} between {FromDate} and {ToDate}",
                results.Count, houseId, fromDate, toDate);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve energy history range for house {HouseId} between {FromDate} and {ToDate}",
                houseId, fromDate, toDate);
            throw;
        }
    }

    /// <summary>
    /// Per-hour upsert. Each incoming point overwrites the matching hour in the
    /// partition; hours that aren't in <paramref name="newPoints"/> are left
    /// alone. Intentionally non-destructive: a backfill that only produces
    /// some hours of the day (because Home Assistant had partial data, or
    /// because the daemon skipped empty hours) will only touch those hours.
    /// The historic delete-then-insert behaviour was the root cause of the
    /// May 2026 data-loss incident.
    /// </summary>
    public async Task ReplaceHistoryAsync(string houseId, string date, List<EnergyHistoryPoint> newPoints)
    {
        try
        {
            string partitionKey = $"{houseId}_{date}";

            foreach (EnergyHistoryPoint point in newPoints)
            {
                point.PartitionKey = partitionKey;
                point.RowKey = point.Hour.ToString("D2");
                point.HouseId = houseId;
                point.Date = date;
                await _tableClient.UpsertEntityAsync(point);
            }

            _logger.LogInformation("Upserted {Count} energy history points for house {HouseId} on {Date}",
                newPoints.Count, houseId, date);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upsert energy history for house {HouseId} on {Date}", houseId, date);
            throw;
        }
    }

    public async Task CleanupOldHistoryAsync(string houseId, int retentionDays = 30)
    {
        try
        {
            DateTimeOffset cutoffDate = DateTimeOffset.UtcNow.AddDays(-retentionDays);
            int deletedCount = 0;

            await foreach (EnergyHistoryPoint point in _tableClient.QueryAsync<EnergyHistoryPoint>(
                filter: $"HouseId eq '{houseId}' and RecordedAt lt datetime'{cutoffDate:O}'"))
            {
                await _tableClient.DeleteEntityAsync(point.PartitionKey, point.RowKey);
                deletedCount++;
            }

            _logger.LogInformation("Cleaned up {Count} old energy history records for house {HouseId}", deletedCount, houseId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup old energy history for house {HouseId}", houseId);
            throw;
        }
    }
}

using Azure.Data.Tables;
using HomeAssistant.Functions.Models;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Functions.Services;

public class BatteryPricingStorageService
{
    private readonly TableClient _tableClient;
    private readonly ILogger _logger;
    private const string TableName = "energypricehistory";

    public BatteryPricingStorageService(string connectionString, ILogger logger)
    {
        _logger = logger;
        TableServiceClient serviceClient = new TableServiceClient(connectionString);
        _tableClient = serviceClient.GetTableClient(TableName);

        _tableClient.CreateIfNotExists();
    }

    public async Task SavePricingDataAsync(string houseId, string date, List<BatteryPricingPoint> points)
    {
        try
        {
            string partitionKey = $"{houseId}_{date}";

            List<BatteryPricingPoint> compressed = CompressPoints(points);

            foreach (BatteryPricingPoint point in compressed)
            {
                point.PartitionKey = partitionKey;
                point.RowKey = point.TimeMinutes.ToString("D4");
                point.HouseId = houseId;
                point.Date = date;

                await _tableClient.UpsertEntityAsync(point);
            }

            _logger.LogDebug("Saved {Count} pricing points (compressed from {Original}) for house {HouseId} on {Date}",
                compressed.Count, points.Count, houseId, date);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save pricing data for house {HouseId} on {Date}", houseId, date);
            throw;
        }
    }

    public async Task<List<BatteryPricingPoint>> GetPricingDataAsync(string houseId, string date)
    {
        try
        {
            string partitionKey = $"{houseId}_{date}";
            List<BatteryPricingPoint> results = [];

            await foreach (BatteryPricingPoint point in _tableClient.QueryAsync<BatteryPricingPoint>(
                filter: $"PartitionKey eq '{partitionKey}'"))
            {
                results.Add(point);
            }

            List<BatteryPricingPoint> ordered = results.OrderBy(p => p.TimeMinutes).ToList();
            List<BatteryPricingPoint> expanded = ExpandPoints(ordered);

            _logger.LogDebug("Retrieved {Count} pricing points (expanded to {Expanded}) for house {HouseId} on {Date}",
                ordered.Count, expanded.Count, houseId, date);
            return expanded;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve pricing data for house {HouseId} on {Date}", houseId, date);
            throw;
        }
    }

    public async Task DeletePricingDataAsync(string houseId, string date)
    {
        try
        {
            string partitionKey = $"{houseId}_{date}";
            List<BatteryPricingPoint> records = [];

            await foreach (BatteryPricingPoint point in _tableClient.QueryAsync<BatteryPricingPoint>(
                filter: $"PartitionKey eq '{partitionKey}'"))
            {
                records.Add(point);
            }

            foreach (BatteryPricingPoint record in records)
            {
                await _tableClient.DeleteEntityAsync(record.PartitionKey, record.RowKey);
            }

            _logger.LogInformation("Deleted {Count} pricing records for house {HouseId} on {Date}", records.Count, houseId, date);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete pricing data for house {HouseId} on {Date}", houseId, date);
            throw;
        }
    }

    /// <summary>
    /// Compresses pricing points by removing consecutive points with the same rates.
    /// Keeps the first point of each constant-rate run and any point where a rate changes.
    /// </summary>
    public static List<BatteryPricingPoint> CompressPoints(List<BatteryPricingPoint> points)
    {
        if (points.Count <= 1)
            return points.ToList();

        List<BatteryPricingPoint> sorted = points.OrderBy(p => p.TimeMinutes).ToList();
        List<BatteryPricingPoint> result = [sorted[0]];

        for (int i = 1; i < sorted.Count; i++)
        {
            BatteryPricingPoint current = sorted[i];
            BatteryPricingPoint previous = sorted[i - 1];

            // Keep point if rates differ from the previous point
            if (Math.Abs(current.ImportPrice - previous.ImportPrice) > 0.0001 ||
                Math.Abs(current.ExportPrice - previous.ExportPrice) > 0.0001)
            {
                result.Add(current);
            }
        }

        return result;
    }

    /// <summary>
    /// Expands compressed points by filling in 30-minute interval gaps between consecutive
    /// points that have the same rates. Points at non-30-minute boundaries (sensor overrides)
    /// are kept as-is.
    /// </summary>
    public static List<BatteryPricingPoint> ExpandPoints(List<BatteryPricingPoint> points)
    {
        if (points.Count <= 1)
            return points.ToList();

        List<BatteryPricingPoint> result = [];

        for (int i = 0; i < points.Count; i++)
        {
            BatteryPricingPoint current = points[i];
            result.Add(current);

            // If there's a next point, check if we need to fill in 30-min gaps
            if (i < points.Count - 1)
            {
                BatteryPricingPoint next = points[i + 1];

                // Only fill gaps if rates are the same (i.e., this was a compressed constant-rate run)
                if (Math.Abs(current.ImportPrice - next.ImportPrice) < 0.0001 &&
                    Math.Abs(current.ExportPrice - next.ExportPrice) < 0.0001)
                {
                    // Find the first 30-min aligned time after current
                    int firstAligned = ((current.TimeMinutes / 30) + 1) * 30;

                    for (int minutes = firstAligned; minutes < next.TimeMinutes; minutes += 30)
                    {
                        result.Add(new BatteryPricingPoint
                        {
                            PartitionKey = current.PartitionKey,
                            RowKey = minutes.ToString("D4"),
                            HouseId = current.HouseId,
                            Date = current.Date,
                            TimeMinutes = minutes,
                            ImportPrice = current.ImportPrice,
                            ExportPrice = current.ExportPrice
                        });
                    }
                }
            }
        }

        return result.OrderBy(p => p.TimeMinutes).ToList();
    }
}

using Azure;
using Azure.Data.Tables;

namespace HomeAssistant.Functions.Models;

public class BatteryHistoryPoint : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string HouseId { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public double BatteryPercent { get; set; }
    public double? PowerWatts { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}

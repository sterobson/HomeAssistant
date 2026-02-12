using Azure;
using Azure.Data.Tables;

namespace HomeAssistant.Functions.Models;

public class BatteryPricingPoint : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string HouseId { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public int TimeMinutes { get; set; }
    public double ImportPrice { get; set; }
    public double ExportPrice { get; set; }
}

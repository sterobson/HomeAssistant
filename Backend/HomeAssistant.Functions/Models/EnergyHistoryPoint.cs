using Azure;
using Azure.Data.Tables;

namespace HomeAssistant.Functions.Models;

public class EnergyHistoryPoint : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string HouseId { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public int Hour { get; set; }
    public double GridKwh { get; set; }
    public double BatteryKwh { get; set; }
    public double SolarKwh { get; set; }
    public double HouseKwh { get; set; }
    public double ImportCostPence { get; set; }
    public double ExportCostPence { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}

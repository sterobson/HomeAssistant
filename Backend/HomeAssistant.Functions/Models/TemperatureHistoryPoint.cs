using Azure;
using Azure.Data.Tables;

namespace HomeAssistant.Functions.Models;

/// <summary>
/// Represents a single temperature history data point stored in Azure Table Storage
/// </summary>
public class TemperatureHistoryPoint : ITableEntity
{
    /// <summary>
    /// Partition key: {houseId}_{roomId} for efficient querying by room
    /// </summary>
    public string PartitionKey { get; set; } = string.Empty;

    /// <summary>
    /// Row key: Inverted ticks (long.MaxValue - ticks) for reverse chronological ordering
    /// </summary>
    public string RowKey { get; set; } = string.Empty;

    /// <summary>
    /// Azure Table Storage timestamp
    /// </summary>
    public DateTimeOffset? Timestamp { get; set; }

    /// <summary>
    /// Azure Table Storage ETag
    /// </summary>
    public ETag ETag { get; set; }

    /// <summary>
    /// House identifier
    /// </summary>
    public string HouseId { get; set; } = string.Empty;

    /// <summary>
    /// Room identifier
    /// </summary>
    public int RoomId { get; set; }

    /// <summary>
    /// Current temperature reading in degrees Celsius
    /// </summary>
    public double? CurrentTemperature { get; set; }

    /// <summary>
    /// Target temperature from the active schedule in degrees Celsius
    /// </summary>
    public double? TargetTemperature { get; set; }

    /// <summary>
    /// Whether heating was active at this point in time
    /// </summary>
    public bool HeatingActive { get; set; }

    /// <summary>
    /// When this data point was recorded
    /// </summary>
    public DateTimeOffset RecordedAt { get; set; }
}

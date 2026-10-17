using Azure;
using Azure.Data.Tables;

namespace HomeAssistant.Functions.Models;

/// <summary>
/// One day's battery reading for one device, in Azure Table Storage.
/// </summary>
public class DeviceBatteryRecord : ITableEntity
{
    /// <summary>
    /// Partition key: {houseId}_{entityId}, so one device's history is a single partition scan.
    /// </summary>
    public string PartitionKey { get; set; } = string.Empty;

    /// <summary>
    /// Row key: inverted ticks of the reading's date at midnight UTC, matching the convention
    /// in <see cref="TemperatureHistoryPoint"/> so the newest row sorts first.
    ///
    /// Because it is derived from the date alone, two pushes on the same day produce the same
    /// row key and the second simply replaces the first. That is what keeps this to one row
    /// per device per day even though the app pushes more often than daily and starts a push
    /// on every restart.
    /// </summary>
    public string RowKey { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string HouseId { get; set; } = string.Empty;

    public string EntityId { get; set; } = string.Empty;

    /// <summary>
    /// The friendly name as it was on the day of the reading. Stored per row rather than
    /// resolved later: entity IDs are not renamed when a device is, so an old ID can name the
    /// wrong room entirely.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Charge percentage, or null when the sensor reported none.
    /// </summary>
    public double? Percent { get; set; }

    /// <summary>
    /// False when the sensor was unavailable. Never conflate this with 0% — see the remarks
    /// on DeviceBatteryReadingDto.IsAvailable.
    /// </summary>
    public bool IsAvailable { get; set; }

    /// <summary>
    /// The date this reading belongs to, at midnight UTC.
    /// </summary>
    public DateTimeOffset RecordedOn { get; set; }
}

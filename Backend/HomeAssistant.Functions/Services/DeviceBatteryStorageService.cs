using Azure.Data.Tables;
using HomeAssistant.Functions.Models;
using HomeAssistant.Shared;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Functions.Services;

/// <summary>
/// Stores a daily battery level per device in Azure Table Storage.
///
/// The entities blob already carries current battery levels, but it is overwritten on every
/// push, so nothing there can show a cell declining or say when one stopped reporting. This
/// table is the durable record: one row per device per day, written idempotently.
/// </summary>
public class DeviceBatteryStorageService
{
    private const string TableName = "devicebatteryhistory";

    private readonly TableClient _tableClient;
    private readonly ILogger _logger;

    public DeviceBatteryStorageService(string connectionString, ILogger logger)
    {
        _logger = logger;
        TableServiceClient serviceClient = new TableServiceClient(connectionString);
        _tableClient = serviceClient.GetTableClient(TableName);
        _tableClient.CreateIfNotExists();
    }

    /// <summary>
    /// Records a snapshot. Readings are keyed by device and date, so pushing twice in one day
    /// updates that day's rows rather than adding duplicates.
    /// </summary>
    public async Task SaveSnapshotAsync(string houseId, DeviceBatterySnapshotDto snapshot)
    {
        DateTimeOffset recordedOn = ToUtcDate(snapshot.RecordedAt);
        string rowKey = GenerateRowKey(recordedOn);

        foreach (DeviceBatteryReadingDto reading in snapshot.Readings)
        {
            if (string.IsNullOrWhiteSpace(reading.EntityId))
            {
                continue;
            }

            DeviceBatteryRecord record = new DeviceBatteryRecord
            {
                PartitionKey = BuildPartitionKey(houseId, reading.EntityId),
                RowKey = rowKey,
                HouseId = houseId,
                EntityId = reading.EntityId,
                Name = reading.Name,
                Percent = reading.Percent,
                IsAvailable = reading.IsAvailable,
                RecordedOn = recordedOn
            };

            await _tableClient.UpsertEntityAsync(record, TableUpdateMode.Replace);
        }

        _logger.LogInformation(
            "Saved {Count} device battery readings for house {HouseId} on {RecordedOn:yyyy-MM-dd}",
            snapshot.Readings.Count, houseId, recordedOn);
    }

    /// <summary>
    /// Returns the last <paramref name="days"/> days of readings, newest day first.
    /// </summary>
    public async Task<DeviceBatteryHistoryDto> GetHistoryAsync(string houseId, int days)
    {
        DateTimeOffset cutoff = ToUtcDate(DateTimeOffset.UtcNow).AddDays(-Math.Max(days - 1, 0));

        // Inverted ticks: a later date yields a smaller row key, so "newer than the cutoff"
        // is "row key less than or equal to the cutoff's key".
        string cutoffRowKey = GenerateRowKey(cutoff);
        string prefix = $"{houseId}_";

        List<DeviceBatteryRecord> records = [];

        await foreach (DeviceBatteryRecord record in _tableClient.QueryAsync<DeviceBatteryRecord>(
            filter: $"PartitionKey ge '{prefix}' and PartitionKey lt '{prefix}~' and RowKey le '{cutoffRowKey}'"))
        {
            records.Add(record);
        }

        List<DeviceBatterySnapshotDto> snapshots = [.. records
            .GroupBy(record => record.RecordedOn)
            .OrderByDescending(group => group.Key)
            .Select(group => new DeviceBatterySnapshotDto
            {
                RecordedAt = group.Key,
                Readings = [.. group
                    .OrderBy(record => record.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(record => new DeviceBatteryReadingDto
                    {
                        EntityId = record.EntityId,
                        Name = record.Name,
                        Percent = record.Percent,
                        IsAvailable = record.IsAvailable
                    })]
            })];

        return new DeviceBatteryHistoryDto { Snapshots = snapshots };
    }

    internal static string BuildPartitionKey(string houseId, string entityId) => $"{houseId}_{entityId}";

    /// <summary>
    /// Midnight UTC of the day a reading belongs to. UTC rather than local time so that the
    /// app's "have I pushed today?" guard and the stored row always agree on the date.
    /// </summary>
    internal static DateTimeOffset ToUtcDate(DateTimeOffset value) =>
        new DateTimeOffset(value.UtcDateTime.Date, TimeSpan.Zero);

    /// <summary>
    /// Inverted ticks of the reading's UTC date, so newer rows sort first. Derived from the
    /// date alone, which is what makes a repeat push on the same day replace rather than
    /// duplicate.
    /// </summary>
    internal static string GenerateRowKey(DateTimeOffset date) =>
        (long.MaxValue - ToUtcDate(date).UtcTicks).ToString("D19");
}

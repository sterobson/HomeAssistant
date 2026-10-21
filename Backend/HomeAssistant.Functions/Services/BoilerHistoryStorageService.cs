using Azure.Data.Tables;
using HomeAssistant.Functions.Models;
using HomeAssistant.Shared.Climate;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Globalization;

namespace HomeAssistant.Functions.Services;

/// <summary>
/// The boiler's on/off history (owner, 2026-10-21), as the NetDaemon app believed it. Recorded from the room-state
/// uploads, which carry the belief (<see cref="RoomStatesResponse.Boiler"/>), so the app needs no change to feed it:
/// a row is written only when the belief or the control path differs from the newest row. A corrective press, which
/// keeps the belief and moves its "since", is not a change and is not recorded.
/// </summary>
public class BoilerHistoryStorageService
{
    private const string TableName = "boilerhistory";

    /// <summary>The longest window the history endpoint returns.</summary>
    public const int MaxHours = 31 * 24;

    private readonly TableClient _tableClient;
    private readonly ILogger _logger;

    // The newest row per house, so most uploads (which change nothing) need no table read.
    private static readonly ConcurrentDictionary<string, BoilerHistoryRecord> _newest = new();

    public BoilerHistoryStorageService(string connectionString, ILogger logger)
    {
        _logger = logger;
        TableServiceClient serviceClient = new TableServiceClient(connectionString);
        _tableClient = serviceClient.GetTableClient(TableName);
        _tableClient.CreateIfNotExists();
    }

    /// <summary>Writes a row when the uploaded belief differs from the newest one stored.</summary>
    public async Task RecordIfChangedAsync(string houseId, BoilerStateDto boiler, DateTimeOffset receivedAt)
    {
        bool on = boiler.AppControlsBoiler && boiler.BelievedOn;
        BoilerHistoryRecord? newest = _newest.TryGetValue(houseId, out BoilerHistoryRecord? cached) ? cached : await ReadNewestAsync(houseId);

        // An inferred row (a backfill) is compared like any other: the app's belief starts a new row only when it differs.
        if (newest != null && newest.On == on && newest.AppControlsBoiler == boiler.AppControlsBoiler)
        {
            _newest[houseId] = newest;
            return;
        }

        DateTimeOffset at = DateTimeOffset.TryParse(boiler.Since, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset since)
            ? since
            : receivedAt;

        // A belief older than the newest row would sort behind it and never show as the latest state.
        if (newest != null && at <= newest.At)
        {
            at = receivedAt;
        }

        BoilerHistoryRecord record = new BoilerHistoryRecord
        {
            PartitionKey = houseId,
            RowKey = GenerateRowKey(at),
            At = at,
            On = on,
            AppControlsBoiler = boiler.AppControlsBoiler
        };

        await _tableClient.UpsertEntityAsync(record, TableUpdateMode.Replace);
        _newest[houseId] = record;
        _logger.LogInformation("Boiler history for house {HouseId}: {State} at {At:O}", houseId,
            boiler.AppControlsBoiler ? (on ? "on" : "off") : "run by the boiler thermostat", at);
    }

    /// <summary>
    /// Every change in the last <paramref name="hours"/> hours, oldest first, preceded by the newest change before
    /// the window (if any), so a chart knows what the boiler was doing when the window opened.
    /// </summary>
    public async Task<BoilerHistoryDto> GetHistoryAsync(string houseId, int hours, DateTimeOffset now)
    {
        DateTimeOffset from = now.AddHours(-Math.Clamp(hours, 1, MaxHours));
        string fromKey = GenerateRowKey(from);
        List<BoilerHistoryEventDto> events = [];

        // Inverted ticks: newer than the window's start is a row key at or below its key.
        await foreach (BoilerHistoryRecord record in _tableClient.QueryAsync<BoilerHistoryRecord>(
            filter: $"PartitionKey eq '{Escape(houseId)}' and RowKey le '{fromKey}'"))
        {
            events.Add(ToDto(record));
        }

        BoilerHistoryEventDto? before = null;
        await foreach (BoilerHistoryRecord record in _tableClient.QueryAsync<BoilerHistoryRecord>(
            filter: $"PartitionKey eq '{Escape(houseId)}' and RowKey gt '{fromKey}'", maxPerPage: 1))
        {
            before = ToDto(record);
            break;
        }

        return new BoilerHistoryDto
        {
            From = from.ToString("O"),
            To = now.ToString("O"),
            Before = before,
            Events = [.. events.OrderBy(e => e.At, StringComparer.Ordinal)]
        };
    }

    private async Task<BoilerHistoryRecord?> ReadNewestAsync(string houseId)
    {
        await foreach (BoilerHistoryRecord record in _tableClient.QueryAsync<BoilerHistoryRecord>(
            filter: $"PartitionKey eq '{Escape(houseId)}'", maxPerPage: 1))
        {
            return record;
        }

        return null;
    }

    private static BoilerHistoryEventDto ToDto(BoilerHistoryRecord record) => new BoilerHistoryEventDto
    {
        At = record.At.UtcDateTime.ToString("O"),
        On = record.On,
        AppControlsBoiler = record.AppControlsBoiler,
        Inferred = record.Inferred
    };

    private static string Escape(string value) => value.Replace("'", "''");

    /// <summary>Inverted ticks, so the newest row sorts first.</summary>
    internal static string GenerateRowKey(DateTimeOffset at) => (long.MaxValue - at.UtcTicks).ToString("D19");
}

/// <summary>The boiler's history over a window, for the UI's chart.</summary>
public class BoilerHistoryDto
{
    /// <summary>The window, ISO 8601 UTC.</summary>
    public string From { get; set; } = string.Empty;

    public string To { get; set; } = string.Empty;

    /// <summary>The newest change before the window: what the boiler was doing as it opened. Null if none is stored.</summary>
    public BoilerHistoryEventDto? Before { get; set; }

    /// <summary>The changes within the window, oldest first.</summary>
    public List<BoilerHistoryEventDto> Events { get; set; } = [];
}

public class BoilerHistoryEventDto
{
    /// <summary>ISO 8601 UTC.</summary>
    public string At { get; set; } = string.Empty;

    public bool On { get; set; }

    public bool AppControlsBoiler { get; set; }

    /// <summary>Worked out from the radiators afterwards rather than recorded from the app's belief.</summary>
    public bool Inferred { get; set; }
}

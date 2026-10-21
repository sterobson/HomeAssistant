using Azure;
using Azure.Data.Tables;

namespace HomeAssistant.Functions.Models;

/// <summary>
/// One change in what the NetDaemon app believes the boiler is doing, in Azure Table Storage (owner, 2026-10-21).
/// A belief, not a reading: nothing in Home Assistant can see the boiler (CLAUDE.md, "The boiler has no feedback").
/// </summary>
public class BoilerHistoryRecord : ITableEntity
{
    /// <summary>Partition key: the house ID, so one house's history is a single partition scan.</summary>
    public string PartitionKey { get; set; } = string.Empty;

    /// <summary>
    /// Row key: inverted ticks of <see cref="At"/>, so the newest change sorts first. The same change uploaded twice
    /// (every room-state upload carries the boiler) has the same key and simply replaces itself.
    /// </summary>
    public string RowKey { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    /// <summary>When the app came to believe this: its own "since", or the upload time when it sent none.</summary>
    public DateTimeOffset At { get; set; }

    /// <summary>Whether the app believed the boiler on. Always false while <see cref="AppControlsBoiler"/> is false.</summary>
    public bool On { get; set; }

    /// <summary>False while the boiler thermostat runs the boiler, when the app knows nothing of it.</summary>
    public bool AppControlsBoiler { get; set; }

    /// <summary>
    /// True for a row worked out afterwards from the radiator valves' own sensors warming and cooling
    /// (tools/ha/boiler-history-backfill.cjs), not from the app's belief: what the boiler evidently did, whoever ran it.
    /// </summary>
    public bool Inferred { get; set; }
}

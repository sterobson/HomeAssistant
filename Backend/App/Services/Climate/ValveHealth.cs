using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using HomeAssistant.Shared.Climate;

namespace HomeAssistant.Services.Climate;

/// <summary>
/// What a radiator valve was seen doing wrong, and when it last looked fine, kept per valve and per kind of problem
/// (<see cref="ValveHealthKinds"/>).
/// </summary>
internal sealed class ValveHealthRecord
{
    /// <summary>How many times the problem has been seen, ever.</summary>
    public int Count { get; set; }

    /// <summary>When it was last seen.</summary>
    public DateTimeOffset? LastSeenAt { get; set; }

    /// <summary>What was seen that time, in words, e.g. "Rose from 20.1 °C to 27.1 °C while shut".</summary>
    public string? LastSeenDetail { get; set; }

    /// <summary>
    /// The valve's own readings that time, in °C: for a leak its lowest while shut and how high it climbed; for one
    /// that stayed cold its reading at the start of the hour and its highest in it.
    /// </summary>
    public double? FromTemperature { get; set; }
    public double? ToTemperature { get; set; }

    /// <summary>For one that stayed cold: how long it had been open, in minutes.</summary>
    public int? Minutes { get; set; }

    /// <summary>When a boiler run last showed the valve behaving properly, which clears an earlier sighting.</summary>
    public DateTimeOffset? LastOkAt { get; set; }

    /// <summary>Seen, and not cleared by a later run.</summary>
    [JsonIgnore]
    public bool Flagged => LastSeenAt != null && (LastOkAt == null || LastOkAt < LastSeenAt);
}

/// <summary>One heating pass's view of a radiator valve.</summary>
/// <param name="Open">Whether the app has it open (its setpoint at or above its own reading, or just sent so).</param>
/// <param name="ValveTemperature">The valve's own onboard reading, which sits on the radiator.</param>
/// <param name="Setpoint">The valve's setpoint.</param>
/// <param name="RoomTemperature">The room thermometer's reading, to tell a cold radiator from a warm one.</param>
internal readonly record struct ValveObservation(bool Open, double? ValveTemperature, double? Setpoint, double? RoomTemperature);

/// <summary>
/// Watches every radiator valve for the two faults found by hand on 2026-10-23 (owner): a valve that lets hot water
/// through while shut, and one that stays cold while open. Fed one observation per valve per heating pass, read-only:
/// it never moves anything, and a suspected fault is confirmed or cleared by the boiler's own runs.
/// </summary>
internal interface IValveHealthMonitor
{
    /// <summary>
    /// Records a valve this pass, with what the app believes the boiler is doing (null in boiler-thermostat mode, when
    /// the app cannot know). Call it for every valve in the same pass with the same <paramref name="now"/>.
    /// </summary>
    void Observe(string entityId, ValveObservation observation, bool? boilerBelievedOn, DateTimeOffset now);

    /// <summary>Forgets what every valve has been doing (not the findings): the valve exercise moves them all.</summary>
    void ResetSpells();

    /// <summary>The valve's findings, one per kind that has ever been seen or cleared.</summary>
    IReadOnlyDictionary<string, ValveHealthRecord> RecordsFor(string entityId);
}

/// <summary>Where the findings are kept between restarts.</summary>
internal interface IValveHealthStore
{
    Dictionary<string, Dictionary<string, ValveHealthRecord>> Load();

    void Save(Dictionary<string, Dictionary<string, ValveHealthRecord>> records);
}

/// <summary>
/// The rules, from the 2026-10-23 investigation (HOME-ASSISTANT.md → Radiator TRVs) and tuned by replaying ten days of
/// recorder history through them, in which only den radiator 2 had a real fault. All read the valves' own sensors:
/// <list type="bullet">
/// <item><b>Proof of hot water</b> is an open valve's radiator still climbing, at least <see cref="RisingDegrees"/>
/// above its lowest reading in the last <see cref="WindowMinutes"/>. The app's belief that the boiler is on is not
/// proof: it has been wrong both ways (HOME-ASSISTANT.md gotchas 23, 24, 27).</item>
/// <item><b>Leaks when shut:</b> a valve shut for at least <see cref="ShutSettleMinutes"/> (a radiator coasts up a
/// little after closing) whose reading climbs <see cref="LeakRise"/> or more above its lowest while hot water is
/// likely: proof from another radiator, or the belief, within the last <see cref="AfterHotWaterMinutes"/>. Cleared by
/// a shut valve sitting through <see cref="WindowMinutes"/> of proof rising less than <see cref="RisingDegrees"/>.</item>
/// <item><b>Doesn't warm:</b> a valve meaningfully open (setpoint <see cref="DemandDegrees"/> or more above its own
/// reading: a head at its setpoint throttles) for a whole <see cref="WindowMinutes"/>, cold at the start of it (within
/// <see cref="ColdDegrees"/> of the room), with proof from another radiator in both halves of it, that rose less than
/// <see cref="RisingDegrees"/>. Cleared by an open valve rising that much.</item>
/// </list>
/// A gap of <see cref="LongestGap"/> between a valve's observations (the app was not running) starts it afresh.
/// </summary>
internal sealed class ValveHealthMonitor : IValveHealthMonitor
{
    internal const double LeakRise = 2.0;
    internal const double ShutSettleMinutes = 45;
    internal const double WindowMinutes = 60;
    internal const double RisingDegrees = 1.0;
    internal const double DemandDegrees = 1.0;
    internal const double ColdDegrees = 2.0;
    internal const double AfterHotWaterMinutes = 30;
    internal const double StillRisingMinutes = 15;
    internal static readonly TimeSpan LongestGap = TimeSpan.FromMinutes(20);

    // How much history to keep per valve: a window, with room to look back across it.
    private static readonly TimeSpan HistoryKept = TimeSpan.FromMinutes(WindowMinutes * 2);

    private readonly record struct Sample(DateTimeOffset At, ValveObservation Observation);

    private sealed class ValveState
    {
        public List<Sample> History { get; } = [];

        // When this valve's radiator was seen climbing while open: proof of hot water.
        public List<DateTimeOffset> RisingAt { get; } = [];

        // The current shut spell: since when, its lowest and highest readings while hot water was likely, and whether
        // it has given its finding or shown the valve shuts.
        public DateTimeOffset? ShutSince { get; set; }
        public double? ShutLow { get; set; }
        public double? ShutPeak { get; set; }
        public bool LeakReported { get; set; }
        public bool LeakCleared { get; set; }

        // The current open-with-demand spell: whether it has given its finding, or shown the valve works.
        public DateTimeOffset? DemandSince { get; set; }
        public bool ColdReported { get; set; }
        public bool ColdCleared { get; set; }
    }

    private readonly object _lock = new();
    private readonly IValveHealthStore _store;
    private readonly ILogger<ValveHealthMonitor>? _logger;
    private readonly Dictionary<string, Dictionary<string, ValveHealthRecord>> _records;
    private readonly Dictionary<string, ValveState> _valves = new(StringComparer.OrdinalIgnoreCase);

    // When the app last believed the boiler on. A shut valve's radiator keeps climbing for a while after the boiler
    // goes off (or after a belief that it went off, which on 2026-10-23 was wrong), so it counts for a while after.
    private DateTimeOffset? _boilerBelievedOnAt;

    public ValveHealthMonitor(IValveHealthStore store, ILogger<ValveHealthMonitor>? logger = null)
    {
        _store = store;
        _logger = logger;
        _records = new(store.Load(), StringComparer.OrdinalIgnoreCase);
    }

    public void Observe(string entityId, ValveObservation observation, bool? boilerBelievedOn, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (boilerBelievedOn == true)
            {
                _boilerBelievedOnAt = now;
            }

            if (!_valves.TryGetValue(entityId, out ValveState? valve) || (valve.History.Count > 0 && now - valve.History[^1].At > LongestGap))
            {
                valve = new ValveState();
                _valves[entityId] = valve;
            }

            valve.History.Add(new Sample(now, observation));
            valve.History.RemoveAll(sample => now - sample.At > HistoryKept);
            valve.RisingAt.RemoveAll(at => now - at > HistoryKept);

            if (observation.ValveTemperature is not double temperature)
            {
                return;
            }

            if (observation.Open)
            {
                valve.ShutSince = null;
                NoteIfRising(valve, temperature, now);
                CheckOpen(entityId, valve, observation, temperature, now);
            }
            else
            {
                valve.DemandSince = null;
                CheckShut(entityId, valve, temperature, now);
            }
        }
    }

    /// <summary>
    /// An open valve's radiator climbing proves hot water is flowing: up <see cref="RisingDegrees"/> on its lowest in the
    /// last <see cref="WindowMinutes"/>, and still going up (higher than <see cref="StillRisingMinutes"/> ago). A radiator
    /// sitting at its peak proves nothing: on 2026-10-19 den radiator 1 held its peak for an hour after the boiler stopped.
    /// </summary>
    private static void NoteIfRising(ValveState valve, double temperature, DateTimeOffset now)
    {
        List<Sample> recent = [.. valve.History
            .Where(sample => now - sample.At <= TimeSpan.FromMinutes(WindowMinutes) && sample.Observation.Open && sample.Observation.ValveTemperature != null)];
        if (recent.Count < 2)
        {
            return;
        }

        double lowest = recent.Min(sample => sample.Observation.ValveTemperature!.Value);
        double earlier = recent.Last(sample => now - sample.At >= TimeSpan.FromMinutes(StillRisingMinutes) || sample.Equals(recent[0])).Observation.ValveTemperature!.Value;
        if (temperature - lowest >= RisingDegrees && temperature > earlier)
        {
            valve.RisingAt.Add(now);
        }
    }

    private void CheckOpen(string entityId, ValveState valve, ValveObservation observation, double temperature, DateTimeOffset now)
    {
        bool demand = observation.Setpoint is double setpoint && setpoint - temperature >= DemandDegrees;
        if (!demand)
        {
            valve.DemandSince = null;
            valve.ColdReported = false;
            valve.ColdCleared = false;
            return;
        }

        if (valve.DemandSince == null)
        {
            valve.DemandSince = now;
            valve.ColdReported = false;
            valve.ColdCleared = false;
        }

        DateTimeOffset windowStart = now - TimeSpan.FromMinutes(WindowMinutes);
        List<Sample> window = [.. valve.History.Where(sample => sample.At >= windowStart && sample.Observation.ValveTemperature != null)];
        double first = window[0].Observation.ValveTemperature!.Value;
        double rise = window.Max(sample => sample.Observation.ValveTemperature!.Value) - first;
        if (rise >= RisingDegrees)
        {
            if (!valve.ColdCleared)
            {
                valve.ColdCleared = true;
                MarkOk(entityId, ValveHealthKinds.DoesNotWarm, now);
            }

            return;
        }

        // Once a valve has warmed its radiator in this spell it evidently opens; it may simply be at its peak now.
        if (valve.ColdReported || valve.ColdCleared || valve.DemandSince > windowStart)
        {
            return;
        }

        // Cold at the start, so a rise was to be expected; a radiator already hot from an earlier run may have no
        // further to go.
        double? room = window[0].Observation.RoomTemperature;
        if (room == null || first - room.Value > ColdDegrees)
        {
            return;
        }

        if (!HotWaterAllHour(entityId, now))
        {
            return;
        }

        valve.ColdReported = true;
        int openMinutes = (int)(now - valve.DemandSince.Value).TotalMinutes;
        double highest = first + rise;
        string detail = $"Open for {openMinutes} minutes while another radiator warmed, but rose only {rise:0.0} °C (from {first:0.0} °C)";
        Record(entityId, ValveHealthKinds.DoesNotWarm, now, detail, first, highest, openMinutes);
    }

    private void CheckShut(string entityId, ValveState valve, double temperature, DateTimeOffset now)
    {
        if (valve.ShutSince == null)
        {
            valve.ShutSince = now;
            valve.ShutLow = null;
            valve.ShutPeak = null;
            valve.LeakReported = false;
            valve.LeakCleared = false;
        }

        if ((now - valve.ShutSince.Value).TotalMinutes < ShutSettleMinutes)
        {
            return;
        }

        DateTimeOffset recently = now - TimeSpan.FromMinutes(AfterHotWaterMinutes);
        bool proven = OthersRising(entityId, recently, now);
        bool hotWaterLikely = proven || _boilerBelievedOnAt >= recently;
        if (!hotWaterLikely)
        {
            // Without hot water a rise means nothing (the sun, a fan heater beside it), so start again from here.
            valve.ShutLow = null;
            return;
        }

        if (valve.ShutLow == null || temperature < valve.ShutLow)
        {
            valve.ShutLow = temperature;
        }

        if (valve.LeakReported)
        {
            // Carry on following the climb, so the finding says how far it went.
            if (temperature > valve.ShutPeak)
            {
                valve.ShutPeak = temperature;
                ValveHealthRecord record = RecordOf(entityId, ValveHealthKinds.LeaksWhenShut);
                record.LastSeenDetail = LeakDetail(valve.ShutLow.Value, temperature);
                record.ToTemperature = temperature;
                Save();
            }

            return;
        }

        double rise = temperature - valve.ShutLow.Value;
        if (rise >= LeakRise)
        {
            valve.LeakReported = true;
            valve.ShutPeak = temperature;
            Record(entityId, ValveHealthKinds.LeaksWhenShut, now, LeakDetail(valve.ShutLow.Value, temperature), valve.ShutLow.Value, temperature, null);
            return;
        }

        // Fine if it has sat through an hour of hot water, settled the whole time, and barely moved.
        DateTimeOffset windowStart = now - TimeSpan.FromMinutes(WindowMinutes);
        if (valve.LeakCleared
            || (windowStart - valve.ShutSince.Value).TotalMinutes < ShutSettleMinutes
            || !HotWaterAllHour(entityId, now))
        {
            return;
        }

        List<double> window = [.. valve.History
            .Where(sample => sample.At >= windowStart && sample.Observation.ValveTemperature != null)
            .Select(sample => sample.Observation.ValveTemperature!.Value)];
        if (window.Max() - window.Min() < RisingDegrees)
        {
            valve.LeakCleared = true;
            MarkOk(entityId, ValveHealthKinds.LeaksWhenShut, now);
        }
    }

    /// <summary>
    /// Hot water reached another radiator early in the last hour and was still reaching one late in it, so the boiler
    /// ran for most of it: long enough to judge a valve by.
    /// </summary>
    private bool HotWaterAllHour(string entityId, DateTimeOffset now)
    {
        DateTimeOffset middle = now - TimeSpan.FromMinutes(WindowMinutes / 2);
        return OthersRising(entityId, now - TimeSpan.FromMinutes(WindowMinutes), middle) && OthersRising(entityId, middle, now);
    }

    private static string LeakDetail(double low, double high) => $"Rose from {low:0.0} °C to {high:0.0} °C while shut, with hot water flowing";

    private bool OthersRising(string entityId, DateTimeOffset from, DateTimeOffset to) =>
        _valves.Any(pair => !string.Equals(pair.Key, entityId, StringComparison.OrdinalIgnoreCase)
            && pair.Value.RisingAt.Any(at => at >= from && at <= to));

    private void Record(string entityId, string kind, DateTimeOffset now, string detail, double from, double to, int? minutes)
    {
        ValveHealthRecord record = RecordOf(entityId, kind);
        record.Count++;
        record.LastSeenAt = now;
        record.LastSeenDetail = detail;
        record.FromTemperature = from;
        record.ToTemperature = to;
        record.Minutes = minutes;
        Save();
        _logger?.LogWarning("Valve health: {Valve} {Problem}. {Detail}", entityId, ValveHealthKinds.Describe(kind), detail);
    }

    private void MarkOk(string entityId, string kind, DateTimeOffset now)
    {
        ValveHealthRecord record = RecordOf(entityId, kind);
        bool wasFlagged = record.Flagged;
        record.LastOkAt = now;
        Save();
        if (wasFlagged)
        {
            _logger?.LogInformation("Valve health: {Valve} looked fine this time (it was suspected: {Problem})", entityId, ValveHealthKinds.Describe(kind));
        }
    }

    private ValveHealthRecord RecordOf(string entityId, string kind)
    {
        if (!_records.TryGetValue(entityId, out Dictionary<string, ValveHealthRecord>? byKind))
        {
            byKind = new(StringComparer.OrdinalIgnoreCase);
            _records[entityId] = byKind;
        }

        if (!byKind.TryGetValue(kind, out ValveHealthRecord? record))
        {
            record = new ValveHealthRecord();
            byKind[kind] = record;
        }

        return record;
    }

    private void Save() => _store.Save(_records);

    public void ResetSpells()
    {
        lock (_lock)
        {
            _valves.Clear();
            _boilerBelievedOnAt = null;
        }
    }

    public IReadOnlyDictionary<string, ValveHealthRecord> RecordsFor(string entityId)
    {
        lock (_lock)
        {
            return _records.TryGetValue(entityId, out Dictionary<string, ValveHealthRecord>? byKind)
                ? new Dictionary<string, ValveHealthRecord>(byKind, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, ValveHealthRecord>();
        }
    }
}

internal sealed class ValveHealthStore : IValveHealthStore
{
    private const string _storageFileName = "valve-health.json";
    private readonly ILogger<ValveHealthStore> _logger;
    private readonly string _path;

    public ValveHealthStore(ILogger<ValveHealthStore> logger)
    {
        _logger = logger;
        _path = Path.Combine(LocalStorageFolder.Resolve(), _storageFileName);
    }

    public Dictionary<string, Dictionary<string, ValveHealthRecord>> Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, ValveHealthRecord>>>(File.ReadAllText(_path)) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading the radiator valve health from {Path}", _path);
            return [];
        }
    }

    public void Save(Dictionary<string, Dictionary<string, ValveHealthRecord>> records)
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(records));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving the radiator valve health to {Path}", _path);
        }
    }
}

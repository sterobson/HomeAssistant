using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace HomeAssistant.Services.Climate;

/// <summary>One measured warm-up: a valve, with the boiler on, taking its room from <see cref="StartTemperature"/> to its target.</summary>
internal sealed record WarmUpSample(
    double DegreesPerHour,
    DateTimeOffset StartedAt,
    double Minutes,
    double StartTemperature,
    double ReachedTemperature);

/// <summary>
/// How fast each radiator valve actually warms its room (owner, 2026-10-20), so the pre-heat can be worked out
/// rather than guessed. Fed one observation per valve per heating pass.
/// </summary>
internal interface IWarmUpRates
{
    /// <summary>
    /// Records what a valve is doing this pass: <paramref name="heating"/> is whether it is open with the boiler on,
    /// <paramref name="roomTemperature"/> is the room thermometer's reading, <paramref name="targetTemperature"/> the
    /// target this valve is aiming at.
    /// </summary>
    void Observe(string entityId, bool heating, double roomTemperature, double targetTemperature, DateTimeOffset now);

    /// <summary>The average of the last few warm-ups in degrees Celsius an hour, or null while there are none.</summary>
    double? RateFor(string entityId);

    /// <summary>How many warm-ups the rate is the average of.</summary>
    int SampleCount(string entityId);

    /// <summary>Minutes this valve is expected to need to raise its room by this many degrees, or null without a rate.</summary>
    int? MinutesToRise(string entityId, double degrees);
}

/// <summary>Where the measured warm-ups are kept between restarts.</summary>
internal interface IWarmUpRateStore
{
    Dictionary<string, List<WarmUpSample>> Load();

    /// <summary>
    /// Warm-ups estimated from Home Assistant's history and shipped with the app (<c>warm-up-seed.json</c>), so a valve
    /// has a rate before it has measured one. Empty when there is no such file.
    /// </summary>
    Dictionary<string, List<WarmUpSample>> LoadSeed();

    void Save(Dictionary<string, List<WarmUpSample>> samples);
}

/// <summary>
/// A warm-up is one run of a valve being open with the boiler on, from when it began until the room first reached the
/// valve's target. It counts only if that took at least <see cref="MinimumMinutes"/> (a short top-up says little, and
/// the thermometer only reads in tenths of a degree) and the run did reach the target: one that ended early, because
/// the boiler went off or the target moved, is thrown away. A run gives at most one sample, and the rate used is the
/// average of the last <see cref="SamplesKept"/> per valve.
/// </summary>
internal sealed class WarmUpRates : IWarmUpRates
{
    internal const double MinimumMinutes = 30;
    internal const int SamplesKept = 5;

    // A gap this long between two observations means the app was not watching (it passes at least every 5 minutes),
    // so what happened in between is unknown and the run is dropped.
    private static readonly TimeSpan LongestGap = TimeSpan.FromMinutes(20);
    private const double TargetMovedTolerance = 0.05;

    private sealed class Run
    {
        public required DateTimeOffset StartedAt { get; init; }
        public required double StartTemperature { get; init; }
        public required double Target { get; init; }
        public DateTimeOffset LastSeen { get; set; }
    }

    private readonly object _lock = new();
    private readonly IWarmUpRateStore _store;
    private readonly ILogger<WarmUpRates>? _logger;
    private readonly Dictionary<string, List<WarmUpSample>> _samples;
    private readonly Dictionary<string, Run> _runs = new(StringComparer.OrdinalIgnoreCase);

    // Valves whose current heating run has already given its sample (or been spent), until it stops heating.
    private readonly HashSet<string> _spent = new(StringComparer.OrdinalIgnoreCase);

    public WarmUpRates(IWarmUpRateStore store, ILogger<WarmUpRates>? logger = null)
    {
        _store = store;
        _logger = logger;
        _samples = Merge(store.Load(), store.LoadSeed());
    }

    /// <summary>
    /// What the valves have measured themselves, topped up with the shipped estimates: the last <see cref="SamplesKept"/>
    /// by start time, so a valve's own warm-ups, being newer, push the estimates out as they come in. A sample already
    /// in both (the estimates are saved along with the measured ones) counts once.
    /// </summary>
    internal static Dictionary<string, List<WarmUpSample>> Merge(
        Dictionary<string, List<WarmUpSample>> measured, Dictionary<string, List<WarmUpSample>> seed)
    {
        Dictionary<string, List<WarmUpSample>> merged = new(StringComparer.OrdinalIgnoreCase);
        foreach (string entityId in measured.Keys.Concat(seed.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            List<WarmUpSample> own = measured.FirstOrDefault(pair => string.Equals(pair.Key, entityId, StringComparison.OrdinalIgnoreCase)).Value ?? [];
            List<WarmUpSample> estimated = seed.FirstOrDefault(pair => string.Equals(pair.Key, entityId, StringComparison.OrdinalIgnoreCase)).Value ?? [];
            merged[entityId] =
            [
                .. own.Concat(estimated.Where(e => !own.Any(o => o.StartedAt == e.StartedAt && o.DegreesPerHour == e.DegreesPerHour)))
                    .OrderBy(sample => sample.StartedAt)
                    .TakeLast(SamplesKept)
            ];
        }

        return merged;
    }

    public void Observe(string entityId, bool heating, double roomTemperature, double targetTemperature, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (!heating)
            {
                _runs.Remove(entityId);
                _spent.Remove(entityId);
                return;
            }

            if (_spent.Contains(entityId))
            {
                return;
            }

            if (_runs.TryGetValue(entityId, out Run? run)
                && (now - run.LastSeen > LongestGap || Math.Abs(run.Target - targetTemperature) > TargetMovedTolerance))
            {
                _runs.Remove(entityId);
                run = null;
            }

            if (run == null)
            {
                // A room already at its target is not being warmed, whatever the valve is doing.
                if (roomTemperature < targetTemperature)
                {
                    _runs[entityId] = new Run { StartedAt = now, StartTemperature = roomTemperature, Target = targetTemperature, LastSeen = now };
                }

                return;
            }

            run.LastSeen = now;
            if (roomTemperature < run.Target)
            {
                return;
            }

            // The room has reached its target: this is the whole of the run that counts.
            _runs.Remove(entityId);
            _spent.Add(entityId);

            double minutes = (now - run.StartedAt).TotalMinutes;
            double rise = roomTemperature - run.StartTemperature;
            if (minutes < MinimumMinutes || rise <= 0)
            {
                return;
            }

            if (!_samples.TryGetValue(entityId, out List<WarmUpSample>? samples))
            {
                samples = [];
                _samples[entityId] = samples;
            }

            samples.Add(new WarmUpSample(rise / (minutes / 60), run.StartedAt, minutes, run.StartTemperature, roomTemperature));
            if (samples.Count > SamplesKept)
            {
                samples.RemoveRange(0, samples.Count - SamplesKept);
            }

            _logger?.LogInformation(
                "{Valve} warmed its room {Rise:0.0}°C in {Minutes:0} minutes ({Rate:0.00}°C an hour); average of its last {Count} is {Average:0.00}°C an hour",
                entityId, rise, minutes, rise / (minutes / 60), samples.Count, samples.Average(s => s.DegreesPerHour));

            _store.Save(_samples.ToDictionary(pair => pair.Key, pair => pair.Value.ToList()));
        }
    }

    public double? RateFor(string entityId)
    {
        lock (_lock)
        {
            return _samples.TryGetValue(entityId, out List<WarmUpSample>? samples) && samples.Count > 0
                ? samples.Average(sample => sample.DegreesPerHour)
                : null;
        }
    }

    public int SampleCount(string entityId)
    {
        lock (_lock)
        {
            return _samples.TryGetValue(entityId, out List<WarmUpSample>? samples) ? samples.Count : 0;
        }
    }

    public int? MinutesToRise(string entityId, double degrees)
    {
        double? rate = RateFor(entityId);
        if (rate is not > 0)
        {
            return null;
        }

        return degrees <= 0 ? 0 : (int)Math.Ceiling(degrees / rate.Value * 60);
    }
}

/// <summary>Keeps the measured warm-ups in <c>warm-up-rates.json</c> in the app's local cache folder. Local only, like the boiler belief.</summary>
internal sealed class WarmUpRateStore : IWarmUpRateStore
{
    private const string _storageFileName = "warm-up-rates.json";
    private readonly ILogger<WarmUpRateStore> _logger;
    private readonly string _path;

    public WarmUpRateStore(ILogger<WarmUpRateStore> logger)
    {
        _logger = logger;
        _path = Path.Combine(LocalStorageFolder.Resolve(), _storageFileName);
    }

    public Dictionary<string, List<WarmUpSample>> Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            return JsonSerializer.Deserialize<Dictionary<string, List<WarmUpSample>>>(File.ReadAllText(_path)) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading the radiator warm-up rates from {Path}", _path);
            return [];
        }
    }

    public Dictionary<string, List<WarmUpSample>> LoadSeed()
    {
        string seedPath = Path.Combine(AppContext.BaseDirectory, "warm-up-seed.json");
        try
        {
            if (!File.Exists(seedPath))
            {
                return [];
            }

            return JsonSerializer.Deserialize<Dictionary<string, List<WarmUpSample>>>(File.ReadAllText(seedPath)) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading the shipped warm-up estimates from {Path}", seedPath);
            return [];
        }
    }

    public void Save(Dictionary<string, List<WarmUpSample>> samples)
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(samples));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving the radiator warm-up rates to {Path}", _path);
        }
    }
}

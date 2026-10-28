using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace HomeAssistant.Services.Climate;

/// <summary>
/// Whether a shower may be warming the radiators. Running hot water can send hot water round the heating with the
/// boiler's heating off (owner, 2026-10-24; HOME-ASSISTANT.md gotcha 29): Bedroom 3's open radiator rose 1.0-2.5 °C
/// after 5 of 15 showers, peaking 38-89 minutes after the bathroom's humidity began to rise. So a radiator warming soon
/// after a shower starts is no proof that the boiler is running, or still running after a turn-off.
/// </summary>
internal interface IShowerDetector
{
    /// <summary>
    /// When the latest shower that may still be warming the radiators began (within the last
    /// <see cref="ShowerDetector.WarmsRadiatorsFor"/>), or null if none did, or the bathroom's history cannot be read.
    /// </summary>
    Task<DateTimeOffset?> ShowerWarmingRadiatorsAsync(DateTimeOffset now);
}

/// <summary>
/// Reads the bathroom's humidity from Home Assistant's history when asked, which is only when a boiler check would
/// otherwise be decided by a radiator warming. Nothing is kept, so it knows about a shower that began before a restart.
/// </summary>
internal sealed class ShowerDetector : IShowerDetector
{
    internal const string BathroomHumidityEntityId = "sensor.bathroom_humidity";

    // A shower, as measured for gotcha 29: the bathroom's humidity up this many points within RiseWithin.
    internal const double HumidityRise = 8.0;
    internal static readonly TimeSpan RiseWithin = TimeSpan.FromMinutes(20);

    // From when the humidity began to rise. The latest a radiator was seen to peak was 89 minutes after.
    internal static readonly TimeSpan WarmsRadiatorsFor = TimeSpan.FromMinutes(90);

    // The heating pass waits for this, so a slow Home Assistant must not hold it up for the HTTP client's 100 s.
    private static readonly TimeSpan HistoryTimeout = TimeSpan.FromSeconds(10);

    private readonly HistoryService _history;
    private readonly ILogger<ShowerDetector> _logger;

    public ShowerDetector(HistoryService history, ILogger<ShowerDetector> logger)
    {
        _history = history;
        _logger = logger;
    }

    public async Task<DateTimeOffset?> ShowerWarmingRadiatorsAsync(DateTimeOffset now)
    {
        try
        {
            // The text history, not the numeric one, which reads "unavailable" as 0 and its return as a rise of 70 points.
            IReadOnlyList<HistoryTextEntry> history = await _history.GetEntityTextHistory(
                BathroomHumidityEntityId, (now - WarmsRadiatorsFor - RiseWithin).UtcDateTime, now.UtcDateTime).WaitAsync(HistoryTimeout);
            List<(DateTimeOffset At, double Humidity)> readings = [];
            foreach (HistoryTextEntry entry in history)
            {
                if (double.TryParse(entry.State, NumberStyles.Float, CultureInfo.InvariantCulture, out double humidity))
                {
                    readings.Add(((DateTimeOffset)entry.LastChanged, humidity));
                }
            }

            return LatestShowerStart(readings, now);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the bathroom's humidity, so the boiler check carries on as if nobody had showered");
            return null;
        }
    }

    /// <summary>
    /// The latest time, no earlier than <see cref="WarmsRadiatorsFor"/> before <paramref name="now"/>, that the humidity
    /// began a rise of <see cref="HumidityRise"/> points within <see cref="RiseWithin"/>; null if there was none.
    /// <paramref name="readings"/> are the sensor's changes, oldest first, each in force until the next.
    /// </summary>
    internal static DateTimeOffset? LatestShowerStart(IReadOnlyList<(DateTimeOffset At, double Humidity)> readings, DateTimeOffset now)
    {
        DateTimeOffset? latest = null;
        for (int peak = 1; peak < readings.Count; peak++)
        {
            // The lowest level in force at any time in the RiseWithin before this reading, including the one that was in
            // force when that span began.
            DateTimeOffset spanStart = readings[peak].At - RiseWithin;
            int low = -1;
            for (int i = 0; i < peak; i++)
            {
                bool inForceDuringSpan = readings[i].At >= spanStart || (i + 1 < readings.Count && readings[i + 1].At > spanStart);
                if (inForceDuringSpan && (low < 0 || readings[i].Humidity <= readings[low].Humidity))
                {
                    low = i;
                }
            }

            if (low < 0 || readings[peak].Humidity - readings[low].Humidity < HumidityRise)
            {
                continue;
            }

            // It began to rise when it left that low.
            DateTimeOffset began = readings[low + 1].At;
            if (began >= now - WarmsRadiatorsFor && (latest == null || began > latest))
            {
                latest = began;
            }
        }

        return latest;
    }
}

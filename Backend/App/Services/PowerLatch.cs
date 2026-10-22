using System.Threading;

namespace HomeAssistant.Services;

/// <summary>
/// Whether something on a power sensor is on, with two thresholds: on once a reading reaches <c>onAtWatts</c>, off
/// once one is at or below <c>offAtWatts</c>, and anything between leaves it as it was. Fed by live readings
/// (<see cref="Observe"/> on every change), never the history API (HOME-ASSISTANT.md, gotcha 21).
/// <para>
/// Made for the den TV (owner, 2026-10-22): watching YouTube it draws anywhere from 73 to 160 W, so a single 100 W
/// threshold flipped on and off every few seconds, while at night the plug draws 35–80 W on its own, so a single low
/// threshold made the den look occupied. A TV switched off drops to about 4 W.
/// </para>
/// <para>
/// It starts off. After an app restart with the TV between the thresholds, it counts as off until a reading reaches
/// <c>onAtWatts</c>, which while watching is a matter of seconds.
/// </para>
/// </summary>
internal sealed class PowerLatch
{
    private readonly Func<double?> _reading;
    private readonly double _onAtWatts;
    private readonly double _offAtWatts;
    private readonly TimeProvider _clock;
    private readonly object _lock = new();
    private bool _on;
    private DateTimeOffset? _wentOffAt;

    public PowerLatch(Func<double?> reading, double onAtWatts, double offAtWatts, TimeProvider clock)
    {
        _reading = reading;
        _onAtWatts = onAtWatts;
        _offAtWatts = offAtWatts;
        _clock = clock;
        Observe();
    }

    /// <summary>Reads the sensor now and switches if a threshold is crossed. An unreadable reading changes nothing.</summary>
    public void Observe()
    {
        double? watts;
        try
        {
            watts = _reading();
        }
        catch
        {
            watts = null;
        }

        if (watts is not double reading)
            return;

        lock (_lock)
        {
            if (!_on && reading >= _onAtWatts)
            {
                _on = true;
            }
            else if (_on && reading <= _offAtWatts)
            {
                _on = false;
                _wentOffAt = _clock.GetUtcNow();
            }
        }
    }

    public bool IsOn
    {
        get
        {
            Observe();
            lock (_lock)
            {
                return _on;
            }
        }
    }

    /// <summary>True while on, or if it went off no longer ago than <paramref name="hold"/>.</summary>
    public bool OnWithin(TimeSpan hold)
    {
        Observe();
        lock (_lock)
        {
            return _on || (_wentOffAt is DateTimeOffset off && _clock.GetUtcNow() - off <= hold);
        }
    }
}

using System.Threading;

namespace HomeAssistant.Services;

/// <summary>
/// Answers "has this been true, without a break, for the last N seconds?" from live readings.
/// <para>
/// Do not answer that from Home Assistant's history API. It runs behind live state by the recorder's commit
/// interval (measured 2026-10-20: the newest row was 4–6 s old about half the time), so a short window cannot
/// see its own last few seconds. A reading that is pulsing around a threshold then looks steady and a heater flaps
/// (HOME-ASSISTANT.md, gotcha 21). This class instead notes the moment the condition last became true.
/// </para>
/// <para>
/// The condition is a function over live entity state, so it can combine several entities (a plug that is on
/// and drawing power). Call <see cref="Observe"/> whenever any of them changes so that a short break is never
/// missed; <see cref="HeldFor"/> also re-reads the condition itself, so the answer never depends on whether a
/// change notification has been handled yet. Time comes from the <see cref="TimeProvider"/>, so tests can drive it.
/// </para>
/// <para>
/// A condition already true when this object is created is counted from then, not from when it really began,
/// so the first <c>HeldFor</c> answer after the app starts is conservative.
/// </para>
/// </summary>
internal sealed class SustainedCondition
{
    private readonly Func<bool> _condition;
    private readonly TimeProvider _clock;
    private readonly object _lock = new();
    private bool _holds;
    private DateTimeOffset _since;

    public SustainedCondition(Func<bool> condition, TimeProvider clock)
    {
        _condition = condition;
        _clock = clock;
        Observe();
    }

    /// <summary>Reads the condition now and notes when it changes. Cheap; safe to call on every state change.</summary>
    public void Observe()
    {
        bool holds;
        try
        {
            holds = _condition();
        }
        catch
        {
            // An entity that cannot be read is not evidence that the condition holds.
            holds = false;
        }

        lock (_lock)
        {
            // Not set on the first call when it is false: _since is only read while _holds is true.
            if (holds && !_holds)
            {
                _since = _clock.GetUtcNow();
            }

            _holds = holds;
        }
    }

    /// <summary>True while the condition holds now and has held, unbroken, for at least <paramref name="duration"/>.</summary>
    public bool HeldFor(TimeSpan duration)
    {
        Observe();
        lock (_lock)
        {
            return _holds && _clock.GetUtcNow() - _since >= duration;
        }
    }
}

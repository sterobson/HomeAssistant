namespace HomeAssistant.Services;

/// <summary>
/// A <see cref="SustainedCondition"/> with a hold: it comes on once the condition has been true, unbroken, for
/// <c>onAfter</c>, and then stays on until the condition has been false, unbroken, for <c>offAfter</c>. A break
/// shorter than that is bridged. Until it has come on, any break restarts the count, so a load pulsing around a
/// threshold never brings it on in the first place.
/// <para>
/// Made for Ruth's desk (owner, 2026-10-22): her equipment draws a surge when the plug is switched on, then dips under
/// 30 W for a reading or two (and once pulsed 25 W ↔ 400–570 W for 30 s) before it settles, and with no hold her heater
/// went on, off and on again within half a minute (HOME-ASSISTANT.md, gotcha 21).
/// </para>
/// <para>
/// Like <see cref="SustainedCondition"/> it works from live readings: call <see cref="Observe"/> on every change to the
/// entities the condition reads, so that neither a run nor a break is missed. Whether it has come on is settled at
/// each change, so a run long enough to count does so even if nobody asked during it. A condition already true when
/// this object is created is counted from then.
/// </para>
/// </summary>
internal sealed class LatchedCondition
{
    private readonly Func<bool> _condition;
    private readonly TimeSpan _onAfter;
    private readonly TimeSpan _offAfter;
    private readonly TimeProvider _clock;
    private readonly object _lock = new();
    private bool _holds;
    private DateTimeOffset _holdsSince;
    private DateTimeOffset _brokenSince;
    private bool _on;

    public LatchedCondition(Func<bool> condition, TimeSpan onAfter, TimeSpan offAfter, TimeProvider clock)
    {
        _condition = condition;
        _onAfter = onAfter;
        _offAfter = offAfter;
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
            DateTimeOffset now = _clock.GetUtcNow();

            // Settle what the time since the last change has done, before the new reading starts a new period.
            if (_holds && !_on && now - _holdsSince >= _onAfter)
            {
                _on = true;
            }
            else if (!_holds && _on && now - _brokenSince >= _offAfter)
            {
                _on = false;
            }

            if (holds && !_holds)
            {
                _holdsSince = now;
            }
            else if (!holds && _holds)
            {
                _brokenSince = now;
            }

            _holds = holds;
        }
    }

    /// <summary>True once the condition has held for <c>onAfter</c>, until it has been broken for <c>offAfter</c>.</summary>
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
}

using System.Collections.Generic;
using System.Threading.Tasks;

namespace HomeAssistant.Services;

/// <summary>
/// Spaces out the commands the app sends to Zigbee devices (owner, 2026-11-13). The Zigbee network can be overloaded
/// by too many commands at once, and the heating has a lot of radiator valves: a pass that changes several devices
/// used to send every command in the same instant. Now each command goes at least <see cref="Spacing"/> after the
/// one before it.
///
/// <list type="bullet">
/// <item>The first command after a quiet spell goes at once.</item>
/// <item>A confirmed command (the boiler relay, a switch from the 3D view) goes to the front of the queue, still
/// <see cref="Spacing"/> after the last one sent. A heating pass switches the boiler before any device, so the boiler
/// is first in a pass, and does not wait behind valves a pass before it queued.</item>
/// <item>A fire-and-forget command (valves, heater plugs) waits its turn behind the others. The heating evaluates
/// again whenever a thermometer reports, and a device that has not moved yet is decided again each time, so a newer
/// command to the same entity <b>takes the place</b> of a waiting one, in the same position, instead of queueing a
/// second command to the same device.</item>
/// </list>
///
/// Only <c>climate</c> and <c>switch</c> commands to an entity are paced: the radiator valves, the heater plugs and
/// the boiler relay. Notifications and the inverter's commands (which do not use Zigbee) are not held back behind a
/// queue of valves.
/// </summary>
internal sealed class CommandPacer
{
    /// <summary>The least time between two paced commands.</summary>
    internal static readonly TimeSpan Spacing = TimeSpan.FromSeconds(3);

    private static readonly HashSet<string> PacedDomains = new(StringComparer.OrdinalIgnoreCase) { "climate", "switch" };

    private readonly TimeSpan _spacing;
    private readonly Func<TimeSpan, Task> _delay;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _lock = new();
    private readonly List<Waiting> _queue = [];
    private DateTimeOffset _lastSent = DateTimeOffset.MinValue;
    private bool _draining;

    private sealed class Waiting(string? key, bool replaceable, bool priority)
    {
        public string? Key { get; } = key;
        public bool Replaceable { get; } = replaceable;
        public bool Priority { get; } = priority;
        public TaskCompletionSource<bool> Turn { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <param name="spacing">The least time between commands.</param>
    /// <param name="delay">Waits that long.</param>
    /// <param name="now">The time now.</param>
    internal CommandPacer(TimeSpan spacing, Func<TimeSpan, Task> delay, Func<DateTimeOffset> now)
    {
        _spacing = spacing;
        _delay = delay;
        _now = now;
    }

    public CommandPacer(TimeProvider timeProvider)
        : this(Spacing, delay => Task.Delay(delay, timeProvider), timeProvider.GetUtcNow)
    {
    }

    /// <summary>Whether commands like this one are held to the spacing.</summary>
    internal static bool IsPaced(ServiceCall call) =>
        PacedDomains.Contains(call.Domain) && call.SupersessionKey != null;

    /// <summary>
    /// Completes when it is this command's turn to be sent: true to send it, false if a newer command to the same
    /// entity has taken its place while it waited (that one sends instead). A command that is not paced completes
    /// at once.
    /// </summary>
    /// <param name="replaceable">A fire-and-forget command, which a newer one to the same entity replaces.</param>
    /// <param name="priority">A command that goes ahead of the waiting fire-and-forget ones.</param>
    public Task<bool> WaitForTurnAsync(ServiceCall call, bool replaceable, bool priority)
    {
        if (!IsPaced(call))
        {
            return Task.FromResult(true);
        }

        Waiting waiting = new(call.SupersessionKey, replaceable, priority);
        lock (_lock)
        {
            int existing = replaceable
                ? _queue.FindIndex(w => w.Replaceable && string.Equals(w.Key, waiting.Key, StringComparison.OrdinalIgnoreCase))
                : -1;
            if (existing >= 0)
            {
                _queue[existing].Turn.SetResult(false);
                _queue[existing] = waiting;
            }
            else if (priority)
            {
                int firstOrdinary = _queue.FindIndex(w => !w.Priority);
                _queue.Insert(firstOrdinary < 0 ? _queue.Count : firstOrdinary, waiting);
            }
            else
            {
                _queue.Add(waiting);
            }

            if (!_draining)
            {
                _draining = true;
                _ = DrainAsync();
            }
        }

        return waiting.Turn.Task;
    }

    private async Task DrainAsync()
    {
        while (true)
        {
            TimeSpan wait;
            lock (_lock)
            {
                if (_queue.Count == 0)
                {
                    _draining = false;
                    return;
                }

                wait = _lastSent + _spacing - _now();
            }

            if (wait > TimeSpan.Zero)
            {
                await _delay(wait);
            }

            Waiting next;
            lock (_lock)
            {
                // Whatever is at the front now, which may have been put there while this waited.
                next = _queue[0];
                _queue.RemoveAt(0);
                _lastSent = _now();
            }

            next.Turn.SetResult(true);
        }
    }
}

using HomeAssistant.Shared.Climate;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HomeAssistant.Services.Climate;

/// <summary>
/// Decides whether a heating device's rules allow it to run right now. Pure: the caller supplies the
/// local time and which presence sources are in use. A device runs only when its zone calls for heat
/// AND this returns true.
/// </summary>
internal static class DeviceRulesEvaluator
{
    /// <summary>No rules allow everything; otherwise the time windows AND the presence rule must both pass.</summary>
    public static bool Allows(DeviceRulesDto? rules, DateTime localNow, Func<string, bool> sourceInUse)
    {
        return rules == null || (IsWithinWindows(rules.TimeWindows, localNow) && IsPresenceSatisfied(rules.Presence, sourceInUse));
    }

    /// <summary>True when there are no windows, or <paramref name="localNow"/> falls in any of them.</summary>
    public static bool IsWithinWindows(IReadOnlyList<TimeWindowDto>? windows, DateTime localNow)
    {
        return windows == null || windows.Count == 0 || windows.Any(window => Contains(window, localNow));
    }

    /// <summary>True when no sources are named, or Any/All of them are in use.</summary>
    public static bool IsPresenceSatisfied(PresenceRuleDto? presence, Func<string, bool> sourceInUse)
    {
        if (presence == null || presence.Sources.Count == 0)
        {
            return true;
        }

        return presence.Match == PresenceMatch.All
            ? presence.Sources.All(sourceInUse)
            : presence.Sources.Any(sourceInUse);
    }

    /// <summary>
    /// A window covers [Start, End) on each of its days. When End is at or before Start it runs past
    /// midnight, and its days are the days it starts on: a Friday 22:00-06:00 window covers early
    /// Saturday too. A window with an unreadable time never matches.
    /// </summary>
    internal static bool Contains(TimeWindowDto window, DateTime localNow)
    {
        if (!TryParseTime(window.Start, out TimeOnly start) || !TryParseTime(window.End, out TimeOnly end))
        {
            return false;
        }

        TimeOnly now = TimeOnly.FromDateTime(localNow);
        if (start < end)
        {
            return StartsOn(window.Days, localNow.DayOfWeek) && now >= start && now < end;
        }

        // Past midnight: the part from Start belongs to today, the part before End to yesterday's window.
        if (now >= start)
        {
            return StartsOn(window.Days, localNow.DayOfWeek);
        }

        return now < end && StartsOn(window.Days, localNow.AddDays(-1).DayOfWeek);
    }

    internal static bool StartsOn(Days days, DayOfWeek dayOfWeek)
    {
        Days day = dayOfWeek switch
        {
            DayOfWeek.Monday => Days.Monday,
            DayOfWeek.Tuesday => Days.Tuesday,
            DayOfWeek.Wednesday => Days.Wednesday,
            DayOfWeek.Thursday => Days.Thursday,
            DayOfWeek.Friday => Days.Friday,
            DayOfWeek.Saturday => Days.Saturday,
            _ => Days.Sunday
        };
        return days.HasFlag(day);
    }

    private static bool TryParseTime(string? value, out TimeOnly time)
    {
        return TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }
}

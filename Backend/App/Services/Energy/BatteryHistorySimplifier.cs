using HomeAssistant.Services;
using System.Collections.Generic;

namespace HomeAssistant.Services.Energy;

/// <summary>
/// Simplifies battery history data by removing collinear points.
/// Duplicated from BatteryHistoryStorageService (Functions project) since the two projects
/// have different models and can't share a project reference.
/// </summary>
public static class BatteryHistorySimplifier
{
    /// <summary>
    /// Simplifies battery history by removing collinear intermediate points.
    /// Points near protected minutes (zone boundaries, price changes) are always kept
    /// to preserve detail around important transitions.
    /// </summary>
    /// <param name="points">Chronologically ordered battery history entries</param>
    /// <param name="tolerancePercent">Maximum deviation from interpolated line before a point is kept</param>
    /// <param name="protectedMinutes">Minutes-from-midnight (local time) where detail should be preserved</param>
    /// <param name="localMidnight">Local midnight for the day being simplified</param>
    /// <param name="localTimeZone">Local timezone for converting UTC timestamps to local minutes</param>
    /// <param name="protectionBufferMinutes">Buffer around each protected minute within which points are kept</param>
    public static List<(DateTimeOffset Timestamp, double BatteryPercent)> Simplify(
        IReadOnlyList<NumericHistoryEntry> points,
        double tolerancePercent = 1.0,
        IReadOnlySet<int>? protectedMinutes = null,
        DateTime? localMidnight = null,
        TimeZoneInfo? localTimeZone = null,
        int protectionBufferMinutes = 5)
    {
        if (points.Count <= 2)
        {
            List<(DateTimeOffset, double)> result = [];
            foreach (NumericHistoryEntry point in points)
            {
                result.Add((new DateTimeOffset(point.LastChanged, TimeSpan.Zero), point.State));
            }
            return result;
        }

        List<(DateTimeOffset Timestamp, double BatteryPercent)> simplified = [
            (new DateTimeOffset(points[0].LastChanged, TimeSpan.Zero), points[0].State)
        ];

        for (int i = 1; i < points.Count - 1; i++)
        {
            (DateTimeOffset lastKeptTime, double lastKeptValue) = simplified[^1];
            DateTimeOffset currentTime = new(points[i].LastChanged, TimeSpan.Zero);
            double currentValue = points[i].State;
            DateTimeOffset nextTime = new(points[i + 1].LastChanged, TimeSpan.Zero);
            double nextValue = points[i + 1].State;

            // Always keep points near protected times (zone boundaries, price changes)
            if (protectedMinutes != null && localMidnight.HasValue && localTimeZone != null)
            {
                if (IsNearProtectedTime(currentTime, protectedMinutes, localMidnight.Value, localTimeZone, protectionBufferMinutes))
                {
                    simplified.Add((currentTime, currentValue));
                    continue;
                }
            }

            double totalSeconds = (nextTime - lastKeptTime).TotalSeconds;
            double currentSeconds = (currentTime - lastKeptTime).TotalSeconds;

            if (totalSeconds <= 0)
            {
                simplified.Add((currentTime, currentValue));
                continue;
            }

            double ratio = currentSeconds / totalSeconds;
            double interpolated = lastKeptValue + (nextValue - lastKeptValue) * ratio;

            if (Math.Abs(currentValue - interpolated) > tolerancePercent)
            {
                simplified.Add((currentTime, currentValue));
            }
        }

        NumericHistoryEntry last = points[^1];
        simplified.Add((new DateTimeOffset(last.LastChanged, TimeSpan.Zero), last.State));
        return simplified;
    }

    private static bool IsNearProtectedTime(
        DateTimeOffset pointTime,
        IReadOnlySet<int> protectedMinutes,
        DateTime localMidnight,
        TimeZoneInfo localTimeZone,
        int bufferMinutes)
    {
        DateTime pointUtc = pointTime.UtcDateTime;
        DateTime pointLocal = TimeZoneInfo.ConvertTimeFromUtc(pointUtc, localTimeZone);
        int pointMinute = (int)(pointLocal - localMidnight).TotalMinutes;

        foreach (int protectedMinute in protectedMinutes)
        {
            if (Math.Abs(pointMinute - protectedMinute) <= bufferMinutes)
            {
                return true;
            }
        }

        return false;
    }
}

using HomeAssistant.Functions.Models;
using HomeAssistant.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Functions;

public class DailyHistorySimplifyFunction
{
    private readonly ILogger<DailyHistorySimplifyFunction> _logger;
    private readonly BatteryHistoryStorageService _historyService;

    // Hardcoded for now — could be extended to query distinct houses from the table
    private const string DefaultHouseId = "blount";

    public DailyHistorySimplifyFunction(ILogger<DailyHistorySimplifyFunction> logger)
    {
        _logger = logger;

        string connectionString = Environment.GetEnvironmentVariable("ScheduleStorageConnectionString")
            ?? throw new InvalidOperationException("ScheduleStorageConnectionString not configured");

        _historyService = new BatteryHistoryStorageService(connectionString, logger);
    }

    /// <summary>
    /// Runs daily at 1:30 AM UTC. Simplifies battery history from 7 days ago by removing
    /// collinear points that lie within a 1% tolerance of the interpolated line,
    /// while ensuring at least one point per hour is retained.
    /// The last 7 days are kept at full resolution (the app backfills unsimplified data on startup).
    /// </summary>
    [Function("DailyHistorySimplify")]
    public async Task Run(
        [TimerTrigger("0 30 1 * * *")] TimerInfo timerInfo)
    {
        string targetDate = DateTime.UtcNow.AddDays(-7).ToString("yyyy-MM-dd");
        _logger.LogInformation("Starting daily history simplification for {Date}", targetDate);

        try
        {
            List<BatteryHistoryPoint> originalPoints = await _historyService.GetHistoryAsync(DefaultHouseId, targetDate);

            if (originalPoints.Count <= 2)
            {
                _logger.LogInformation("Simplified {Date}: {Count} points, nothing to simplify", targetDate, originalPoints.Count);
                return;
            }

            List<BatteryHistoryPoint> simplifiedPoints = BatteryHistoryStorageService.SimplifyHistory(originalPoints, tolerancePercent: 1.0);

            // Ensure at least one point per hour is kept
            simplifiedPoints = EnsureHourlyPoints(originalPoints, simplifiedPoints);

            // Find which points were removed
            HashSet<string> keptRowKeys = new(simplifiedPoints.Select(p => p.RowKey));
            List<BatteryHistoryPoint> removedPoints = originalPoints
                .Where(p => !keptRowKeys.Contains(p.RowKey))
                .ToList();

            if (removedPoints.Count > 0)
            {
                await _historyService.DeletePointsAsync(removedPoints);
            }

            _logger.LogInformation("Simplified {Date}: {Original} → {Simplified} points ({Removed} removed)",
                targetDate, originalPoints.Count, simplifiedPoints.Count, removedPoints.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error simplifying history for {Date}", targetDate);
        }
    }

    /// <summary>
    /// Ensures at least one point exists in each clock hour (00:xx, 01:xx, ..., 23:xx).
    /// For any hour missing from the simplified set, picks the point from the original set
    /// closest to the middle of that hour.
    /// </summary>
    private static List<BatteryHistoryPoint> EnsureHourlyPoints(
        List<BatteryHistoryPoint> originalPoints,
        List<BatteryHistoryPoint> simplifiedPoints)
    {
        HashSet<string> keptRowKeys = new(simplifiedPoints.Select(p => p.RowKey));

        // Group original points by hour
        Dictionary<int, List<BatteryHistoryPoint>> pointsByHour = [];
        foreach (BatteryHistoryPoint point in originalPoints)
        {
            int hour = point.RecordedAt.Hour;
            if (!pointsByHour.ContainsKey(hour))
                pointsByHour[hour] = [];
            pointsByHour[hour].Add(point);
        }

        // Check which hours are missing from the simplified set
        HashSet<int> coveredHours = [];
        foreach (BatteryHistoryPoint point in simplifiedPoints)
        {
            coveredHours.Add(point.RecordedAt.Hour);
        }

        // For each missing hour, add the point closest to the middle of the hour
        foreach (KeyValuePair<int, List<BatteryHistoryPoint>> entry in pointsByHour)
        {
            if (coveredHours.Contains(entry.Key))
                continue;

            int midMinute = 30;
            BatteryHistoryPoint? best = null;
            int bestDistance = int.MaxValue;

            foreach (BatteryHistoryPoint point in entry.Value)
            {
                int distance = Math.Abs(point.RecordedAt.Minute - midMinute);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = point;
                }
            }

            if (best != null && !keptRowKeys.Contains(best.RowKey))
            {
                simplifiedPoints.Add(best);
                keptRowKeys.Add(best.RowKey);
            }
        }

        // Re-sort by recorded time
        simplifiedPoints.Sort((a, b) => a.RecordedAt.CompareTo(b.RecordedAt));
        return simplifiedPoints;
    }
}

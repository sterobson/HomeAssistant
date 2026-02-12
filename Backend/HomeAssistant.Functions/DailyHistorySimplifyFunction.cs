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
    /// Runs daily at 1:30 AM UTC. Simplifies yesterday's battery history by removing
    /// collinear points that lie within a 1% tolerance of the interpolated line.
    /// </summary>
    [Function("DailyHistorySimplify")]
    public async Task Run(
        [TimerTrigger("0 30 1 * * *")] TimerInfo timerInfo)
    {
        string yesterday = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd");
        _logger.LogInformation("Starting daily history simplification for {Date}", yesterday);

        try
        {
            List<BatteryHistoryPoint> originalPoints = await _historyService.GetHistoryAsync(DefaultHouseId, yesterday);

            if (originalPoints.Count <= 2)
            {
                _logger.LogInformation("Simplified {Date}: {Count} points, nothing to simplify", yesterday, originalPoints.Count);
                return;
            }

            List<BatteryHistoryPoint> simplifiedPoints = BatteryHistoryStorageService.SimplifyHistory(originalPoints, tolerancePercent: 1.0);

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
                yesterday, originalPoints.Count, simplifiedPoints.Count, removedPoints.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error simplifying history for {Date}", yesterday);
        }
    }
}

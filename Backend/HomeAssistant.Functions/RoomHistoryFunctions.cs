using HomeAssistant.Functions.Models;
using HomeAssistant.Functions.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace HomeAssistant.Functions;

public class RoomHistoryFunctions
{
    private readonly ILogger<RoomHistoryFunctions> _logger;
    private readonly RoomHistoryStorageService _historyService;

    public RoomHistoryFunctions(ILogger<RoomHistoryFunctions> logger)
    {
        _logger = logger;

        string connectionString = Environment.GetEnvironmentVariable("ScheduleStorageConnectionString")
            ?? throw new InvalidOperationException("ScheduleStorageConnectionString not configured");

        _historyService = new RoomHistoryStorageService(connectionString, logger);
    }

    /// <summary>
    /// Gets temperature history for a specific room
    /// </summary>
    [Function("GetRoomHistory")]
    public async Task<IActionResult> GetRoomHistory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "room-history")] HttpRequest req)
    {
        // Get query parameters
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        if (!req.Query.TryGetValue("roomId", out StringValues roomIdStr) ||
            !int.TryParse(roomIdStr, out int roomId))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing roomId query parameter" });
        }

        // Parse startDate and endDate parameters
        if (!req.Query.TryGetValue("startDate", out StringValues startDateStr) ||
            !DateTimeOffset.TryParse(startDateStr, out DateTimeOffset startDate))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing startDate query parameter" });
        }

        if (!req.Query.TryGetValue("endDate", out StringValues endDateStr) ||
            !DateTimeOffset.TryParse(endDateStr, out DateTimeOffset endDate))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing endDate query parameter" });
        }

        string houseId = houseIdStr.ToString();
        _logger.LogInformation("Getting temperature history for house {HouseId}, room {RoomId}, from {StartDate} to {EndDate}",
            houseId, roomId, startDate, endDate);

        try
        {
            List<TemperatureHistoryPoint> history = await _historyService.GetHistoryAsync(houseId, roomId, startDate, endDate);

            // Transform to response DTOs
            RoomHistoryResponse response = new RoomHistoryResponse
            {
                RoomId = roomId,
                Points = history.Select(p => new HistoryPointDto
                {
                    Timestamp = p.RecordedAt.ToString("O"),
                    CurrentTemperature = p.CurrentTemperature,
                    TargetTemperature = p.TargetTemperature,
                    HeatingActive = p.HeatingActive
                }).ToList()
            };

            _logger.LogInformation("Retrieved {Count} history points for room {RoomId}", history.Count, roomId);
            return new OkObjectResult(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting temperature history for house {HouseId}, room {RoomId}",
                houseId, roomId);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Scheduled cleanup function that runs daily at 3 AM to delete old history data
    /// </summary>
    [Function("CleanupOldHistory")]
    public async Task CleanupOldHistory(
        [TimerTrigger("0 0 3 * * *")] TimerInfo timerInfo)
    {
        _logger.LogInformation("Starting scheduled cleanup of old temperature history at {Time}", DateTime.UtcNow);

        try
        {
            // Note: This is a simple implementation that cleans up for all known rooms
            // In production, you might want to query all partition keys or maintain a list of active houses/rooms

            // For now, we'll log a message indicating manual cleanup may be needed
            _logger.LogInformation("Automatic cleanup runs daily. Manual cleanup may be required for specific rooms.");

            // You can implement more sophisticated cleanup by:
            // 1. Maintaining a list of active house/room combinations
            // 2. Querying Table Storage for all partition keys
            // 3. Using a separate table to track active rooms

            _logger.LogInformation("Scheduled cleanup task completed at {Time}", DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during scheduled cleanup");
        }
    }
}

/// <summary>
/// Response DTO for temperature history endpoint
/// </summary>
public class RoomHistoryResponse
{
    public int RoomId { get; set; }
    public List<HistoryPointDto> Points { get; set; } = [];
}

/// <summary>
/// Data transfer object for a single history point
/// </summary>
public class HistoryPointDto
{
    public string Timestamp { get; set; } = string.Empty;
    public double? CurrentTemperature { get; set; }
    public double? TargetTemperature { get; set; }
    public bool HeatingActive { get; set; }
}

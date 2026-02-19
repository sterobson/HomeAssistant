using HomeAssistant.Functions.JsonConverters;
using HomeAssistant.Functions.Models;
using HomeAssistant.Functions.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System.Text.Json;

namespace HomeAssistant.Functions;

public class BatteryHistoryFunctions
{
    private readonly ILogger<BatteryHistoryFunctions> _logger;
    private readonly BatteryHistoryStorageService _historyService;
    private readonly SignalRService _signalRService;

    public BatteryHistoryFunctions(ILogger<BatteryHistoryFunctions> logger, SignalRService signalRService)
    {
        _logger = logger;
        _signalRService = signalRService;

        string connectionString = Environment.GetEnvironmentVariable("ScheduleStorageConnectionString")
            ?? throw new InvalidOperationException("ScheduleStorageConnectionString not configured");

        _historyService = new BatteryHistoryStorageService(connectionString, logger);
    }

    [Function("GetBatteryHistory")]
    public async Task<IActionResult> GetBatteryHistory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "battery-history")] HttpRequest req)
    {
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        if (!req.Query.TryGetValue("date", out StringValues dateStr) ||
            string.IsNullOrWhiteSpace(dateStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing date query parameter" });
        }

        string houseId = houseIdStr.ToString();
        string date = dateStr.ToString();
        _logger.LogInformation("Getting battery history for house {HouseId} on {Date}", houseId, date);

        try
        {
            List<BatteryHistoryPoint> history = await _historyService.GetHistoryAsync(houseId, date);

            // Fetch boundary points from adjacent days for chart interpolation
            DateOnly parsedDate = DateOnly.ParseExact(date, "yyyy-MM-dd");
            string prevDate = parsedDate.AddDays(-1).ToString("yyyy-MM-dd");
            string nextDate = parsedDate.AddDays(1).ToString("yyyy-MM-dd");

            Task<BatteryHistoryPoint?> prevTask = _historyService.GetLastPointAsync(houseId, prevDate);
            Task<BatteryHistoryPoint?> nextTask = _historyService.GetFirstPointAsync(houseId, nextDate);
            await Task.WhenAll(prevTask, nextTask);

            BatteryHistoryPoint? prevDayLastPoint = await prevTask;
            BatteryHistoryPoint? nextDayFirstPoint = await nextTask;

            BatteryHistoryResponse response = new BatteryHistoryResponse
            {
                Points = history.Select(p => new BatteryHistoryPointDto
                {
                    Timestamp = p.RecordedAt.ToString("O"),
                    BatteryPercent = p.BatteryPercent,
                    PowerWatts = p.PowerWatts
                }).ToList(),
                PreviousDayLastPoint = prevDayLastPoint != null ? new BatteryHistoryPointDto
                {
                    Timestamp = prevDayLastPoint.RecordedAt.ToString("O"),
                    BatteryPercent = prevDayLastPoint.BatteryPercent,
                    PowerWatts = prevDayLastPoint.PowerWatts
                } : null,
                NextDayFirstPoint = nextDayFirstPoint != null ? new BatteryHistoryPointDto
                {
                    Timestamp = nextDayFirstPoint.RecordedAt.ToString("O"),
                    BatteryPercent = nextDayFirstPoint.BatteryPercent,
                    PowerWatts = nextDayFirstPoint.PowerWatts
                } : null
            };

            _logger.LogInformation("Retrieved {Count} battery history points for house {HouseId} on {Date}",
                history.Count, houseId, date);
            return new OkObjectResult(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting battery history for house {HouseId} on {Date}", houseId, date);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    [Function("SetBatteryState")]
    public async Task<IActionResult> SetBatteryState(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "battery-state")] HttpRequest req)
    {
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        string houseId = houseIdStr.ToString();

        try
        {
            using StreamReader reader = new(req.Body);
            string body = await reader.ReadToEndAsync();

            BatteryStateRequest? stateRequest = JsonSerializer.Deserialize<BatteryStateRequest>(body, JsonConfiguration.CreateOptions());

            if (stateRequest == null)
            {
                return new BadRequestObjectResult(new { error = "Invalid request body" });
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;

            await _historyService.SaveHistoryPointAsync(
                houseId,
                stateRequest.Date,
                stateRequest.BatteryPercent,
                stateRequest.PowerWatts,
                now
            );

            _logger.LogDebug("Recorded battery state for house {HouseId}", houseId);

            // Send SignalR notification
            try
            {
                await _signalRService.SendMessageToGroupAsync($"house-{houseId}", "battery-state-changed", new
                {
                    houseId,
                    batteryPercent = stateRequest.BatteryPercent,
                    powerWatts = stateRequest.PowerWatts
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send SignalR message for battery-state-changed to house {HouseId}", houseId);
            }

            return new OkObjectResult(new { success = true, recorded = true });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error parsing JSON for house {HouseId}", houseId);
            return new BadRequestObjectResult(new { error = "Invalid JSON format" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting battery state for house {HouseId}", houseId);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    [Function("RequestBatteryHistoryBackfill")]
    public async Task<IActionResult> RequestBatteryHistoryBackfill(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "battery-history-backfill")] HttpRequest req)
    {
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        if (!req.Query.TryGetValue("date", out StringValues dateStr) ||
            string.IsNullOrWhiteSpace(dateStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing date query parameter" });
        }

        string houseId = houseIdStr.ToString();
        string date = dateStr.ToString();

        _logger.LogInformation("Requesting battery history backfill for house {HouseId} on {Date}", houseId, date);

        try
        {
            await _signalRService.SendMessageToGroupAsync($"house-{houseId}", "backfill-battery-history", new
            {
                houseId,
                date
            });

            return new AcceptedResult(string.Empty, new { success = true, message = "Backfill request sent" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error requesting battery history backfill for house {HouseId} on {Date}", houseId, date);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    [Function("ReplaceBatteryHistory")]
    public async Task<IActionResult> ReplaceBatteryHistory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "battery-history-replace")] HttpRequest req)
    {
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        if (!req.Query.TryGetValue("date", out StringValues dateStr) ||
            string.IsNullOrWhiteSpace(dateStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing date query parameter" });
        }

        string houseId = houseIdStr.ToString();
        string date = dateStr.ToString();

        _logger.LogInformation("Replacing battery history for house {HouseId} on {Date}", houseId, date);

        try
        {
            using StreamReader reader = new(req.Body);
            string body = await reader.ReadToEndAsync();

            List<BatteryHistoryReplacePoint>? points = JsonSerializer.Deserialize<List<BatteryHistoryReplacePoint>>(
                body, JsonConfiguration.CreateOptions());

            if (points == null || points.Count == 0)
            {
                return new BadRequestObjectResult(new { error = "Invalid or empty request body" });
            }

            List<BatteryHistoryPoint> historyPoints = points.Select(p => new BatteryHistoryPoint
            {
                BatteryPercent = p.BatteryPercent,
                RecordedAt = p.RecordedAtUtc
            }).ToList();

            await _historyService.ReplaceHistoryAsync(houseId, date, historyPoints);

            // Broadcast replacement notification
            try
            {
                await _signalRService.SendMessageToGroupAsync($"house-{houseId}", "battery-history-replaced", new
                {
                    houseId,
                    date
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send SignalR message for battery-history-replaced to house {HouseId}", houseId);
            }

            return new OkObjectResult(new { success = true, pointCount = historyPoints.Count });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error parsing JSON for battery history replace, house {HouseId}", houseId);
            return new BadRequestObjectResult(new { error = "Invalid JSON format" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error replacing battery history for house {HouseId} on {Date}", houseId, date);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

}

public class BatteryStateRequest
{
    public double BatteryPercent { get; set; }
    public double? PowerWatts { get; set; }
    public string Date { get; set; } = string.Empty;
}

public class BatteryHistoryResponse
{
    public List<BatteryHistoryPointDto> Points { get; set; } = [];
    public BatteryHistoryPointDto? PreviousDayLastPoint { get; set; }
    public BatteryHistoryPointDto? NextDayFirstPoint { get; set; }
}

public class BatteryHistoryPointDto
{
    public string Timestamp { get; set; } = string.Empty;
    public double BatteryPercent { get; set; }
    public double? PowerWatts { get; set; }
}

public class BatteryHistoryReplacePoint
{
    public double BatteryPercent { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
}

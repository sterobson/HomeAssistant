using HomeAssistant.Functions.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace HomeAssistant.Functions;

/// <summary>
/// The boiler's on/off history, as the NetDaemon app believed it (owner, 2026-10-21). Written by
/// <see cref="RoomStateFunctions"/> from each room-state upload; read here for the heating page's chart.
/// </summary>
public class BoilerHistoryFunctions
{
    private readonly ILogger<BoilerHistoryFunctions> _logger;
    private readonly BoilerHistoryStorageService _storageService;

    public BoilerHistoryFunctions(ILogger<BoilerHistoryFunctions> logger)
    {
        _logger = logger;

        string connectionString = Environment.GetEnvironmentVariable("ScheduleStorageConnectionString")
            ?? throw new InvalidOperationException("ScheduleStorageConnectionString not configured");

        _storageService = new BoilerHistoryStorageService(connectionString, logger);
    }

    /// <summary>GET /api/boiler-history?houseId=...&amp;hours=48 (1 to 744; default 48).</summary>
    [Function("GetBoilerHistory")]
    public async Task<IActionResult> GetBoilerHistory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "boiler-history")] HttpRequest req)
    {
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) || string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        int hours = 48;
        if (req.Query.TryGetValue("hours", out StringValues hoursStr) && !int.TryParse(hoursStr, out hours))
        {
            return new BadRequestObjectResult(new { error = "hours must be a whole number" });
        }

        string houseId = houseIdStr.ToString();
        try
        {
            return new OkObjectResult(await _storageService.GetHistoryAsync(houseId, hours, DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting boiler history for house {HouseId}", houseId);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }
}

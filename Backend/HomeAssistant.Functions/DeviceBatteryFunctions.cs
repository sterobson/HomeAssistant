using HomeAssistant.Functions.Services;
using HomeAssistant.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System.Text.Json;

namespace HomeAssistant.Functions;

/// <summary>
/// Daily battery levels for the house's replaceable cells. The NetDaemon app pushes a
/// snapshot; the UI can read the history back.
/// </summary>
public class DeviceBatteryFunctions
{
    private const int DefaultDays = 30;
    private const int MaxDays = 365;

    private readonly ILogger<DeviceBatteryFunctions> _logger;
    private readonly DeviceBatteryStorageService _storageService;

    public DeviceBatteryFunctions(ILogger<DeviceBatteryFunctions> logger)
    {
        _logger = logger;

        string connectionString = Environment.GetEnvironmentVariable("ScheduleStorageConnectionString")
            ?? throw new InvalidOperationException("ScheduleStorageConnectionString not configured");

        _storageService = new DeviceBatteryStorageService(connectionString, logger);
    }

    /// <summary>
    /// Records a day's battery readings. Safe to call more than once a day — readings are
    /// keyed by device and date, so a repeat push updates that day rather than duplicating it.
    /// </summary>
    [Function("SetDeviceBatteries")]
    public async Task<IActionResult> SetDeviceBatteries(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "device-batteries")] HttpRequest req)
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

            DeviceBatterySnapshotDto? snapshot =
                JsonSerializer.Deserialize<DeviceBatterySnapshotDto>(body, JsonConfiguration.CreateOptions());

            if (snapshot == null)
            {
                return new BadRequestObjectResult(new { error = "Invalid request body" });
            }

            if (snapshot.RecordedAt == default)
            {
                snapshot.RecordedAt = DateTimeOffset.UtcNow;
            }

            await _storageService.SaveSnapshotAsync(houseId, snapshot);

            return new OkObjectResult(new { success = true, count = snapshot.Readings.Count });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Invalid JSON posting device batteries for house {HouseId}", houseId);
            return new BadRequestObjectResult(new { error = "Invalid JSON" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving device batteries for house {HouseId}", houseId);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Returns recent daily battery readings, newest day first.
    /// </summary>
    [Function("GetDeviceBatteries")]
    public async Task<IActionResult> GetDeviceBatteries(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "device-batteries")] HttpRequest req)
    {
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        string houseId = houseIdStr.ToString();

        int days = DefaultDays;
        if (req.Query.TryGetValue("days", out StringValues daysStr) && int.TryParse(daysStr, out int requestedDays))
        {
            days = Math.Clamp(requestedDays, 1, MaxDays);
        }

        try
        {
            DeviceBatteryHistoryDto history = await _storageService.GetHistoryAsync(houseId, days);
            return new OkObjectResult(history);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading device batteries for house {HouseId}", houseId);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }
}

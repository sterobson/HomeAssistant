using HomeAssistant.Functions.JsonConverters;
using HomeAssistant.Functions.Services;
using HomeAssistant.Shared.Climate;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System.Text.Json;

namespace HomeAssistant.Functions;

public class ScheduleFunctions
{
    private readonly ILogger<ScheduleFunctions> _logger;
    private readonly ScheduleStorageService _storageService;

    public ScheduleFunctions(ILogger<ScheduleFunctions> logger)
    {
        _logger = logger;

        string connectionString = Environment.GetEnvironmentVariable("ScheduleStorageConnectionString")
            ?? throw new InvalidOperationException("ScheduleStorageConnectionString not configured");

        _storageService = new ScheduleStorageService(connectionString);
    }

    [Function("GetSchedules")]
    public async Task<IActionResult> GetSchedules(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "schedules")] HttpRequest req)
    {
        // Get houseId from query parameter
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        string houseId = houseIdStr.ToString();
        _logger.LogInformation("Getting heating schedules for house {HouseId}", houseId);

        try
        {
            RoomSchedulesDto? schedules = await _storageService.GetSchedulesAsync(houseId);

            if (schedules == null)
            {
                // Return default schedules if none exist
                return new NotFoundResult();
            }

            return new OkObjectResult(schedules);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting schedules for house {HouseId}", houseId);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    [Function("SetSchedules")]
    public async Task<IActionResult> SetSchedules(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "schedules")] HttpRequest req)
    {
        // Get houseId from query parameter
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        string houseId = houseIdStr.ToString();
        _logger.LogInformation("Setting heating schedules for house {HouseId}", houseId);

        try
        {
            using StreamReader reader = new(req.Body);
            string body = await reader.ReadToEndAsync();

            RoomSchedulesDto? dto = JsonSerializer.Deserialize<RoomSchedulesDto>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new FlexibleEnumConverterFactory() }
            });

            if (dto == null)
            {
                return new BadRequestObjectResult(new { error = "Invalid request body" });
            }

            await _storageService.SaveSchedulesAsync(houseId, dto);
            _logger.LogInformation("Successfully saved schedules for house {HouseId} with {RoomCount} rooms",
                houseId, dto.Rooms.Count);

            return new OkObjectResult(new { success = true });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error parsing JSON for house {HouseId}", houseId);
            return new BadRequestObjectResult(new { error = "Invalid JSON format" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting schedules for house {HouseId}", houseId);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }
}

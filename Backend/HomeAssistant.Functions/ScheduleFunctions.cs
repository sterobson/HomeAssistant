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
    private readonly SignalRService _signalRService;

    public ScheduleFunctions(ILogger<ScheduleFunctions> logger, SignalRService signalRService)
    {
        _logger = logger;
        _signalRService = signalRService;

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

            RoomSchedulesDto? dto = JsonSerializer.Deserialize<RoomSchedulesDto>(body, JsonConfiguration.CreateOptions());

            if (dto == null)
            {
                return new BadRequestObjectResult(new { error = "Invalid request body" });
            }

            // Make sure every DTO has an ID
            EnsureIdsCorrect(dto.Rooms, room => room.Id, (room, newId) => room.Id = newId);
            foreach (RoomDto r in dto.Rooms)
            {
                EnsureIdsCorrect(r.Schedules, scheduleTrack => scheduleTrack.Id, (scheduleTrack, newId) => scheduleTrack.Id = newId);
            }

            await _storageService.SaveSchedulesAsync(houseId, dto);

            _logger.LogInformation("Successfully saved schedules for house {HouseId} with {RoomCount} rooms", houseId, dto.Rooms.Count);

            // Send SignalR message to all clients for this house
            try
            {
                _logger.LogInformation("About to send schedules-changed message to group house-{HouseId}", houseId);
                await _signalRService.SendMessageToGroupAsync($"house-{houseId}", "schedules-changed", new { houseId });
                _logger.LogInformation("Successfully sent schedules-changed message to group house-{HouseId}", houseId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send SignalR message for schedules-changed to house {HouseId}", houseId);
                // Don't fail the request if SignalR fails
            }

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

    private static void EnsureIdsCorrect<T>(List<T> source, Func<T, int> getId, Action<T, int> setId) where T : class
    {
        // Make sure every item has an ID.
        T? item;
        do
        {
            item = source.FirstOrDefault(r => getId(r) <= 0);
            if (item != null)
            {
                int newId = Math.Max(1, source.Max(r => getId(r)) + 1);
                setId(item, newId);
            }
        } while (item != null);

        // Make sure every DTO has a unique ID
        foreach (T r in source)
        {
            if (source.Any(d => getId(d) == getId(r) && d != r))
            {
                int newId = Math.Max(1, source.Max(r => getId(r)) + 1);
                setId(r, newId);
            }
        }
    }
}

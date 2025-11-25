using HomeAssistant.Functions.Models;
using HomeAssistant.Functions.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace HomeAssistant.Functions;

public class RoomStateFunctions
{
    private readonly ILogger<RoomStateFunctions> _logger;
    private readonly RoomStateStorageService _storageService;

    public RoomStateFunctions(ILogger<RoomStateFunctions> logger)
    {
        _logger = logger;

        var connectionString = Environment.GetEnvironmentVariable("ScheduleStorageConnectionString")
            ?? throw new InvalidOperationException("ScheduleStorageConnectionString not configured");

        _storageService = new RoomStateStorageService(connectionString);
    }

    [Function("GetRoomStates")]
    public async Task<IActionResult> GetRoomStates(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "room-states")] HttpRequest req)
    {
        // Get houseId from query parameter
        if (!req.Query.TryGetValue("houseId", out var houseIdStr) ||
            !Guid.TryParse(houseIdStr, out var houseId))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        _logger.LogInformation("Getting room states for house {HouseId}", houseId);

        try
        {
            var roomStates = await _storageService.GetRoomStatesAsync(houseId);

            if (roomStates == null)
            {
                // Return empty states if none exist
                _logger.LogInformation("No room states found for house {HouseId}, returning empty", houseId);
                return new OkObjectResult(new RoomStatesResponse { RoomStates = [] });
            }

            return new OkObjectResult(roomStates);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting room states for house {HouseId}", houseId);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    [Function("SetRoomStates")]
    public async Task<IActionResult> SetRoomStates(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "room-states")] HttpRequest req)
    {
        // Get houseId from query parameter
        if (!req.Query.TryGetValue("houseId", out var houseIdStr) ||
            !Guid.TryParse(houseIdStr, out var houseId))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        _logger.LogInformation("Setting room states for house {HouseId}", houseId);

        try
        {
            using StreamReader reader = new(req.Body);
            var body = await reader.ReadToEndAsync();

            RoomStatesResponse? dto = JsonSerializer.Deserialize<RoomStatesResponse>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (dto == null)
            {
                return new BadRequestObjectResult(new { error = "Invalid request body" });
            }

            await _storageService.SaveRoomStatesAsync(houseId, dto);
            _logger.LogInformation("Successfully saved room states for house {HouseId} with {StateCount} rooms",
                houseId, dto.RoomStates.Count);

            return new OkObjectResult(new { success = true });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error parsing JSON for house {HouseId}", houseId);
            return new BadRequestObjectResult(new { error = "Invalid JSON format" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting room states for house {HouseId}", houseId);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }
}

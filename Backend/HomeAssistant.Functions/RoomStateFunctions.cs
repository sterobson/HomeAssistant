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

public class RoomStateFunctions
{
    private readonly ILogger<RoomStateFunctions> _logger;
    private readonly RoomStateStorageService _storageService;
    private readonly SignalRService _signalRService;

    public RoomStateFunctions(ILogger<RoomStateFunctions> logger, SignalRService signalRService)
    {
        _logger = logger;
        _signalRService = signalRService;

        string connectionString = Environment.GetEnvironmentVariable("ScheduleStorageConnectionString")
            ?? throw new InvalidOperationException("ScheduleStorageConnectionString not configured");

        _storageService = new RoomStateStorageService(connectionString);
    }

    [Function("GetRoomStates")]
    public async Task<IActionResult> GetRoomStates(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "room-states")] HttpRequest req)
    {
        // Get houseId from query parameter
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        string houseId = houseIdStr.ToString();
        _logger.LogInformation("Getting room states for house {HouseId}", houseId);

        try
        {
            RoomStatesResponse? roomStates = await _storageService.GetRoomStatesAsync(houseId);

            if (roomStates == null)
            {
                // Return empty states if none exist
                _logger.LogInformation("No room states found for house {HouseId}, returning empty", houseId);
                return new OkObjectResult(new RoomStatesResponse { RoomStates = [] });
            }

            // Default missing capabilities (null) to full capabilities (both flags)
            // If capabilities is explicitly set to 0, keep it as 0
            foreach (RoomStateDto state in roomStates.RoomStates)
            {
                if (state.Capabilities == null)
                {
                    state.Capabilities = RoomCapabilities.CanSetTemperature | RoomCapabilities.CanDetectRoomOccupancy;
                }
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
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        string houseId = houseIdStr.ToString();
        _logger.LogInformation("Setting room states for house {HouseId}", houseId);

        try
        {
            using StreamReader reader = new(req.Body);
            string body = await reader.ReadToEndAsync();

            RoomStatesResponse? dto = JsonSerializer.Deserialize<RoomStatesResponse>(body, JsonConfiguration.CreateOptions());

            if (dto == null)
            {
                return new BadRequestObjectResult(new { error = "Invalid request body" });
            }

            await _storageService.SaveRoomStatesAsync(houseId, dto);
            _logger.LogInformation("Successfully saved room states for house {HouseId} with {StateCount} rooms",
                houseId, dto.RoomStates.Count);

            // Send SignalR message to all clients for this house
            try
            {
                _logger.LogInformation("About to send room-states-changed message to group house-{HouseId}", houseId);
                await _signalRService.SendMessageToGroupAsync($"house-{houseId}", "room-states-changed", new { houseId });
                _logger.LogInformation("Successfully sent room-states-changed message to group house-{HouseId}", houseId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send SignalR message for room-states-changed to house {HouseId}", houseId);
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
            _logger.LogError(ex, "Error setting room states for house {HouseId}", houseId);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }
}

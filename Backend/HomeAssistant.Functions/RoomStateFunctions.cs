using HomeAssistant.Functions.JsonConverters;
using HomeAssistant.Functions.Services;
using HomeAssistant.Shared.Climate;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System.Collections.Concurrent;
using System.Text.Json;

namespace HomeAssistant.Functions;

public class RoomStateFunctions
{
    private readonly ILogger<RoomStateFunctions> _logger;
    private readonly RoomStateStorageService _storageService;
    private readonly RoomHistoryStorageService _historyService;
    private readonly SignalRService _signalRService;

    // Track last recorded history state per room to enable deduplication
    private static readonly ConcurrentDictionary<string, LastRecordedState> _lastRecordedStates = new();

    private class LastRecordedState
    {
        public double? CurrentTemperature { get; set; }
        public double? TargetTemperature { get; set; }
        public bool HeatingActive { get; set; }
        public DateTimeOffset RecordedAt { get; set; }
    }

    public RoomStateFunctions(ILogger<RoomStateFunctions> logger, SignalRService signalRService)
    {
        _logger = logger;
        _signalRService = signalRService;

        string connectionString = Environment.GetEnvironmentVariable("ScheduleStorageConnectionString")
            ?? throw new InvalidOperationException("ScheduleStorageConnectionString not configured");

        _storageService = new RoomStateStorageService(connectionString);
        _historyService = new RoomHistoryStorageService(connectionString, logger);
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

            // Record temperature history with deduplication
            foreach (RoomStateDto state in dto.RoomStates)
            {
                await RecordHistoryIfChanged(houseId, state);
            }

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

    /// <summary>
    /// Records temperature history if the state has changed significantly enough to warrant recording.
    /// Implements deduplication logic to minimize storage usage.
    /// </summary>
    private async Task RecordHistoryIfChanged(string houseId, RoomStateDto state)
    {
        try
        {
            string key = $"{houseId}_{state.RoomId}";
            DateTimeOffset now = DateTimeOffset.UtcNow;
            bool shouldRecord = false;
            string reason = string.Empty;

            // Get the last recorded state for this room
            if (_lastRecordedStates.TryGetValue(key, out LastRecordedState? lastState))
            {
                // Check if temperature changed by at least 0.1°C
                if (state.CurrentTemperature.HasValue && lastState.CurrentTemperature.HasValue)
                {
                    double tempDiff = Math.Abs(state.CurrentTemperature.Value - lastState.CurrentTemperature.Value);
                    if (tempDiff >= 0.1)
                    {
                        shouldRecord = true;
                        reason = $"temperature changed by {tempDiff:F1}°C";
                    }
                }
                else if (state.CurrentTemperature != lastState.CurrentTemperature)
                {
                    // One was null, now it's not (or vice versa)
                    shouldRecord = true;
                    reason = "temperature availability changed";
                }

                // Check if heating state changed
                if (state.HeatingActive != lastState.HeatingActive)
                {
                    shouldRecord = true;
                    reason = $"heating state changed to {(state.HeatingActive ? "ON" : "OFF")}";
                }

                // Check if target temperature changed
                if (state.TargetTemperature != lastState.TargetTemperature)
                {
                    shouldRecord = true;
                    reason = $"target temperature changed from {lastState.TargetTemperature}°C to {state.TargetTemperature}°C";
                }

                // Check if 15 minutes elapsed since last recording (fallback)
                TimeSpan elapsed = now - lastState.RecordedAt;
                if (elapsed.TotalMinutes >= 15)
                {
                    shouldRecord = true;
                    reason = $"15-minute fallback ({elapsed.TotalMinutes:F0} minutes elapsed)";
                }
            }
            else
            {
                // First time recording for this room
                shouldRecord = true;
                reason = "first recording for room";
            }

            if (shouldRecord)
            {
                await _historyService.SaveHistoryPointAsync(
                    houseId,
                    state.RoomId,
                    state.CurrentTemperature,
                    state.TargetTemperature,
                    state.HeatingActive,
                    now
                );

                // Update last recorded state
                _lastRecordedStates[key] = new LastRecordedState
                {
                    CurrentTemperature = state.CurrentTemperature,
                    TargetTemperature = state.TargetTemperature,
                    HeatingActive = state.HeatingActive,
                    RecordedAt = now
                };

                _logger.LogDebug("Recorded history for room {RoomId}: {Reason}", state.RoomId, reason);
            }
        }
        catch (Exception ex)
        {
            // Don't fail the main request if history recording fails
            _logger.LogError(ex, "Error recording temperature history for room {RoomId}", state.RoomId);
        }
    }
}

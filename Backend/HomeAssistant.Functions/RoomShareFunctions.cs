using HomeAssistant.Functions.Services;
using HomeAssistant.Shared.Climate;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace HomeAssistant.Functions;

/// <summary>
/// Owner endpoints for the QR links, one per zone (<c>roomId=</c>) or layout group (<c>groupId=</c>). Keyed by
/// house ID like every other owner endpoint; the tokens they hand out are what guests use (GuestFunctions).
/// </summary>
public class RoomShareFunctions
{
    private readonly ILogger<RoomShareFunctions> _logger;
    private readonly RoomShareStorageService _shares;
    private readonly ScheduleStorageService _schedules;

    public RoomShareFunctions(ILogger<RoomShareFunctions> logger)
    {
        _logger = logger;

        string connectionString = Environment.GetEnvironmentVariable("ScheduleStorageConnectionString")
            ?? throw new InvalidOperationException("ScheduleStorageConnectionString not configured");

        _shares = new RoomShareStorageService(connectionString);
        _schedules = new ScheduleStorageService(connectionString);
    }

    [Function("GetRoomShares")]
    public async Task<IActionResult> GetRoomShares(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "room-shares")] HttpRequest req)
    {
        if (!TryGetHouseId(req, out string houseId))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        List<RoomShareStorageService.RoomShare> shares = await _shares.ListAsync(houseId);
        return new OkObjectResult(shares.Select(ToResponse));
    }

    [Function("CreateRoomShare")]
    public async Task<IActionResult> CreateRoomShare(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "room-shares")] HttpRequest req)
    {
        return await WithTarget(req, async (houseId, roomId, groupId) => await _shares.GetOrCreateAsync(houseId, roomId, groupId));
    }

    [Function("RevokeRoomShare")]
    public async Task<IActionResult> RevokeRoomShare(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "room-shares/revoke")] HttpRequest req)
    {
        return await WithTarget(req, async (houseId, roomId, groupId) =>
        {
            _logger.LogInformation("Revoking the QR link for {Target} in house {HouseId}", groupId != null ? $"group {groupId}" : $"room {roomId}", houseId);
            return await _shares.RevokeAsync(houseId, roomId, groupId);
        });
    }

    /// <summary>Checks the house ID and that the zone (roomId=) or group (groupId=) exists, then runs the action.</summary>
    private async Task<IActionResult> WithTarget(HttpRequest req, Func<string, int?, string?, Task<RoomShareStorageService.RoomShare>> action)
    {
        if (!TryGetHouseId(req, out string houseId))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        RoomSchedulesDto? schedules = await _schedules.GetSchedulesAsync(houseId);

        if (req.Query.TryGetValue("groupId", out StringValues groupIdText) && !string.IsNullOrWhiteSpace(groupIdText))
        {
            string groupId = groupIdText.ToString();
            if (schedules == null || GuestOverride.FindGroup(schedules.Layout, groupId) == null)
            {
                return new NotFoundObjectResult(new { error = "No such group" });
            }

            return new OkObjectResult(ToResponse(await action(houseId, null, groupId)));
        }

        if (!req.Query.TryGetValue("roomId", out StringValues roomIdText) || !int.TryParse(roomIdText, out int roomId))
        {
            return new BadRequestObjectResult(new { error = "Give a roomId or a groupId query parameter" });
        }

        if (schedules?.Rooms.Any(r => r.Id == roomId) != true)
        {
            return new NotFoundObjectResult(new { error = "No such zone" });
        }

        return new OkObjectResult(ToResponse(await action(houseId, roomId, null)));
    }

    private static bool TryGetHouseId(HttpRequest req, out string houseId)
    {
        houseId = req.Query.TryGetValue("houseId", out StringValues value) ? value.ToString() : string.Empty;
        return !string.IsNullOrWhiteSpace(houseId);
    }

    private static object ToResponse(RoomShareStorageService.RoomShare share)
    {
        return new { roomId = share.RoomId, groupId = share.GroupId, token = share.Token, createdAt = share.CreatedAt };
    }
}

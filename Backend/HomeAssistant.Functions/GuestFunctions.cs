using HomeAssistant.Functions.Services;
using HomeAssistant.Shared.Climate;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System.Text.Json;

namespace HomeAssistant.Functions;

/// <summary>
/// What a guest who scanned a QR code can do, keyed by the share token alone. A token covers one zone or one
/// layout group (every zone in it, at any depth). These never return the house ID, and they can only set,
/// change or cancel an override on those zones' boosts, within the limits in
/// <see cref="GuestOverride"/>. Schedules, other zones and every other setting are out of reach, because every
/// other endpoint needs the house ID, which a guest never sees.
/// </summary>
public class GuestFunctions
{
    private readonly ILogger<GuestFunctions> _logger;
    private readonly RoomShareStorageService _shares;
    private readonly ScheduleStorageService _schedules;
    private readonly RoomStateStorageService _roomStates;
    private readonly SignalRService _signalR;

    public GuestFunctions(ILogger<GuestFunctions> logger, SignalRService signalR)
    {
        _logger = logger;
        _signalR = signalR;

        string connectionString = Environment.GetEnvironmentVariable("ScheduleStorageConnectionString")
            ?? throw new InvalidOperationException("ScheduleStorageConnectionString not configured");

        _shares = new RoomShareStorageService(connectionString);
        _schedules = new ScheduleStorageService(connectionString);
        _roomStates = new RoomStateStorageService(connectionString);
    }

    [Function("GetGuestRoom")]
    public async Task<IActionResult> GetGuestRoom(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "guest/room")] HttpRequest req)
    {
        RoomShareStorageService.RoomShare? share = await FindShare(req);
        RoomSchedulesDto? schedules = share == null ? null : await _schedules.GetSchedulesAsync(share.HouseId);
        Target? target = share == null || schedules == null ? null : Resolve(share, schedules);
        if (target == null)
        {
            return InvalidLink();
        }

        RoomStatesResponse? states = await _roomStates.GetRoomStatesAsync(share!.HouseId);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        List<object> zones = [];
        BoostDto? activeOverride = null;
        foreach (RoomDto room in target.Rooms)
        {
            RoomStateDto? state = states?.RoomStates.FirstOrDefault(s => s.RoomId == room.Id);
            BoostDto? zoneOverride = GuestOverride.Active(room, now);
            zones.Add(new
            {
                name = Label(room),
                icon = room.Icon,
                currentTemperature = state?.CurrentTemperature,
                // A running override (any boost) is the target from the moment it is saved; the app's own
                // report of the target only catches up on its next upload, which can be minutes away.
                targetTemperature = zoneOverride?.Temperature ?? state?.TargetTemperature,
                overridden = zoneOverride != null,
                overrideEndTime = zoneOverride?.EndTime,
                heatingActive = state?.HeatingActive ?? false
            });

            // The override the guest controls: for a zone its running boost; for a group only the group's own
            // boost, not a zone's or a smaller group's inside it.
            if (!target.IsGroup || zoneOverride?.GroupId == share.GroupId)
            {
                activeOverride ??= zoneOverride;
            }
        }

        // While a group around the target is boosted the guest can only look (owner, 2026-10-19).
        RoomLayoutNodeDto? lockedBy = GuestOverride.LockedBy(schedules!, share.RoomId, share.GroupId, now);

        return new OkObjectResult(new
        {
            kind = target.IsGroup ? "group" : "zone",
            name = target.Name,
            icon = target.Icon,
            lockedBy = lockedBy == null ? null : lockedBy.Name ?? "Group",
            zones,
            @override = activeOverride == null ? null : new { temperature = activeOverride.Temperature, endTime = activeOverride.EndTime },
            limits = new
            {
                minTemperature = GuestOverride.MinTemperature,
                maxTemperature = GuestOverride.MaxTemperature,
                maxMinutes = GuestOverride.MaxMinutes
            }
        });
    }

    /// <summary>Sets, or changes, the override on the token's zone or group. A new one replaces a running one.</summary>
    [Function("SetGuestOverride")]
    public async Task<IActionResult> SetGuestOverride(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "guest/override")] HttpRequest req)
    {
        RoomShareStorageService.RoomShare? share = await FindShare(req);
        if (share == null)
        {
            return InvalidLink();
        }

        GuestOverrideRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<GuestOverrideRequest>(req.Body, JsonConfiguration.CreateOptions());
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(new { error = "Invalid request body" });
        }

        if (request == null)
        {
            return new BadRequestObjectResult(new { error = "Invalid request body" });
        }

        string? problem = GuestOverride.Validate(request.Temperature, request.DurationMinutes);
        if (problem != null)
        {
            return new BadRequestObjectResult(new { error = problem });
        }

        return await ChangeSchedules(share, (schedules, roomIds) =>
            GuestOverride.Apply(schedules, roomIds, request.Temperature, request.DurationMinutes, DateTimeOffset.UtcNow, share.GroupId) > 0,
            $"set to {request.Temperature} °C for {request.DurationMinutes} min");
    }

    [Function("CancelGuestOverride")]
    public async Task<IActionResult> CancelGuestOverride(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "guest/override/cancel")] HttpRequest req)
    {
        RoomShareStorageService.RoomShare? share = await FindShare(req);
        if (share == null)
        {
            return InvalidLink();
        }

        return await ChangeSchedules(share, (schedules, roomIds) =>
        {
            GuestOverride.Cancel(schedules, roomIds, share.GroupId);
            return true;
        }, "cancelled");
    }

    /// <summary>
    /// Loads the house's schedules, lets <paramref name="change"/> alter the boosts of the share's zones, saves,
    /// and sends the same "schedules-changed" SignalR message as an owner save, so the NetDaemon app and any
    /// open owner page pick it up.
    /// </summary>
    private async Task<IActionResult> ChangeSchedules(RoomShareStorageService.RoomShare share, Func<RoomSchedulesDto, IReadOnlyCollection<int>, bool> change, string what)
    {
        RoomSchedulesDto? schedules = await _schedules.GetSchedulesAsync(share.HouseId);
        Target? target = schedules == null ? null : Resolve(share, schedules);
        if (target == null)
        {
            return InvalidLink();
        }

        // Nothing inside a boosted group can be changed on its own; only that group, or one around it, can.
        RoomLayoutNodeDto? lockedBy = GuestOverride.LockedBy(schedules!, share.RoomId, share.GroupId, DateTimeOffset.UtcNow);
        if (lockedBy != null)
        {
            return new ConflictObjectResult(new { error = $"{lockedBy.Name ?? "A group"} is boosted, so this can't be changed until that boost ends." });
        }

        if (!change(schedules!, [.. target.Rooms.Select(r => r.Id)]))
        {
            return InvalidLink();
        }

        await _schedules.SaveSchedulesAsync(share.HouseId, schedules!);
        _logger.LogInformation("Guest override for {Target} in house {HouseId} {What}", target.Name, share.HouseId, what);

        try
        {
            await _signalR.SendMessageToGroupAsync($"house-{share.HouseId}", "schedules-changed", new { houseId = share.HouseId });
        }
        catch (Exception ex)
        {
            // The save stands; the app also re-reads schedules periodically.
            _logger.LogError(ex, "Failed to send schedules-changed after a guest override for house {HouseId}", share.HouseId);
        }

        return new OkObjectResult(new { success = true });
    }

    /// <summary>What a share covers now: its zone, or every zone in its group. Null if that no longer exists.</summary>
    private static Target? Resolve(RoomShareStorageService.RoomShare share, RoomSchedulesDto schedules)
    {
        if (share.GroupId != null)
        {
            RoomLayoutNodeDto? group = GuestOverride.FindGroup(schedules.Layout, share.GroupId);
            List<int> ids = GuestOverride.RoomIdsInGroup(schedules, share.GroupId);
            if (group == null || ids.Count == 0)
            {
                return null;
            }

            // Keep the layout's order, so the guest sees the zones as the owner arranged them.
            List<RoomDto> rooms = [.. ids.Select(id => schedules.Rooms.First(r => r.Id == id))];
            return new Target(true, group.Name ?? "Group", group.Icon, rooms);
        }

        RoomDto? room = schedules.Rooms.FirstOrDefault(r => r.Id == share.RoomId);
        return room == null ? null : new Target(false, Label(room), room.Icon, [room]);
    }

    private sealed record Target(bool IsGroup, string Name, string? Icon, List<RoomDto> Rooms);

    private static string Label(RoomDto room) => string.IsNullOrWhiteSpace(room.DisplayName) ? room.Name : room.DisplayName;

    private async Task<RoomShareStorageService.RoomShare?> FindShare(HttpRequest req)
    {
        return req.Query.TryGetValue("token", out StringValues token)
            ? await _shares.FindByTokenAsync(token.ToString())
            : null;
    }

    private static IActionResult InvalidLink()
    {
        return new NotFoundObjectResult(new { error = "This link is no longer valid." });
    }

    private sealed class GuestOverrideRequest
    {
        public double Temperature { get; set; }
        public int DurationMinutes { get; set; }
    }
}

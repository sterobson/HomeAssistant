using HomeAssistant.Functions.Services;
using HomeAssistant.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HomeAssistant.Functions;

/// <summary>
/// The 3D house view (owner, 2026-10-22). This app only stores the NetDaemon app's device snapshot and relays
/// messages both ways over SignalR; which devices may be controlled is decided by the NetDaemon app, which refuses
/// anything it does not offer. Like every endpoint here, the house ID is the only credential (CLAUDE.md → Access).
/// </summary>
public partial class HouseDevicesFunctions
{
    private const int MaxChangesPerBatch = 500;

    private readonly ILogger<HouseDevicesFunctions> _logger;
    private readonly HouseDevicesStorageService _storageService;
    private readonly SignalRService _signalRService;

    public HouseDevicesFunctions(ILogger<HouseDevicesFunctions> logger, HouseDevicesStorageService storageService, SignalRService signalRService)
    {
        _logger = logger;
        _storageService = storageService;
        _signalRService = signalRService;
    }

    [GeneratedRegex("^[a-z_]+\\.[a-z0-9_]+$")]
    private static partial Regex EntityIdPattern();

    [Function("GetHouseDevices")]
    public async Task<IActionResult> GetHouseDevices(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "house-devices")] HttpRequest req)
    {
        if (!TryGetHouseId(req, out string houseId))
        {
            return MissingHouseId();
        }

        try
        {
            HouseDevicesDto? snapshot = await _storageService.GetAsync(houseId);
            return new OkObjectResult(snapshot ?? new HouseDevicesDto());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting house devices for house {HouseId}", houseId);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>From the NetDaemon app: a fresh snapshot. Stored, then the page is told to fetch it.</summary>
    [Function("SetHouseDevices")]
    public async Task<IActionResult> SetHouseDevices(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "house-devices")] HttpRequest req)
    {
        if (!TryGetHouseId(req, out string houseId))
        {
            return MissingHouseId();
        }

        HouseDevicesDto? snapshot = await ReadBodyAsync<HouseDevicesDto>(req);
        if (snapshot == null)
        {
            return new BadRequestObjectResult(new { error = "Invalid request body" });
        }

        try
        {
            await _storageService.SaveAsync(houseId, snapshot);
            await _signalRService.SendMessageToGroupAsync($"house-{houseId}", "house-devices-changed", new { updatedAt = snapshot.UpdatedAt });
            return new OkObjectResult(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving house devices for house {HouseId}", houseId);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>From the page, when it opens and every few minutes while open: asks the NetDaemon app for live states.</summary>
    [Function("WatchHouseDevices")]
    public async Task<IActionResult> WatchHouseDevices(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "house-devices/watch")] HttpRequest req)
    {
        if (!TryGetHouseId(req, out string houseId))
        {
            return MissingHouseId();
        }

        await _signalRService.SendMessageToGroupAsync($"house-{houseId}", "house-devices-watch", new { houseId });
        return new OkObjectResult(new { success = true });
    }

    /// <summary>From the NetDaemon app: a batch of state changes, relayed to the page and not stored.</summary>
    [Function("PostHouseDeviceStates")]
    public async Task<IActionResult> PostHouseDeviceStates(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "house-devices/states")] HttpRequest req)
    {
        if (!TryGetHouseId(req, out string houseId))
        {
            return MissingHouseId();
        }

        HouseDeviceStatesDto? states = await ReadBodyAsync<HouseDeviceStatesDto>(req);
        if (states == null || states.Changes.Count > MaxChangesPerBatch)
        {
            return new BadRequestObjectResult(new { error = $"Expected up to {MaxChangesPerBatch} changes" });
        }

        await _signalRService.SendMessageToGroupAsync($"house-{houseId}", "house-device-states", new
        {
            changes = states.Changes.Select(change => new { entityId = change.EntityId, state = change.State })
        });
        return new OkObjectResult(new { success = true });
    }

    /// <summary>From the page: one command for one entity, relayed to the NetDaemon app, which decides whether to run it.</summary>
    [Function("SendHouseDeviceCommand")]
    public async Task<IActionResult> SendHouseDeviceCommand(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "house-devices/command")] HttpRequest req)
    {
        if (!TryGetHouseId(req, out string houseId))
        {
            return MissingHouseId();
        }

        HouseDeviceCommandDto? command = await ReadBodyAsync<HouseDeviceCommandDto>(req);
        if (command == null || !EntityIdPattern().IsMatch(command.EntityId ?? string.Empty) || !HouseDeviceActions.All.Contains(command.Action ?? string.Empty))
        {
            return new BadRequestObjectResult(new { error = "Expected an entityId and an action of turn_on, turn_off, toggle or press" });
        }

        string commandId = Guid.NewGuid().ToString("N");
        _logger.LogInformation("3D view command {CommandId}: {Action} on {EntityId} for house {HouseId}", commandId, command.Action, command.EntityId, houseId);
        await _signalRService.SendMessageToGroupAsync($"house-{houseId}", "house-device-command", new
        {
            commandId,
            entityId = command.EntityId,
            action = command.Action
        });
        return new OkObjectResult(new { commandId });
    }

    /// <summary>From the NetDaemon app: whether a command worked, relayed to the page.</summary>
    [Function("PostHouseDeviceCommandResult")]
    public async Task<IActionResult> PostHouseDeviceCommandResult(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "house-devices/command-result")] HttpRequest req)
    {
        if (!TryGetHouseId(req, out string houseId))
        {
            return MissingHouseId();
        }

        HouseDeviceCommandResultDto? result = await ReadBodyAsync<HouseDeviceCommandResultDto>(req);
        if (result == null)
        {
            return new BadRequestObjectResult(new { error = "Invalid request body" });
        }

        await _signalRService.SendMessageToGroupAsync($"house-{houseId}", "house-device-command-result", new
        {
            commandId = result.CommandId,
            entityId = result.EntityId,
            ok = result.Ok,
            message = result.Message
        });
        return new OkObjectResult(new { success = true });
    }

    private static bool TryGetHouseId(HttpRequest req, out string houseId)
    {
        houseId = string.Empty;
        if (!req.Query.TryGetValue("houseId", out StringValues value) || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        houseId = value.ToString();
        return true;
    }

    private static BadRequestObjectResult MissingHouseId() =>
        new(new { error = "Invalid or missing houseId query parameter" });

    private async Task<T?> ReadBodyAsync<T>(HttpRequest req) where T : class
    {
        try
        {
            using StreamReader reader = new(req.Body);
            string body = await reader.ReadToEndAsync();
            return JsonSerializer.Deserialize<T>(body, JsonConfiguration.CreateOptions());
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON for {Type}", typeof(T).Name);
            return null;
        }
    }
}

using HomeAssistant.Functions.Models;
using HomeAssistant.Functions.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System.Text.Json;

namespace HomeAssistant.Functions;

public class HeatingConfigFunctions
{
    private readonly ILogger<HeatingConfigFunctions> _logger;
    private readonly HeatingConfigStorageService _storageService;
    private readonly SignalRService _signalRService;

    public HeatingConfigFunctions(ILogger<HeatingConfigFunctions> logger, HeatingConfigStorageService storageService, SignalRService signalRService)
    {
        _logger = logger;
        _storageService = storageService;
        _signalRService = signalRService;
    }

    [Function("GetHeatingConfig")]
    public async Task<IActionResult> GetHeatingConfig(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "heating-config")] HttpRequest req)
    {
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        string houseId = houseIdStr.ToString();
        _logger.LogInformation("Getting heating config for house {HouseId}", houseId);

        try
        {
            HeatingConfigDto? config = await _storageService.GetConfigAsync(houseId);

            if (config == null)
            {
                return new OkObjectResult(new HeatingConfigDto());
            }

            return new OkObjectResult(config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting heating config for house {HouseId}", houseId);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    [Function("SetHeatingConfig")]
    public async Task<IActionResult> SetHeatingConfig(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "heating-config")] HttpRequest req)
    {
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        string houseId = houseIdStr.ToString();
        _logger.LogInformation("Setting heating config for house {HouseId}", houseId);

        try
        {
            using StreamReader reader = new(req.Body);
            string body = await reader.ReadToEndAsync();

            HeatingConfigDto? dto = JsonSerializer.Deserialize<HeatingConfigDto>(body, JsonConfiguration.CreateOptions());

            if (dto == null)
            {
                return new BadRequestObjectResult(new { error = "Invalid request body" });
            }

            await _storageService.SaveConfigAsync(houseId, dto);
            _logger.LogInformation("Successfully saved heating config for house {HouseId}", houseId);

            try
            {
                await _signalRService.SendMessageToGroupAsync($"house-{houseId}", "heating-config-changed", new { houseId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send SignalR message for heating-config-changed to house {HouseId}", houseId);
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
            _logger.LogError(ex, "Error setting heating config for house {HouseId}", houseId);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }
}

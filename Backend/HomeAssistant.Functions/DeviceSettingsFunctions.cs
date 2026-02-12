using HomeAssistant.Functions.JsonConverters;
using HomeAssistant.Functions.Models;
using HomeAssistant.Functions.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System.Text.Json;

namespace HomeAssistant.Functions;

public class DeviceSettingsFunctions
{
    private readonly ILogger<DeviceSettingsFunctions> _logger;
    private readonly DeviceSettingsStorageService _storageService;
    private readonly SignalRService _signalRService;

    public DeviceSettingsFunctions(ILogger<DeviceSettingsFunctions> logger, DeviceSettingsStorageService storageService, SignalRService signalRService)
    {
        _logger = logger;
        _storageService = storageService;
        _signalRService = signalRService;
    }

    [Function("GetDeviceSettings")]
    public async Task<IActionResult> GetDeviceSettings(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "device-settings")] HttpRequest req)
    {
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        string houseId = houseIdStr.ToString();
        _logger.LogInformation("Getting device settings for house {HouseId}", houseId);

        try
        {
            DeviceSettingsDto? settings = await _storageService.GetSettingsAsync(houseId);

            if (settings == null)
            {
                return new OkObjectResult(new DeviceSettingsDto());
            }

            return new OkObjectResult(settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting device settings for house {HouseId}", houseId);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    [Function("SetDeviceSettings")]
    public async Task<IActionResult> SetDeviceSettings(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "device-settings")] HttpRequest req)
    {
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        string houseId = houseIdStr.ToString();
        _logger.LogInformation("Setting device settings for house {HouseId}", houseId);

        try
        {
            using StreamReader reader = new(req.Body);
            string body = await reader.ReadToEndAsync();

            DeviceSettingsDto? dto = JsonSerializer.Deserialize<DeviceSettingsDto>(body, JsonConfiguration.CreateOptions());

            if (dto == null)
            {
                return new BadRequestObjectResult(new { error = "Invalid request body" });
            }

            await _storageService.SaveSettingsAsync(houseId, dto);
            _logger.LogInformation("Successfully saved device settings for house {HouseId}", houseId);

            try
            {
                await _signalRService.SendMessageToGroupAsync($"house-{houseId}", "device-settings-changed", new { houseId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send SignalR message for device-settings-changed to house {HouseId}", houseId);
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
            _logger.LogError(ex, "Error setting device settings for house {HouseId}", houseId);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }
}

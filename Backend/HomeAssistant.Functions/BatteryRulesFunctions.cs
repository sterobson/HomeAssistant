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

public class BatteryRulesFunctions
{
    private readonly ILogger<BatteryRulesFunctions> _logger;
    private readonly BatteryRulesStorageService _storageService;
    private readonly SignalRService _signalRService;

    public BatteryRulesFunctions(ILogger<BatteryRulesFunctions> logger, BatteryRulesStorageService storageService, SignalRService signalRService)
    {
        _logger = logger;
        _storageService = storageService;
        _signalRService = signalRService;
    }

    [Function("GetBatteryRules")]
    public async Task<IActionResult> GetBatteryRules(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "battery-rules")] HttpRequest req)
    {
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        string houseId = houseIdStr.ToString();
        _logger.LogInformation("Getting battery rules for house {HouseId}", houseId);

        try
        {
            BatteryZoneRulesDto? rules = await _storageService.GetRulesAsync(houseId);

            if (rules == null)
            {
                return new OkObjectResult(new BatteryZoneRulesDto());
            }

            return new OkObjectResult(rules);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting battery rules for house {HouseId}", houseId);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    [Function("SetBatteryRules")]
    public async Task<IActionResult> SetBatteryRules(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "battery-rules")] HttpRequest req)
    {
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        string houseId = houseIdStr.ToString();
        _logger.LogInformation("Setting battery rules for house {HouseId}", houseId);

        try
        {
            using StreamReader reader = new(req.Body);
            string body = await reader.ReadToEndAsync();

            BatteryZoneRulesDto? dto = JsonSerializer.Deserialize<BatteryZoneRulesDto>(body, JsonConfiguration.CreateOptions());

            if (dto == null)
            {
                return new BadRequestObjectResult(new { error = "Invalid request body" });
            }

            await _storageService.SaveRulesAsync(houseId, dto);
            _logger.LogInformation("Successfully saved battery rules for house {HouseId} with {RuleCount} rules",
                houseId, dto.Rules.Count);

            try
            {
                await _signalRService.SendMessageToGroupAsync($"house-{houseId}", "battery-rules-changed", new { houseId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send SignalR message for battery-rules-changed to house {HouseId}", houseId);
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
            _logger.LogError(ex, "Error setting battery rules for house {HouseId}", houseId);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }
}

using HomeAssistant.Functions.JsonConverters;
using HomeAssistant.Functions.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System.Text.Json;

namespace HomeAssistant.Functions;

public class PowerMonitorFunctions
{
    private readonly ILogger<PowerMonitorFunctions> _logger;
    private readonly SignalRService _signalRService;

    public PowerMonitorFunctions(ILogger<PowerMonitorFunctions> logger, SignalRService signalRService)
    {
        _logger = logger;
        _signalRService = signalRService;
    }

    [Function("StartPowerMonitor")]
    public async Task<IActionResult> StartPowerMonitor(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "power-monitor/start")] HttpRequest req)
    {
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        string houseId = houseIdStr.ToString();
        _logger.LogInformation("Starting power monitor for house {HouseId}", houseId);

        try
        {
            await _signalRService.SendMessageToGroupAsync($"house-{houseId}", "start-power-monitor", new
            {
                houseId
            });

            return new OkObjectResult(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting power monitor for house {HouseId}", houseId);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    [Function("PostPowerSnapshot")]
    public async Task<IActionResult> PostPowerSnapshot(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "power-monitor/snapshot")] HttpRequest req)
    {
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        string houseId = houseIdStr.ToString();

        try
        {
            using StreamReader reader = new(req.Body);
            string body = await reader.ReadToEndAsync();

            PowerSnapshotRequest? snapshot = JsonSerializer.Deserialize<PowerSnapshotRequest>(body, JsonConfiguration.CreateOptions());

            if (snapshot == null)
            {
                return new BadRequestObjectResult(new { error = "Invalid request body" });
            }

            await _signalRService.SendMessageToGroupAsync($"house-{houseId}", "power-snapshot", new
            {
                solarW = snapshot.SolarW,
                batteryW = snapshot.BatteryW,
                houseW = snapshot.HouseW,
                gridW = snapshot.GridW
            });

            return new OkObjectResult(new { success = true });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error parsing JSON for power snapshot, house {HouseId}", houseId);
            return new BadRequestObjectResult(new { error = "Invalid JSON format" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error posting power snapshot for house {HouseId}", houseId);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }
}

public class PowerSnapshotRequest
{
    public double SolarW { get; set; }
    public double BatteryW { get; set; }
    public double HouseW { get; set; }
    public double GridW { get; set; }
}

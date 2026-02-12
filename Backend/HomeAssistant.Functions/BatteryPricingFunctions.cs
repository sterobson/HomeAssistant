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

public class BatteryPricingFunctions
{
    private readonly ILogger<BatteryPricingFunctions> _logger;
    private readonly BatteryPricingStorageService _pricingService;

    public BatteryPricingFunctions(ILogger<BatteryPricingFunctions> logger)
    {
        _logger = logger;

        string connectionString = Environment.GetEnvironmentVariable("ScheduleStorageConnectionString")
            ?? throw new InvalidOperationException("ScheduleStorageConnectionString not configured");

        _pricingService = new BatteryPricingStorageService(connectionString, logger);
    }

    [Function("GetBatteryPricing")]
    public async Task<IActionResult> GetBatteryPricing(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "battery-pricing")] HttpRequest req)
    {
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        if (!req.Query.TryGetValue("date", out StringValues dateStr) ||
            string.IsNullOrWhiteSpace(dateStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing date query parameter" });
        }

        string houseId = houseIdStr.ToString();
        string date = dateStr.ToString();
        _logger.LogInformation("Getting battery pricing for house {HouseId} on {Date}", houseId, date);

        try
        {
            List<BatteryPricingPoint> points = await _pricingService.GetPricingDataAsync(houseId, date);

            BatteryPricingResponse response = new BatteryPricingResponse
            {
                Date = date,
                Points = points.Select(p => new PricingPointDto
                {
                    TimeMinutes = p.TimeMinutes,
                    ImportPrice = p.ImportPrice,
                    ExportPrice = p.ExportPrice
                }).ToList()
            };

            return new OkObjectResult(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting battery pricing for house {HouseId} on {Date}", houseId, date);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    [Function("SetBatteryPricing")]
    public async Task<IActionResult> SetBatteryPricing(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "battery-pricing")] HttpRequest req)
    {
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        if (!req.Query.TryGetValue("date", out StringValues dateStr) ||
            string.IsNullOrWhiteSpace(dateStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing date query parameter" });
        }

        string houseId = houseIdStr.ToString();
        string date = dateStr.ToString();
        _logger.LogInformation("Setting battery pricing for house {HouseId} on {Date}", houseId, date);

        try
        {
            using StreamReader reader = new(req.Body);
            string body = await reader.ReadToEndAsync();

            List<PricingPointDto>? dtos = JsonSerializer.Deserialize<List<PricingPointDto>>(body, JsonConfiguration.CreateOptions());

            if (dtos == null)
            {
                return new BadRequestObjectResult(new { error = "Invalid request body" });
            }

            List<BatteryPricingPoint> points = dtos.Select(dto => new BatteryPricingPoint
            {
                TimeMinutes = dto.TimeMinutes,
                ImportPrice = dto.ImportPrice,
                ExportPrice = dto.ExportPrice
            }).ToList();

            // Delete existing data for this date before saving new points
            await _pricingService.DeletePricingDataAsync(houseId, date);
            await _pricingService.SavePricingDataAsync(houseId, date, points);
            _logger.LogInformation("Successfully saved {Count} pricing points for house {HouseId} on {Date}",
                points.Count, houseId, date);

            return new OkObjectResult(new { success = true });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error parsing JSON for house {HouseId}", houseId);
            return new BadRequestObjectResult(new { error = "Invalid JSON format" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting battery pricing for house {HouseId} on {Date}", houseId, date);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }
}

public class BatteryPricingResponse
{
    public string Date { get; set; } = string.Empty;
    public List<PricingPointDto> Points { get; set; } = [];
}

public class PricingPointDto
{
    public int TimeMinutes { get; set; }
    public double ImportPrice { get; set; }
    public double ExportPrice { get; set; }
}

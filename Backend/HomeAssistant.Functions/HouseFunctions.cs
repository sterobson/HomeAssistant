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

public class HouseFunctions
{
    private readonly ILogger<HouseFunctions> _logger;
    private readonly HouseDetailsStorageService _storageService;

    public HouseFunctions(ILogger<HouseFunctions> logger)
    {
        _logger = logger;

        string connectionString = Environment.GetEnvironmentVariable("ScheduleStorageConnectionString")
            ?? throw new InvalidOperationException("ScheduleStorageConnectionString not configured");

        _storageService = new HouseDetailsStorageService(connectionString);
    }

    [Function("GetHouseDetails")]
    public async Task<IActionResult> GetHouseDetails(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "house-details")] HttpRequest req)
    {
        // Get houseId from query parameter
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        string houseId = houseIdStr.ToString();
        _logger.LogInformation("Getting house details for house {HouseId}", houseId);

        try
        {
            HouseDetailsDto? houseDetails = await _storageService.GetHouseDetailsAsync(houseId);

            if (houseDetails == null)
            {
                // Return empty details if none exist
                _logger.LogInformation("No house details found for house {HouseId}, returning empty", houseId);
                return new OkObjectResult(new HouseDetailsDto());
            }

            return new OkObjectResult(houseDetails);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting house details for house {HouseId}", houseId);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    [Function("SetHouseDetails")]
    public async Task<IActionResult> SetHouseDetails(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "house-details")] HttpRequest req)
    {
        // Get houseId from query parameter
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        string houseId = houseIdStr.ToString();
        _logger.LogInformation("Setting house details for house {HouseId}", houseId);

        try
        {
            using StreamReader reader = new(req.Body);
            string body = await reader.ReadToEndAsync();

            HouseDetailsDto? dto = JsonSerializer.Deserialize<HouseDetailsDto>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new FlexibleEnumConverterFactory() }
            });

            if (dto == null)
            {
                return new BadRequestObjectResult(new { error = "Invalid request body" });
            }

            await _storageService.SaveHouseDetailsAsync(houseId, dto);
            _logger.LogInformation("Successfully saved house details for house {HouseId} with name '{Name}'",
                houseId, dto.Name);

            return new OkObjectResult(new { success = true });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error parsing JSON for house {HouseId}", houseId);
            return new BadRequestObjectResult(new { error = "Invalid JSON format" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting house details for house {HouseId}", houseId);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }
}

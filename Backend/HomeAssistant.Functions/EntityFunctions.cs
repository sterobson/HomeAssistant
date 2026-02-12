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

public class EntityFunctions
{
    private readonly ILogger<EntityFunctions> _logger;
    private readonly EntityStorageService _storageService;

    public EntityFunctions(ILogger<EntityFunctions> logger, EntityStorageService storageService)
    {
        _logger = logger;
        _storageService = storageService;
    }

    [Function("GetEntities")]
    public async Task<IActionResult> GetEntities(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "entities")] HttpRequest req)
    {
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        string houseId = houseIdStr.ToString();
        _logger.LogInformation("Getting entities for house {HouseId}", houseId);

        try
        {
            EntityListDto? entities = await _storageService.GetEntitiesAsync(houseId);

            if (entities == null)
            {
                return new OkObjectResult(new EntityListDto());
            }

            return new OkObjectResult(entities);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting entities for house {HouseId}", houseId);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    [Function("SetEntities")]
    public async Task<IActionResult> SetEntities(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "entities")] HttpRequest req)
    {
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        string houseId = houseIdStr.ToString();
        _logger.LogInformation("Setting entities for house {HouseId}", houseId);

        try
        {
            using StreamReader reader = new(req.Body);
            string body = await reader.ReadToEndAsync();

            EntityListDto? dto = JsonSerializer.Deserialize<EntityListDto>(body, JsonConfiguration.CreateOptions());

            if (dto == null)
            {
                return new BadRequestObjectResult(new { error = "Invalid request body" });
            }

            await _storageService.SaveEntitiesAsync(houseId, dto);
            _logger.LogInformation("Successfully saved {EntityCount} entities for house {HouseId}",
                dto.Entities.Count, houseId);

            return new OkObjectResult(new { success = true });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error parsing JSON for house {HouseId}", houseId);
            return new BadRequestObjectResult(new { error = "Invalid JSON format" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting entities for house {HouseId}", houseId);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }
}

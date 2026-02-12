using HomeAssistant.Services;
using HomeAssistant.Shared;
using NetDaemon.HassModel.Entities;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Text.Json;
using System.Threading.Tasks;

namespace HomeAssistant.apps.HassModel.Energy;

[NetDaemonApp]
internal class EntityPushService
{
    private static readonly HashSet<string> AllowedDomains =
    [
        "sensor", "number", "select", "switch", "climate", "input_number", "input_boolean"
    ];

    private readonly IHaContext _ha;
    private readonly IEntityPushApiClient _apiClient;
    private readonly WebSynchronisationConfiguration _configuration;
    private readonly ILogger<EntityPushService> _logger;

    public EntityPushService(
        IScheduler scheduler,
        IHaContext ha,
        IEntityPushApiClient apiClient,
        WebSynchronisationConfiguration configuration,
        ILogger<EntityPushService> logger)
    {
        _ha = ha;
        _apiClient = apiClient;
        _configuration = configuration;
        _logger = logger;

        // Initial push after 30s delay to allow HA to fully load
        Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(async _ =>
        {
            await PushEntitiesAsync();
        });

        // Re-push every 6 hours to pick up new entities
        scheduler.SchedulePeriodic(TimeSpan.FromHours(6), async () =>
        {
            await PushEntitiesAsync();
        });
    }

    private async Task PushEntitiesAsync()
    {
        if (string.IsNullOrEmpty(_configuration.HouseId))
        {
            _logger.LogWarning("Cannot push entities - HouseId not configured");
            return;
        }

        try
        {
            IEnumerable<Entity> allEntities = _ha.GetAllEntities();

            List<EntityItemDto> filteredEntities = allEntities
                .Where(e => !string.IsNullOrEmpty(e.EntityId))
                .Where(e =>
                {
                    string domain = e.EntityId.Split('.')[0];
                    return AllowedDomains.Contains(domain);
                })
                .Select(e => new EntityItemDto
                {
                    EntityId = e.EntityId,
                    Name = ExtractFriendlyName(e),
                    Domain = e.EntityId.Split('.')[0],
                    State = FormatState(e)
                })
                .OrderBy(e => e.EntityId)
                .ToList();

            EntityListDto dto = new()
            {
                Entities = filteredEntities,
                UpdatedAt = DateTime.UtcNow
            };

            await _apiClient.PostEntitiesAsync(_configuration.HouseId, dto);
            _logger.LogInformation("Successfully pushed {Count} entities to API for house {HouseId}",
                filteredEntities.Count, _configuration.HouseId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pushing entities to API for house {HouseId}", _configuration.HouseId);
        }
    }

    private static string? FormatState(Entity entity)
    {
        string? state = entity.EntityState?.State;
        if (string.IsNullOrEmpty(state)) return null;

        try
        {
            JsonElement? attributes = entity.EntityState?.AttributesJson;
            if (attributes.HasValue && attributes.Value.TryGetProperty("unit_of_measurement", out JsonElement unit))
            {
                string? unitStr = unit.GetString();
                if (!string.IsNullOrEmpty(unitStr))
                {
                    return $"{state} {unitStr}";
                }
            }
        }
        catch
        {
            // Fall back to state without unit
        }

        return state;
    }

    private static string ExtractFriendlyName(Entity entity)
    {
        try
        {
            JsonElement? attributes = entity.EntityState?.AttributesJson;
            if (attributes.HasValue && attributes.Value.TryGetProperty("friendly_name", out JsonElement friendlyName))
            {
                return friendlyName.GetString() ?? entity.EntityId;
            }
        }
        catch
        {
            // Fall back to entity ID if attribute extraction fails
        }

        return entity.EntityId;
    }
}

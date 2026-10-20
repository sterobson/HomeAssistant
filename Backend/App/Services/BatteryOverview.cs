using HomeAssistant.Shared.Climate;
using NetDaemon.HassModel.Entities;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace HomeAssistant.Services;

/// <summary>Every replaceable battery in the house right now, for the web UI's low-battery icons.</summary>
internal interface IBatteryOverview
{
    List<BatteryDeviceDto> Read();
}

/// <summary>
/// Reads every replaceable battery (the same set <see cref="DeviceBatteryPushService"/> logs daily) with its device's
/// name, Home Assistant area and other entities, from HA's registry (owner, 2026-10-20). The UI places each device on
/// a zone or layout group from that and shows the lowest level at 20% or below.
/// </summary>
internal class BatteryOverview(IHaContext ha, IHaRegistry registry, ILogger<BatteryOverview> logger) : IBatteryOverview
{
    public List<BatteryDeviceDto> Read()
    {
        List<BatteryDeviceDto> batteries = [];
        foreach (Entity entity in ha.GetAllEntities())
        {
            if (!DeviceBatteryPushService.IsReplaceableBatterySensor(entity.EntityId, ReadAttribute(entity, "device_class")))
            {
                continue;
            }

            try
            {
                batteries.Add(Describe(entity));
            }
            catch (Exception ex)
            {
                // One odd registry entry must not lose every other battery.
                logger.LogDebug(ex, "Could not describe battery {EntityId}", entity.EntityId);
                batteries.Add(new BatteryDeviceDto
                {
                    EntityId = entity.EntityId,
                    Name = ReadAttribute(entity, "friendly_name") ?? entity.EntityId,
                    Percent = DeviceBatteryPushService.ParsePercent(entity.EntityState?.State),
                    DeviceEntityIds = [entity.EntityId]
                });
            }
        }

        return [.. batteries.OrderBy(battery => battery.EntityId, StringComparer.OrdinalIgnoreCase)];
    }

    private BatteryDeviceDto Describe(Entity entity)
    {
        EntityRegistration? registration = registry.GetEntityRegistration(entity.EntityId);
        Device? device = registration?.Device;
        string? deviceName = device == null ? null : (device.NameByUser ?? device.Name);

        return new BatteryDeviceDto
        {
            EntityId = entity.EntityId,
            Name = string.IsNullOrWhiteSpace(deviceName) ? ReadAttribute(entity, "friendly_name") ?? entity.EntityId : deviceName.Trim(),
            Percent = DeviceBatteryPushService.ParsePercent(entity.EntityState?.State),
            Area = registration?.Area?.Name ?? device?.Area?.Name,
            DeviceEntityIds = device == null
                ? [entity.EntityId]
                : [.. device.Entities.Select(deviceEntity => deviceEntity.EntityId).Where(id => !string.IsNullOrEmpty(id)).Order(StringComparer.OrdinalIgnoreCase)]
        };
    }

    private static string? ReadAttribute(Entity entity, string attribute)
    {
        try
        {
            JsonElement? attributes = entity.EntityState?.AttributesJson;
            if (attributes.HasValue && attributes.Value.ValueKind == JsonValueKind.Object
                && attributes.Value.TryGetProperty(attribute, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }
        catch
        {
            // Malformed attributes: treat as missing.
        }

        return null;
    }
}

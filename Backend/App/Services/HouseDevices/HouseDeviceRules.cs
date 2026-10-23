using HomeAssistant.Shared;
using System.Collections.Generic;
using System.Linq;

namespace HomeAssistant.Services.HouseDevices;

/// <summary>One entity as read from Home Assistant, before the rules decide what the 3D view may do with it.</summary>
internal sealed record HouseEntityReading(
    string EntityId,
    string Name,
    string? State,
    string? Unit,
    string? DeviceClass,
    string? Platform);

/// <summary>One registry device with its entities, as read from Home Assistant.</summary>
internal sealed record HouseDeviceReading(
    string Id,
    string Name,
    string? Manufacturer,
    string? Model,
    string? Area,
    IReadOnlyList<HouseEntityReading> Entities);

/// <summary>
/// Which devices the 3D house view shows and what it may do to each entity (owner, 2026-10-22). Pure, so the rules
/// can be pinned by tests without Home Assistant.
/// </summary>
internal static class HouseDeviceRules
{
    /// <summary>
    /// Entity kinds that say nothing about the device in its room: firmware updates, logbook events, speech services,
    /// notification targets and calendars. A device with nothing else (an add-on, an integration) is not shown.
    /// </summary>
    private static readonly HashSet<string> HiddenDomains =
    [
        "update", "event", "tts", "stt", "notify", "calendar", "conversation", "wake_word", "assist_satellite", "time", "text", "image"
    ];

    /// <summary>
    /// The integrations whose switches, lights and buttons may be used from the 3D view: the house's own Zigbee plugs
    /// and buttons, the TP-Link plugs and the WiZ lights. The car, the charger, the inverter, the router and the
    /// speakers can be looked at but not controlled, so a stray tap on a model can't stop a charge or reboot the
    /// Wi-Fi.
    /// </summary>
    internal static readonly HashSet<string> ControllablePlatforms = ["zha", "tplink", "wiz"];

    internal const string ValveReason = "Set by the heating. Change it from the heating page.";

    public static string DomainOf(string entityId)
    {
        int dot = entityId.IndexOf('.');
        return dot < 0 ? entityId : entityId[..dot];
    }

    /// <summary>The commands an entity's kind allows, whoever owns it.</summary>
    public static List<string> ActionsForDomain(string domain) => domain switch
    {
        "switch" or "light" or "input_boolean" => [HouseDeviceActions.TurnOn, HouseDeviceActions.TurnOff, HouseDeviceActions.Toggle],
        "button" => [HouseDeviceActions.Press],
        _ => []
    };

    /// <summary>
    /// The devices to show, with the actions each entity allows. <paramref name="ownedEntities"/> maps an entity the
    /// app drives by itself to why it does; every entity of a device that has one is shown but not controllable, so
    /// nothing in the view fights the heating or the dehumidifier. A device with a radiator valve is the heating's too.
    /// </summary>
    public static List<HouseDeviceDto> Describe(
        IEnumerable<HouseDeviceReading> devices,
        IReadOnlyDictionary<string, string> ownedEntities,
        IReadOnlySet<string>? alwaysShown = null)
    {
        List<HouseDeviceDto> result = [];
        foreach (HouseDeviceReading device in devices)
        {
            // A device with nothing to show is left out, unless it must be drawn anyway (the Zigbee hub, which has no
            // entities but is where every signal line ends).
            List<HouseEntityReading> shown = [.. device.Entities.Where(e => !HiddenDomains.Contains(DomainOf(e.EntityId)) && e.State != null)];
            if (shown.Count == 0 && alwaysShown?.Contains(device.Id) != true)
            {
                continue;
            }

            string? deviceOwner = device.Entities.Select(e => ownedEntities.GetValueOrDefault(e.EntityId)).FirstOrDefault(reason => reason != null)
                ?? (device.Entities.Any(e => DomainOf(e.EntityId) == "climate") && device.Entities.Any(e => e.Platform == "zha") ? ValveReason : null);

            result.Add(new HouseDeviceDto
            {
                Id = device.Id,
                Name = device.Name,
                Manufacturer = device.Manufacturer,
                Model = device.Model,
                Area = device.Area,
                Entities = [.. shown
                    .OrderBy(e => e.EntityId, StringComparer.OrdinalIgnoreCase)
                    .Select(e => DescribeEntity(e, deviceOwner))]
            });
        }

        return [.. result.OrderBy(d => d.Area ?? "￿", StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)];
    }

    private static HouseDeviceEntityDto DescribeEntity(HouseEntityReading entity, string? deviceOwner)
    {
        string domain = DomainOf(entity.EntityId);
        List<string> actions = ActionsForDomain(domain);
        string? controlledBy = null;
        if (actions.Count > 0)
        {
            if (deviceOwner != null)
            {
                controlledBy = deviceOwner;
                actions = [];
            }
            else if (entity.Platform == null || !ControllablePlatforms.Contains(entity.Platform))
            {
                controlledBy = "Can only be viewed here, so a stray tap can't change it. Use Home Assistant to change it.";
                actions = [];
            }
        }

        return new HouseDeviceEntityDto
        {
            EntityId = entity.EntityId,
            Name = entity.Name,
            Domain = domain,
            State = entity.State,
            Unit = entity.Unit,
            DeviceClass = entity.DeviceClass,
            Actions = actions,
            ControlledBy = controlledBy
        };
    }

    /// <summary>
    /// Whether a command may run: the entity must be one the snapshot offers that action for. Null when it may,
    /// otherwise the reason to give back.
    /// </summary>
    public static string? RefuseCommand(IEnumerable<HouseDeviceDto> devices, string entityId, string action)
    {
        HouseDeviceEntityDto? entity = devices.SelectMany(d => d.Entities).FirstOrDefault(e => string.Equals(e.EntityId, entityId, StringComparison.OrdinalIgnoreCase));
        if (entity == null)
        {
            return "That device isn't in the house any more.";
        }

        if (!entity.Actions.Contains(action))
        {
            return entity.ControlledBy ?? "That can't be done to this device.";
        }

        return null;
    }
}

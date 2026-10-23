using System.Collections.Generic;

namespace HomeAssistant.Shared;

/// <summary>
/// Every physical device in Home Assistant with its entities and their state, for the web UI's 3D house view
/// (owner, 2026-10-22). Pushed by the NetDaemon app; the Functions app only stores and relays it.
/// </summary>
public class HouseDevicesDto
{
    public List<HouseDeviceDto> Devices { get; set; } = [];
    public DateTime UpdatedAt { get; set; }

    /// <summary>The Zigbee mesh as ZHA last scanned it, or null when it could not be read (owner, 2026-10-22).</summary>
    public HouseZigbeeDto? Zigbee { get; set; }
}

/// <summary>
/// The Zigbee mesh from ZHA's neighbour tables. ZHA does not report the routes messages actually take, only who hears
/// whom and how well, so the UI works out each device's likely route to the hub from these links.
/// </summary>
public class HouseZigbeeDto
{
    public List<HouseZigbeeNodeDto> Nodes { get; set; } = [];
    public List<HouseZigbeeLinkDto> Links { get; set; } = [];
}

public class HouseZigbeeNodeDto
{
    /// <summary>Home Assistant's device registry ID, as in <see cref="HouseDeviceDto.Id"/>.</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>"coordinator", "router" or "end".</summary>
    public string Role { get; set; } = string.Empty;

    public bool Available { get; set; }
}

/// <summary>One entry of a neighbour table: <see cref="From"/> hears <see cref="To"/> at <see cref="Lqi"/> (0–255).</summary>
public class HouseZigbeeLinkDto
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public int Lqi { get; set; }

    /// <summary>What <see cref="To"/> is to <see cref="From"/>: "parent", "child", "sibling" or "other".</summary>
    public string Relationship { get; set; } = string.Empty;
}

public class HouseDeviceDto
{
    /// <summary>Home Assistant's device registry ID.</summary>
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }

    /// <summary>The Home Assistant area name, which the UI maps to a room of its plan. Null when HA has none.</summary>
    public string? Area { get; set; }

    public List<HouseDeviceEntityDto> Entities { get; set; } = [];
}

public class HouseDeviceEntityDto
{
    public string EntityId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string? State { get; set; }
    public string? Unit { get; set; }
    public string? DeviceClass { get; set; }

    /// <summary>The commands the UI may send for this entity (<see cref="HouseDeviceActions"/>); empty when none.</summary>
    public List<string> Actions { get; set; } = [];

    /// <summary>Why this entity can't be controlled from the UI although its kind could be, or null.</summary>
    public string? ControlledBy { get; set; }
}

/// <summary>A batch of state changes, sent while a 3D view is open.</summary>
public class HouseDeviceStatesDto
{
    public List<HouseDeviceStateDto> Changes { get; set; } = [];
}

public class HouseDeviceStateDto
{
    public string EntityId { get; set; } = string.Empty;
    public string? State { get; set; }
}

/// <summary>A command from the UI to one entity, relayed to the NetDaemon app.</summary>
public class HouseDeviceCommandDto
{
    public string CommandId { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
}

/// <summary>What became of a command: whether Home Assistant accepted it, and if not why.</summary>
public class HouseDeviceCommandResultDto
{
    public string CommandId { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public bool Ok { get; set; }
    public string? Message { get; set; }
}

public static class HouseDeviceActions
{
    public const string TurnOn = "turn_on";
    public const string TurnOff = "turn_off";
    public const string Toggle = "toggle";
    public const string Press = "press";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { TurnOn, TurnOff, Toggle, Press };
}

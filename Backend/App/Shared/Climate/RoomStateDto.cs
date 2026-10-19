using System.Collections.Generic;

namespace HomeAssistant.Shared.Climate;

public class RoomStatesResponse
{
    public List<RoomStateDto> RoomStates { get; set; } = [];
}

public class RoomStateDto
{
    public int RoomId { get; set; }
    public double? CurrentTemperature { get; set; }
    public double? TargetTemperature { get; set; }
    public bool HeatingActive { get; set; }
    public int ActiveScheduleTrackId { get; set; }
    public string LastUpdated { get; set; } = string.Empty;
    public RoomCapabilities Capabilities { get; set; }

    /// <summary>
    /// The hardware this zone is bound to, as the heating code sees it: what it drives, what it
    /// reads and what it detects presence with. Built from the same switch statements that do
    /// the binding, so a zone that has lost its hardware shows an empty list here. Display only.
    /// </summary>
    public List<RoomDeviceDto> Devices { get; set; } = [];

    /// <summary>
    /// Which presence detectors count for this zone, and every detector there is to choose from, so the UI can
    /// show and edit it (owner, 2026-10-19). Display only; the choice is saved in device settings as
    /// <c>heating.zoneOccupancy</c>.
    /// </summary>
    public RoomOccupancyDto? Occupancy { get; set; }
}

public class RoomOccupancyDto
{
    /// <summary>The detectors that count for this zone; it is in use while any of them is active.</summary>
    public List<string> DetectorIds { get; set; } = [];

    /// <summary>True while nothing has been saved, so these are the built-in defaults.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Every detector the app knows, with whether each is active right now.</summary>
    public List<PresenceDetectorDto> Available { get; set; } = [];
}

/// <summary>One way of telling someone is using something, from plugs. Defined in the NetDaemon app (PresenceService).</summary>
public class PresenceDetectorDto
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    /// <summary>Whether it counts towards "house" (someone is home). A desk merely switched on does not.</summary>
    public bool MeansSomeoneHome { get; set; }

    /// <summary>Whether it was active when the room state was published.</summary>
    public bool Active { get; set; }
}

/// <summary>
/// One Home Assistant entity a zone uses, with its state at the time the room state was published.
/// </summary>
public class RoomDeviceDto
{
    public string EntityId { get; set; } = string.Empty;

    /// <summary>The entity's friendly name in Home Assistant, or null if it was not available.</summary>
    public string? Name { get; set; }

    /// <summary>One of <see cref="RoomDeviceRoles"/>. A string rather than an enum so it survives every JSON hop unchanged.</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>The raw Home Assistant state ("on", "21.4", "heat", "unavailable"), or null if the entity was not found.</summary>
    public string? State { get; set; }

    /// <summary>The entity's unit_of_measurement, if it has one.</summary>
    public string? Unit { get; set; }

    /// <summary>Radiator valves only: the valve's own onboard temperature reading.</summary>
    public double? CurrentTemperature { get; set; }

    /// <summary>Radiator valves only: the target the valve is set to.</summary>
    public double? TargetTemperature { get; set; }

    /// <summary>
    /// Drivable devices only (heater plugs, radiator valves): the rules the app is applying to this
    /// device right now, or null for none. Lets the UI show, and start editing from, the built-in
    /// defaults before anyone has saved rules.
    /// </summary>
    public DeviceRulesDto? Rules { get; set; }

    /// <summary>True when <see cref="Rules"/> are the app's built-in defaults rather than saved ones.</summary>
    public bool RulesAreDefault { get; set; }
}

public static class RoomDeviceRoles
{
    public const string Thermometer = "thermometer";
    public const string RadiatorValve = "radiatorValve";
    public const string HeaterPlug = "heaterPlug";
    public const string DeskPlug = "deskPlug";
    public const string DeskPower = "deskPower";
    public const string TvPower = "tvPower";
    public const string LightsPlug = "lightsPlug";
}

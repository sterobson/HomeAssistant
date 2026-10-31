using System.Collections.Generic;

namespace HomeAssistant.Shared.Climate;

public class RoomStatesResponse
{
    public List<RoomStateDto> RoomStates { get; set; } = [];

    /// <summary>
    /// What the NetDaemon app has the boiler doing, for the UI (owner, 2026-10-19). Null from an app too old
    /// to send it. Since 2026-10-31 the state the boiler relay is kept in; before that a belief kept by counting fingerbot presses.
    /// </summary>
    public BoilerStateDto? Boiler { get; set; }

    /// <summary>
    /// Every replaceable battery in the house (owner, 2026-10-20): the cells in TRVs, thermometers, buttons, motion
    /// and flood sensors and the boiler fingerbot; not phones, the car or the home battery. The UI places each on a
    /// zone (one that uses any of the device's entities), else by its Home Assistant area, else at the top of the
    /// page, and warns at 20% or below. Null from an app too old to send it.
    /// </summary>
    public List<BatteryDeviceDto>? Batteries { get; set; }
}

/// <summary>One device's replaceable battery, with what the UI needs to say where the device is.</summary>
public class BatteryDeviceDto
{
    /// <summary>The battery sensor, e.g. sensor.bedroom_1_radiator_battery.</summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>The device's name in Home Assistant (the owner's name for it if set), else the sensor's.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The charge in percent, or null while the sensor is unavailable (a silent device is not a flat one).</summary>
    public double? Percent { get; set; }

    /// <summary>The Home Assistant area of the sensor or its device, or null for none.</summary>
    public string? Area { get; set; }

    /// <summary>Every entity of the battery's device, so the UI can tell which zone's heating uses it.</summary>
    public List<string> DeviceEntityIds { get; set; } = [];
}

public class BoilerStateDto
{
    /// <summary>True in relay mode, when the app drives the boiler; false while the boiler thermostat does.</summary>
    public bool AppControlsBoiler { get; set; }

    /// <summary>Whether the app has the boiler on (named from the fingerbot, when it was a belief). Meaningless unless <see cref="AppControlsBoiler"/>.</summary>
    public bool BelievedOn { get; set; }

    /// <summary>Since when, ISO 8601, or null when it does not know.</summary>
    public string? Since { get; set; }
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

    /// <summary>
    /// The tracks that are due now (started today, no earlier than the active one) and would run but for their
    /// "room in use" or "room not in use" condition, so the UI can say why on the track (owner, 2026-10-21). Empty
    /// during a boost, and from an app too old to send it.
    /// </summary>
    public List<int> WaitingForOccupancyTrackIds { get; set; } = [];
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

    /// <summary>Radiator valves and heater plugs: whether the pre-heat is Auto, worked out from the warm-up rate below.</summary>
    public bool PreHeatAuto { get; set; }

    /// <summary>
    /// Radiator valves and heater plugs (since 2026-10-21): how fast this device has warmed its room, in degrees
    /// Celsius an hour, averaged over its
    /// last <see cref="HeatingRateSamples"/> warm-ups (a valve's only with the boiler on); null until there is one.
    /// </summary>
    public double? HeatingRateCelsiusPerHour { get; set; }

    /// <summary>Radiator valves and heater plugs: how many warm-ups <see cref="HeatingRateCelsiusPerHour"/> averages (up to 5).</summary>
    public int HeatingRateSamples { get; set; }

    /// <summary>
    /// Radiator valves (since 2026-10-23): set when this valve has no warm-up of its own yet and Auto uses another
    /// valve's in the same zone instead, to that valve's entity ID. <see cref="HeatingRateCelsiusPerHour"/> and
    /// <see cref="HeatingRateSamples"/> are then that valve's. Null when the rate is the valve's own, or there is none.
    /// </summary>
    public string? HeatingRateFromEntityId { get; set; }

    /// <summary>
    /// Radiator valves and heater plugs: how long this device would likely take to bring its room up to the next warmer schedule
    /// track, from the room's temperature now; null without a rate, or with no warmer track to warm up for.
    /// </summary>
    public int? WarmUpMinutes { get; set; }

    /// <summary>
    /// Radiator valves (since 2026-10-23): what the app has seen this valve do wrong, one entry per kind of problem
    /// (<see cref="ValveHealthKinds"/>) that has ever been seen or checked. Empty when nothing is known, null from an
    /// app too old to send it. Read-only observation: nothing is moved to test a valve.
    /// </summary>
    public List<ValveHealthIssueDto>? ValveHealth { get; set; }
}

/// <summary>One kind of radiator valve fault and what the app has seen of it.</summary>
public class ValveHealthIssueDto
{
    /// <summary>One of <see cref="ValveHealthKinds"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>True when the fault was seen and no boiler run since has shown the valve behaving properly.</summary>
    public bool Flagged { get; set; }

    /// <summary>How many times it has been seen.</summary>
    public int Count { get; set; }

    /// <summary>When it was last seen, ISO 8601, or null if never.</summary>
    public string? LastSeenAt { get; set; }

    /// <summary>What was seen that time, in words (in °C, for logs and as a fallback).</summary>
    public string? LastSeenDetail { get; set; }

    /// <summary>
    /// The valve's own readings that time, in °C: for <see cref="ValveHealthKinds.LeaksWhenShut"/> its lowest while shut
    /// and how high it climbed; for <see cref="ValveHealthKinds.DoesNotWarm"/> its reading when the hour began and its
    /// highest in it.
    /// </summary>
    public double? FromTemperature { get; set; }
    public double? ToTemperature { get; set; }

    /// <summary><see cref="ValveHealthKinds.DoesNotWarm"/> only: how long the valve had been open, in minutes.</summary>
    public int? Minutes { get; set; }

    /// <summary>When a boiler run last showed the valve behaving properly, ISO 8601, or null if never.</summary>
    public string? LastOkAt { get; set; }
}

public static class ValveHealthKinds
{
    /// <summary>Its radiator warms while the valve is shut and hot water flows: the valve lets water through.</summary>
    public const string LeaksWhenShut = "leaksWhenShut";

    /// <summary>Its radiator stays cold while the valve is open and another radiator proves hot water is flowing.</summary>
    public const string DoesNotWarm = "doesNotWarm";

    public static string Describe(string kind) => kind switch
    {
        LeaksWhenShut => "lets hot water through while shut",
        DoesNotWarm => "stays cold while open",
        _ => kind
    };
}

public static class RoomDeviceRoles
{
    public const string Thermometer = "thermometer";
    public const string RadiatorValve = "radiatorValve";
    public const string HeaterPlug = "heaterPlug";
    /// <summary>
    /// A towel rail on the boiler with no valve: not in Home Assistant, so its entity ID is the app's own name for it,
    /// and its state is "on"/"off" with the boiler while the app runs the boiler, null otherwise.
    /// </summary>
    public const string TowelRail = "towelRail";
    public const string DeskPlug = "deskPlug";
    public const string DeskPower = "deskPower";
    public const string TvPower = "tvPower";
    public const string LightsPlug = "lightsPlug";
    /// <summary>
    /// The router's list of devices online, which phone presence is read from. Listed by apps before 2026-10-21; since
    /// then each phone is listed as itself (<see cref="Phone"/>).
    /// </summary>
    public const string RouterDevices = "routerDevices";

    /// <summary>
    /// A phone whose presence on the home Wi-Fi a zone uses: not a Home Assistant entity, so its entity ID is the app's
    /// own ("phone.ellie"), and its state is "home", "away" or "unavailable".
    /// </summary>
    public const string Phone = "phone";
}

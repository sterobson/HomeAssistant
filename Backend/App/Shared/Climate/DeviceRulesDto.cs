using System;
using System.Collections.Generic;

namespace HomeAssistant.Shared.Climate;

// Per-device heating rules. Shared (linked) with the Functions project, which stores them in the
// device-settings blob and passes them through on room states; only the NetDaemon app evaluates them
// (DeviceRulesEvaluator).

/// <summary>When a heating device may run, on top of its zone wanting heat.</summary>
public class DeviceRulesDto
{
    /// <summary>When it may run. Empty means any time.</summary>
    public List<TimeWindowDto> TimeWindows { get; set; } = [];

    /// <summary>Who must be present. Null, or no sources, means no presence is needed.</summary>
    public PresenceRuleDto? Presence { get; set; }

    /// <summary>
    /// How many minutes before a warmer schedule track starts this device begins heating for it ("pre-heat",
    /// formerly the track's ramp-up), 0 to <see cref="MaxPreHeatMinutes"/>. Per device because a radiator warms a room
    /// far more slowly than a fan heater (owner, 2026-10-19). Null means the default for its kind: for a heater plug
    /// none; for a radiator valve <b>Auto</b> (owner, 2026-10-20), which works the time out from how fast that valve
    /// has actually warmed its room (see <see cref="IsAutoPreHeat"/>), and is <see cref="DefaultValvePreHeatMinutes"/>
    /// until there is data to go on.
    /// </summary>
    public int? PreHeatMinutes { get; set; }

    /// <summary>
    /// True for an Auto pre-heat, worked out from the device's measured warm-up rate; false for a fixed one
    /// (<see cref="PreHeatMinutes"/>). Null means the default for its kind: Auto for a radiator valve with no
    /// <see cref="PreHeatMinutes"/>, fixed for a heater plug. Added 2026-10-21 so a heater plug can be set to Auto
    /// (owner), which "no minutes" could not say, since for a heater that means its default of none.
    /// </summary>
    public bool? PreHeatAuto { get; set; }

    public const int DefaultValvePreHeatMinutes = 30;
    public const int DefaultHeaterPreHeatMinutes = 0;
    public const int MaxPreHeatMinutes = 60;

    /// <summary>The longest an Auto pre-heat may run: a cold room can need more than the 60 minutes a fixed one offers.</summary>
    public const int MaxAutoPreHeatMinutes = 120;

    /// <summary>The longest any pre-heat can be, so how far ahead the schedule needs to be looked at.</summary>
    public const int LongestPreHeatMinutes = MaxAutoPreHeatMinutes;

    /// <summary>
    /// Whether the pre-heat is worked out from the device's measured warm-up rate rather than fixed: whatever
    /// <see cref="PreHeatAuto"/> says, else a radiator valve with no pre-heat set. A heater plug is Auto only when set
    /// to be (owner, 2026-10-21); its default is a fixed pre-heat of none.
    /// </summary>
    public static bool IsAutoPreHeat(DeviceRulesDto? rules, bool isRadiatorValve) =>
        rules?.PreHeatAuto ?? (isRadiatorValve && rules?.PreHeatMinutes == null);

    /// <summary>
    /// The fixed pre-heat a device with these rules (or none) uses, clamped to 0 to <see cref="MaxPreHeatMinutes"/>.
    /// For an Auto device this is the figure used until its warm-up has been measured: 30 minutes for a valve, none
    /// for a heater plug (unless minutes are also set).
    /// </summary>
    public static int PreHeatFor(DeviceRulesDto? rules, bool isRadiatorValve) =>
        Math.Clamp(rules?.PreHeatMinutes ?? (isRadiatorValve ? DefaultValvePreHeatMinutes : DefaultHeaterPreHeatMinutes), 0, MaxPreHeatMinutes);
}

public class TimeWindowDto
{
    /// <summary>The days the window starts on.</summary>
    public Days Days { get; set; } = Days.Everyday;

    /// <summary>Local time, "HH:mm".</summary>
    public string Start { get; set; } = "00:00";

    /// <summary>Local time, "HH:mm". At or before <see cref="Start"/> means the window runs past midnight.</summary>
    public string End { get; set; } = "00:00";
}

public class PresenceRuleDto
{
    public PresenceMatch Match { get; set; } = PresenceMatch.Any;

    /// <summary>
    /// <see cref="PresenceSources.House"/>, or the canonical name of a zone that can detect presence
    /// ("games desk", "den", ...).
    /// </summary>
    public List<string> Sources { get; set; } = [];
}

public enum PresenceMatch
{
    /// <summary>At least one source is in use.</summary>
    Any = 0,

    /// <summary>Every source is in use.</summary>
    All = 1
}

public static class PresenceSources
{
    /// <summary>
    /// Pseudo-source: any zone that can detect presence is in use. Deliberately not motion sensors,
    /// which the cat sets off.
    /// </summary>
    public const string House = "house";
}

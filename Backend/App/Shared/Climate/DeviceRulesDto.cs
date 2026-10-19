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

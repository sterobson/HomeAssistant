namespace HomeAssistant.Services.Climate;

/// <summary>
/// A radiator on the boiler's circuit with no valve or switch: it is hot whenever the boiler runs and cold when it
/// does not, so the only way to heat its zone is to run the boiler (owner, 2026-10-20). Listed with a zone's
/// switches so that the zone can be given a target, a schedule and a boost, and calls for the boiler like a room
/// with a radiator valve (<see cref="HeatingControlService"/>.DecideBoiler). Nothing is ever sent to it.
/// It is not in Home Assistant, so <see cref="Id"/> is the app's own name for it, not an entity ID.
/// </summary>
internal sealed record TowelRail(string Id, string Name)
{
    /// <summary>The upstairs bathroom's heated towel rail.</summary>
    public static readonly TowelRail UpstairsBathroom = new("upstairs bathroom towel rail", "Heated towel rail");
}

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HomeAssistant.Shared.Climate;

public class RoomSchedulesDto
{
    public HouseOccupancyState HouseOccupancyState { get; set; } = HouseOccupancyState.Home;

    /// <summary>
    /// How the rooms are arranged on screen: an ordered tree whose nodes are rooms or groups, and whose
    /// groups hold more nodes, to any depth. Purely presentation. The heating code never reads it and
    /// every room is still scheduled, controlled and reported on its own, wherever it sits here.
    ///
    /// A room that is in <see cref="Rooms"/> but missing from this tree is shown at the end of the top
    /// level, and a node naming a room that no longer exists is skipped, so the two lists cannot get
    /// stuck out of step. An empty tree means "no arrangement saved yet": rooms appear in list order.
    /// </summary>
    public List<RoomLayoutNodeDto> Layout { get; set; } = [];

    public List<RoomDto> Rooms { get; set; } = [];
}

/// <summary>
/// One node of <see cref="RoomSchedulesDto.Layout"/>: either a room or a group of further nodes.
/// </summary>
public class RoomLayoutNodeDto
{
    /// <summary>Set for a room node: the <see cref="RoomDto.Id"/> it shows. Null for a group.</summary>
    public int? RoomId { get; set; }

    /// <summary>Group nodes only: a stable identifier, so a group can be renamed without losing its place.</summary>
    public string? Id { get; set; }

    /// <summary>Group nodes only: the name shown on the group.</summary>
    public string? Name { get; set; }

    /// <summary>Group nodes only: an emoji shown beside the name.</summary>
    public string? Icon { get; set; }

    /// <summary>Group nodes only: what the group contains, in display order. Null for a room node.</summary>
    public List<RoomLayoutNodeDto>? Children { get; set; }
}

public class RoomDto
{
    public int Id { get; set; }

    /// <summary>
    /// The room's canonical name. This is the join key between configuration and hardware:
    /// four switch statements in HeatingControlService and PresenceService match on
    /// Name.Trim().ToLower() to decide which plugs, valves and sensors a room is wired to.
    /// Changing it silently unbinds the room from its hardware, so rename
    /// <see cref="DisplayName"/> instead.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional human-friendly name shown in the UI in place of <see cref="Name"/>.
    /// Free to change at any time — nothing binds to it. Null or empty means fall back
    /// to <see cref="Name"/>.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// An emoji shown beside the room's name. UI-only: nothing binds to it, so it is safe to change.
    /// </summary>
    public string? Icon { get; set; }

    public BoostDto? Boost { get; set; }

    public List<ScheduleTrackDto> Schedules { get; set; } = [];
}

public class BoostDto
{
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public double? Temperature { get; set; }

    /// <summary>
    /// The layout group that set this boost, when a group was boosted (the owner's ⚡ on a group card, or a guest's
    /// group link): the same boost then sits on every zone inside, each tagged with the group. Null for a zone's
    /// own boost. While a group's boost runs nothing inside it can be boosted on its own (GuestOverride.LockedBy,
    /// Frontend/src/utils/boosts.js). The NetDaemon app ignores it: a boost is a boost.
    /// </summary>
    public string? GroupId { get; set; }
}

public class ScheduleTrackDto
{
    public int Id { get; set; }

    public string Time { get; set; } = string.Empty;

    // Support alternative property name for backward compatibility
    [JsonIgnore]
    public string TargetTime
    {
        get => Time;
        set => Time = value;
    }

    public double Temperature { get; set; }
    public int RampUpMinutes { get; set; }
    public Days Days { get; set; }
    public ConditionType Conditions { get; set; }
}

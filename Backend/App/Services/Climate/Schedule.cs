using HomeAssistant.Shared.Climate;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HomeAssistant.Services.Climate;

public class RoomSchedules
{
    public HouseOccupancyState HouseOccupancyState { get; set; } = HouseOccupancyState.Home;

    /// <summary>UI-only arrangement of the rooms as a tree; see RoomSchedulesDto.Layout. The heating code never reads it.</summary>
    public List<RoomLayoutNode> Layout { get; set; } = [];

    public List<RoomSchedule> Rooms { get; set; } = [];
}

public class RoomLayoutNode
{
    public int? RoomId { get; set; }
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Icon { get; set; }
    public List<RoomLayoutNode>? Children { get; set; }
}

public class Boost
{
    public DateTimeOffset? StartTime { get; set; }
    public DateTimeOffset? EndTime { get; set; }
    public double? Temperature { get; set; }
}

public class RoomSchedule
{
    public int Id { get; set; }

    /// <summary>
    /// The room's canonical name, and the join key to its hardware — see the remarks on
    /// RoomDto.Name. Never rename this to change what the UI shows; set
    /// <see cref="DisplayName"/> instead.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional UI-only name. Nothing binds to it, so it is safe to change.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>UI-only emoji shown beside the room's name; the heating code ignores it.</summary>
    public string? Icon { get; set; }

    public required List<HeatingScheduleTrack> ScheduleTracks { get; set; }
    public Boost Boost { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore]
    public Func<Task<double?>>? GetCurrentTemperature { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public Func<bool, Task<bool>>? OnToggleHeating { get; set; }
}

// Room state - separate from schedule configuration
public class RoomState
{
    public int RoomId { get; set; }
    public double? CurrentTemperature { get; set; }
    public double? TargetTemperature { get; set; }
    public bool HeatingActive { get; set; }
    public int ActiveScheduleTrackId { get; set; }
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
    public RoomCapabilities Capabilities { get; set; }
}

public class HeatingScheduleTrack
{
    public int Id { get; set; }
    public required double Temperature { get; set; }
    public required TimeOnly TargetTime { get; set; }
    public int RampUpMinutes { get; set; } = 30;
    public Days Days { get; set; } = Days.Unspecified;
    public ConditionType Conditions { get; set; } = ConditionType.None;
}

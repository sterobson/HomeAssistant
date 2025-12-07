using HomeAssistant.Shared.Climate;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HomeAssistant.Services.Climate;

public class RoomSchedules
{
    public HouseOccupancyState HouseOccupancyState { get; set; } = HouseOccupancyState.Home;
    public List<RoomSchedule> Rooms { get; set; } = [];
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
    public string Name { get; set; } = string.Empty;
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
    public bool HeatingActive { get; set; }
    public int ActiveScheduleTrackId { get; set; }
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
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

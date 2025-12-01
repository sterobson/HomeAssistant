using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HomeAssistant.Shared.Climate;

public class RoomSchedulesDto
{
    public List<RoomDto> Rooms { get; set; } = [];
}

public class RoomDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public BoostDto? Boost { get; set; }

    public List<ScheduleTrackDto> Schedules { get; set; } = [];
}

public class BoostDto
{
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public double? Temperature { get; set; }
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
    public ConditionOperatorType ConditionOperator { get; set; }
}

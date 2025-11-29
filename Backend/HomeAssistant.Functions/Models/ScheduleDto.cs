using HomeAssistant.Services.Climate;
using System.Text.Json.Serialization;

namespace HomeAssistant.Functions.Models;

public class SchedulesResponse
{
    public List<RoomDto> Rooms { get; set; } = [];
}

public class RoomDto
{
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("room")]
    public Room Room { get; set; }

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
    public string Id { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public double Temperature { get; set; }
    public int RampUpMinutes { get; set; }
    public Days Days { get; set; }
    public ConditionType Conditions { get; set; }
    public ConditionOperatorType ConditionOperator { get; set; }
}

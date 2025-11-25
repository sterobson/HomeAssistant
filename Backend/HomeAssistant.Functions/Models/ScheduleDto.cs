namespace HomeAssistant.Functions.Models;

public class SchedulesResponse
{
    public List<RoomDto> Rooms { get; set; } = [];
}

public class RoomDto
{
    public string Id { get; set; } = string.Empty;
    public int RoomType { get; set; }
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
    public string Conditions { get; set; } = string.Empty;
}

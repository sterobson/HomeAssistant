using System.Collections.Generic;

namespace HomeAssistant.Shared.Climate;

public class RoomStatesResponse
{
    public List<RoomStateDto> RoomStates { get; set; } = [];
}

public class RoomStateDto
{
    public string RoomId { get; set; } = string.Empty;
    public double? CurrentTemperature { get; set; }
    public bool HeatingActive { get; set; }
    public string? ActiveScheduleTrackId { get; set; }
    public string LastUpdated { get; set; } = string.Empty;
}

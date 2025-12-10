using System.Collections.Generic;

namespace HomeAssistant.Shared.Climate;

public class RoomStatesResponse
{
    public List<RoomStateDto> RoomStates { get; set; } = [];
}

public class RoomStateDto
{
    public int RoomId { get; set; }
    public double? CurrentTemperature { get; set; }
    public bool HeatingActive { get; set; }
    public int ActiveScheduleTrackId { get; set; }
    public string LastUpdated { get; set; } = string.Empty;
    public RoomCapabilities? Capabilities { get; set; }
}

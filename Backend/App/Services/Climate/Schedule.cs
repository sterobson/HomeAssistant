using System.Collections.Generic;
using System.Threading.Tasks;

namespace HomeAssistant.Services.Climate;

public class Boost
{
    public DateTimeOffset? StartTime { get; set; }
    public DateTimeOffset? EndTime { get; set; }
    public double? Temperature { get; set; }
}

public class RoomSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Func<bool> Condition { get; set; }
    public required List<HeatingScheduleTrack> ScheduleTracks { get; set; }
    public required Room Room { get; set; }
    public Boost Boost { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore]
    public Func<Task<double?>>? GetCurrentTemperature { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public Func<bool, Task<bool>>? OnToggleHeating { get; set; }
}

// Room state - separate from schedule configuration
public class RoomState
{
    public Guid RoomId { get; set; }
    public double? CurrentTemperature { get; set; }
    public bool HeatingActive { get; set; }
    public Guid? ActiveScheduleTrackId { get; set; }
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
}

[Flags]
public enum Days
{
    Unspecified = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64,
    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
    Weekends = Saturday | Sunday,
    NotSunday = Weekdays | Saturday,
    Everyday = Monday | Tuesday | Wednesday | Thursday | Friday | Saturday | Sunday
}

[Flags]
public enum ConditionType
{
    None = 0,
    PlentyOfPowerAvailable = 1,
    LowPowerAvailable = 2,
    RoomInUse = 4,
    RoomNotInUse = 8
}

[Flags]
public enum Room
{
    Kitchen = 0,
    GamesRoom = 1,
    DiningRoom = 2,
    Lounge = 4,
    DownstairsBathroom = 8,
    Bedroom1 = 16,
    Bedroom2 = 32,
    Bedroom3 = 64,
    UpstairsBathroom = 128
}

public enum ConditionOperatorType
{
    And,
    Or
}

public class HeatingScheduleTrack
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required double Temperature { get; set; }
    public required TimeOnly TargetTime { get; set; }
    public int RampUpMinutes { get; set; } = 30;
    public Days Days { get; set; } = Days.Unspecified;
    public ConditionType Conditions { get; set; } = ConditionType.None;
    public ConditionOperatorType ConditionOperator { get; set; } = ConditionOperatorType.Or;
}

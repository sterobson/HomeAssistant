using System.Collections.Generic;
using System.Threading.Tasks;

namespace HomeAssistant.Services.Climate;

internal class Schedule
{
    public required Func<bool> Condition { get; set; }
    public required List<HeatingScheduleTrack> ScheduleTracks { get; set; }
    public required Room Room { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public Func<Task<double?>>? GetCurrentTemperature { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public Func<bool, Task<bool>>? OnToggleHeating { get; set; }
}

[Flags]
internal enum Days
{
    Everyday = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64,
    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
    Weekends = Saturday | Sunday,
    NotSunday = Weekdays | Saturday
}

[Flags]
internal enum ConditionType
{
    None = 0,
    PlentyOfPowerAvailable = 1,
    LowPowerAvailable = 2,
    RoomInUse = 4,
    RoomNotInUse = 8
}

[Flags]
internal enum Room
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

internal enum ConditionOperatorType
{
    And,
    Or
}

internal class HeatingScheduleTrack
{
    public required double Temperature { get; set; }
    public required TimeOnly TargetTime { get; set; }
    public int RampUpMinutes { get; set; } = 30;
    public Days Days { get; set; } = Days.Everyday;
    public ConditionType Conditions { get; set; } = ConditionType.None;
    public ConditionOperatorType ConditionOperator { get; set; } = ConditionOperatorType.Or;
}

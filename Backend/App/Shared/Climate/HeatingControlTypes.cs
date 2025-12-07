namespace HomeAssistant.Shared.Climate;

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
    Schedule1 = 1,
    Schedule2 = 2,
    RoomInUse = 4,
    RoomNotInUse = 8
}

public enum HouseOccupancyState
{
    Home = 0,
    Away = 1
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
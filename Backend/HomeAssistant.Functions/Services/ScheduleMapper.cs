using HomeAssistant.Functions.Models;
using HomeAssistant.Services.Climate;

namespace HomeAssistant.Functions.Services;

public class ScheduleMapper
{
    public SchedulesResponse ToDto(List<RoomSchedule> schedules)
    {
        var rooms = new List<RoomDto>();

        foreach (RoomSchedule schedule in schedules)
        {
            var boostDto = new BoostDto
            {
                StartTime = schedule.Boost.StartTime?.ToString("O"),
                EndTime = schedule.Boost.EndTime?.ToString("O"),
                Temperature = schedule.Boost.Temperature
            };

            var roomDto = new RoomDto
            {
                Id = schedule.Id.ToString(),
                RoomType = (int)schedule.Room,
                Name = GetRoomName(schedule.Room),
                Boost = boostDto,
                Schedules = []
            };

            foreach (HeatingScheduleTrack track in schedule.ScheduleTracks)
            {
                roomDto.Schedules.Add(new ScheduleTrackDto
                {
                    Id = track.Id.ToString(),
                    Time = track.TargetTime.ToString("HH:mm"),
                    Temperature = track.Temperature,
                    Conditions = FormatConditions(track.Days, track.Conditions)
                });
            }

            rooms.Add(roomDto);
        }

        return new SchedulesResponse { Rooms = rooms };
    }

    public List<RoomSchedule> FromDto(SchedulesResponse dto)
    {
        var schedules = new List<RoomSchedule>();

        foreach (RoomDto roomDto in dto.Rooms)
        {
            var boost = new Boost();
            if (roomDto.Boost != null)
            {
                boost.StartTime = !string.IsNullOrEmpty(roomDto.Boost.StartTime)
                    ? DateTimeOffset.Parse(roomDto.Boost.StartTime)
                    : null;
                boost.EndTime = !string.IsNullOrEmpty(roomDto.Boost.EndTime)
                    ? DateTimeOffset.Parse(roomDto.Boost.EndTime)
                    : null;
                boost.Temperature = roomDto.Boost.Temperature;
            }

            var schedule = new RoomSchedule
            {
                Id = Guid.TryParse(roomDto.Id, out var scheduleId) ? scheduleId : Guid.NewGuid(),
                Room = (Room)roomDto.RoomType,
                Condition = () => true,
                Boost = boost,
                ScheduleTracks = []
            };

            foreach (ScheduleTrackDto trackDto in roomDto.Schedules)
            {
                var track = new HeatingScheduleTrack
                {
                    Id = Guid.TryParse(trackDto.Id, out var trackId) ? trackId : Guid.NewGuid(),
                    TargetTime = TimeOnly.Parse(trackDto.Time),
                    Temperature = trackDto.Temperature
                };

                // Parse conditions string
                ParseConditions(trackDto.Conditions, out Days days, out ConditionType conditionType);
                track.Days = days;
                track.Conditions = conditionType;

                schedule.ScheduleTracks.Add(track);
            }

            schedules.Add(schedule);
        }

        return schedules;
    }

    private static string GetRoomName(Room room)
    {
        return room switch
        {
            Room.Kitchen => "Kitchen",
            Room.GamesRoom => "Games Room",
            Room.DiningRoom => "Dining Room",
            Room.Lounge => "Lounge",
            Room.DownstairsBathroom => "Downstairs Bathroom",
            Room.Bedroom1 => "Bedroom 1",
            Room.Bedroom2 => "Bedroom 2",
            Room.Bedroom3 => "Bedroom 3",
            Room.UpstairsBathroom => "Upstairs Bathroom",
            _ => room.ToString()
        };
    }

    private static string FormatConditions(Days days, ConditionType conditions)
    {
        var parts = new List<string>();

        // Format days
        if (days != Days.Unspecified && days != Days.Everyday)
        {
            if (days.HasFlag(Days.Monday)) parts.Add("Mon");
            if (days.HasFlag(Days.Tuesday)) parts.Add("Tue");
            if (days.HasFlag(Days.Wednesday)) parts.Add("Wed");
            if (days.HasFlag(Days.Thursday)) parts.Add("Thu");
            if (days.HasFlag(Days.Friday)) parts.Add("Fri");
            if (days.HasFlag(Days.Saturday)) parts.Add("Sat");
            if (days.HasFlag(Days.Sunday)) parts.Add("Sun");
        }

        // Format conditions
        if (conditions.HasFlag(ConditionType.RoomInUse))
            parts.Add("Occupied");
        if (conditions.HasFlag(ConditionType.RoomNotInUse))
            parts.Add("Unoccupied");
        if (conditions.HasFlag(ConditionType.PlentyOfPowerAvailable))
            parts.Add("PlentyOfPower");
        if (conditions.HasFlag(ConditionType.LowPowerAvailable))
            parts.Add("LowPower");

        return string.Join(",", parts);
    }

    private static void ParseConditions(string conditionsStr, out Days days, out ConditionType conditions)
    {
        days = Days.Unspecified;
        conditions = ConditionType.None;

        if (string.IsNullOrWhiteSpace(conditionsStr))
            return;

        var parts = conditionsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            switch (part)
            {
                case "Mon": days |= Days.Monday; break;
                case "Tue": days |= Days.Tuesday; break;
                case "Wed": days |= Days.Wednesday; break;
                case "Thu": days |= Days.Thursday; break;
                case "Fri": days |= Days.Friday; break;
                case "Sat": days |= Days.Saturday; break;
                case "Sun": days |= Days.Sunday; break;
                case "Occupied": conditions |= ConditionType.RoomInUse; break;
                case "Unoccupied": conditions |= ConditionType.RoomNotInUse; break;
                case "PlentyOfPower": conditions |= ConditionType.PlentyOfPowerAvailable; break;
                case "LowPower": conditions |= ConditionType.LowPowerAvailable; break;
            }
        }
    }
}

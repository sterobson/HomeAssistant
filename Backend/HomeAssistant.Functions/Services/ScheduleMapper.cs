using HomeAssistant.Functions.Models;
using HomeAssistant.Services.Climate;

namespace HomeAssistant.Functions.Services;

public class ScheduleMapper
{
    public SchedulesResponse ToDto(List<RoomSchedule> schedules)
    {
        List<RoomDto> rooms = new List<RoomDto>();

        foreach (RoomSchedule schedule in schedules)
        {
            BoostDto boostDto = new BoostDto
            {
                StartTime = schedule.Boost.StartTime?.ToString("O"),
                EndTime = schedule.Boost.EndTime?.ToString("O"),
                Temperature = schedule.Boost.Temperature
            };

            RoomDto roomDto = new RoomDto
            {
                Id = schedule.Id.ToString(),
                Room = schedule.Room,
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
                    RampUpMinutes = track.RampUpMinutes,
                    Days = track.Days,
                    Conditions = track.Conditions,
                    ConditionOperator = track.ConditionOperator
                });
            }

            rooms.Add(roomDto);
        }

        return new SchedulesResponse { Rooms = rooms };
    }

    public List<RoomSchedule> FromDto(SchedulesResponse dto)
    {
        List<RoomSchedule> schedules = new List<RoomSchedule>();

        foreach (RoomDto roomDto in dto.Rooms)
        {
            Boost boost = new Boost();
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

            RoomSchedule schedule = new RoomSchedule
            {
                Id = Guid.TryParse(roomDto.Id, out var scheduleId) ? scheduleId : Guid.NewGuid(),
                Room = roomDto.Room,
                Boost = boost,
                ScheduleTracks = []
            };

            foreach (ScheduleTrackDto trackDto in roomDto.Schedules)
            {
                HeatingScheduleTrack track = new HeatingScheduleTrack
                {
                    Id = Guid.TryParse(trackDto.Id, out var trackId) ? trackId : Guid.NewGuid(),
                    TargetTime = TimeOnly.Parse(trackDto.Time),
                    Temperature = trackDto.Temperature,
                    RampUpMinutes = trackDto.RampUpMinutes,
                    Days = trackDto.Days,
                    Conditions = trackDto.Conditions,
                    ConditionOperator = trackDto.ConditionOperator
                };

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
}

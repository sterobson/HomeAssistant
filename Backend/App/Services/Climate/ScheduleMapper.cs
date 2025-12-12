using HomeAssistant.Services.Climate;
using HomeAssistant.Shared.Climate;
using System.Collections.Generic;

namespace HomeAssistant.Services;

/// <summary>
/// Maps between domain models and DTOs for schedules and room states
/// </summary>
public static class ScheduleMapper
{
    /// <summary>
    /// Maps from DTO to domain model
    /// </summary>
    public static RoomSchedules MapFromDto(RoomSchedulesDto dto)
    {
        if (dto == null)
            throw new ArgumentNullException(nameof(dto));

        RoomSchedules roomSchedules = new RoomSchedules
        {
            HouseOccupancyState = dto.HouseOccupancyState
        };

        List<RoomSchedule> schedules = [];

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

            RoomSchedule schedule = new()
            {
                Id = roomDto.Id,
                Name = roomDto.Name,
                Boost = boost,
                ScheduleTracks = []
            };

            foreach (ScheduleTrackDto trackDto in roomDto.Schedules)
            {
                HeatingScheduleTrack track = new()
                {
                    Id = trackDto.Id,
                    TargetTime = TimeOnly.Parse(trackDto.Time),
                    Temperature = trackDto.Temperature,
                    RampUpMinutes = trackDto.RampUpMinutes,
                    Days = trackDto.Days,
                    Conditions = trackDto.Conditions
                };

                schedule.ScheduleTracks.Add(track);
            }

            schedules.Add(schedule);
        }

        roomSchedules.Rooms = schedules;
        return roomSchedules;
    }

    /// <summary>
    /// Maps from domain model to DTO
    /// </summary>
    public static RoomSchedulesDto MapToDto(RoomSchedules schedules)
    {
        if (schedules == null)
            throw new ArgumentNullException(nameof(schedules));

        List<RoomDto> rooms = [];

        foreach (RoomSchedule schedule in schedules.Rooms)
        {
            BoostDto boostDto = new BoostDto
            {
                StartTime = schedule.Boost.StartTime?.ToString("O"),
                EndTime = schedule.Boost.EndTime?.ToString("O"),
                Temperature = schedule.Boost.Temperature
            };

            RoomDto roomDto = new RoomDto
            {
                Id = schedule.Id,
                Name = schedule.Name,
                Boost = boostDto,
                Schedules = []
            };

            foreach (HeatingScheduleTrack track in schedule.ScheduleTracks)
            {
                roomDto.Schedules.Add(new ScheduleTrackDto
                {
                    Id = track.Id,
                    Time = track.TargetTime.ToString("HH:mm"),
                    Temperature = track.Temperature,
                    RampUpMinutes = track.RampUpMinutes,
                    Days = track.Days,
                    Conditions = track.Conditions
                });
            }

            rooms.Add(roomDto);
        }

        return new RoomSchedulesDto
        {
            HouseOccupancyState = schedules.HouseOccupancyState,
            Rooms = rooms
        };
    }

    /// <summary>
    /// Maps room state from DTO to domain model
    /// </summary>
    public static RoomState MapRoomStateFromDto(RoomStateDto dto)
    {
        if (dto == null)
            throw new ArgumentNullException(nameof(dto));

        return new RoomState
        {
            RoomId = dto.RoomId,
            CurrentTemperature = dto.CurrentTemperature,
            HeatingActive = dto.HeatingActive,
            ActiveScheduleTrackId = dto.ActiveScheduleTrackId,
            LastUpdated = DateTimeOffset.Parse(dto.LastUpdated),
            Capabilities = dto.Capabilities
        };
    }

    /// <summary>
    /// Maps room state from domain model to DTO
    /// </summary>
    public static RoomStateDto MapRoomStateToDto(RoomState state)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));

        return new RoomStateDto
        {
            RoomId = state.RoomId,
            CurrentTemperature = state.CurrentTemperature,
            HeatingActive = state.HeatingActive,
            ActiveScheduleTrackId = state.ActiveScheduleTrackId,
            LastUpdated = state.LastUpdated.ToString("O"),
            Capabilities = state.Capabilities
        };
    }
}

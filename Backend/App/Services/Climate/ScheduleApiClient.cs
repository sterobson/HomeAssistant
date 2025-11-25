using HomeAssistant.Services.Climate;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace HomeAssistant.Services;

public interface IScheduleApiClient
{
    Task<List<RoomSchedule>> GetSchedulesAsync(Guid houseId);
    Task SetSchedulesAsync(Guid houseId, List<RoomSchedule> schedules);
    Task<List<RoomState>> GetRoomStatesAsync(Guid houseId);
    Task SetRoomStatesAsync(Guid houseId, List<RoomState> roomStates);
}

public class ScheduleApiClient : IScheduleApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ScheduleApiClient> _logger;

    public ScheduleApiClient(HttpClient httpClient, ILogger<ScheduleApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<RoomSchedule>> GetSchedulesAsync(Guid houseId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/schedules?houseId={houseId}");
            response.EnsureSuccessStatusCode();

            var schedulesResponse = await response.Content.ReadFromJsonAsync<SchedulesResponse>();
            if (schedulesResponse == null)
            {
                _logger.LogWarning("Received null response when getting schedules for house {HouseId}", houseId);
                return [];
            }

            return MapFromDto(schedulesResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting schedules from API for house {HouseId}", houseId);
            throw;
        }
    }

    public async Task SetSchedulesAsync(Guid houseId, List<RoomSchedule> schedules)
    {
        try
        {
            var dto = MapToDto(schedules);
            var json = JsonSerializer.Serialize(dto);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"/api/schedules?houseId={houseId}", content);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("Successfully set schedules for house {HouseId}", houseId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting schedules to API for house {HouseId}", houseId);
            throw;
        }
    }

    public async Task<List<RoomState>> GetRoomStatesAsync(Guid houseId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/room-states?houseId={houseId}");
            response.EnsureSuccessStatusCode();

            var statesResponse = await response.Content.ReadFromJsonAsync<RoomStatesResponse>();
            if (statesResponse == null)
            {
                _logger.LogWarning("Received null response when getting room states for house {HouseId}", houseId);
                return [];
            }

            return statesResponse.RoomStates.Select(MapRoomStateFromDto).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting room states from API for house {HouseId}", houseId);
            throw;
        }
    }

    public async Task SetRoomStatesAsync(Guid houseId, List<RoomState> roomStates)
    {
        try
        {
            var dto = new RoomStatesResponse
            {
                RoomStates = roomStates.Select(MapRoomStateToDto).ToList()
            };

            var json = JsonSerializer.Serialize(dto);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"/api/room-states?houseId={houseId}", content);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("Successfully set room states for house {HouseId}", houseId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting room states to API for house {HouseId}", houseId);
            throw;
        }
    }

    // Mapping methods
    private List<RoomSchedule> MapFromDto(SchedulesResponse dto)
    {
        var schedules = new List<RoomSchedule>();

        foreach (var roomDto in dto.Rooms)
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

            foreach (var trackDto in roomDto.Schedules)
            {
                var track = new HeatingScheduleTrack
                {
                    Id = Guid.TryParse(trackDto.Id, out var trackId) ? trackId : Guid.NewGuid(),
                    TargetTime = TimeOnly.Parse(trackDto.Time),
                    Temperature = trackDto.Temperature
                };

                // Parse conditions
                ParseConditions(trackDto.Conditions, out Days days, out ConditionType conditionType);
                track.Days = days;
                track.Conditions = conditionType;

                schedule.ScheduleTracks.Add(track);
            }

            schedules.Add(schedule);
        }

        return schedules;
    }

    private SchedulesResponse MapToDto(List<RoomSchedule> schedules)
    {
        var rooms = new List<RoomDto>();

        foreach (var schedule in schedules)
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

            foreach (var track in schedule.ScheduleTracks)
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

    private RoomState MapRoomStateFromDto(RoomStateDto dto)
    {
        return new RoomState
        {
            RoomId = Guid.TryParse(dto.RoomId, out var roomId) ? roomId : Guid.Empty,
            CurrentTemperature = dto.CurrentTemperature,
            HeatingActive = dto.HeatingActive,
            ActiveScheduleTrackId = Guid.TryParse(dto.ActiveScheduleTrackId, out var trackId) ? trackId : null,
            LastUpdated = DateTimeOffset.Parse(dto.LastUpdated)
        };
    }

    private RoomStateDto MapRoomStateToDto(RoomState state)
    {
        return new RoomStateDto
        {
            RoomId = state.RoomId.ToString(),
            CurrentTemperature = state.CurrentTemperature,
            HeatingActive = state.HeatingActive,
            ActiveScheduleTrackId = state.ActiveScheduleTrackId?.ToString(),
            LastUpdated = state.LastUpdated.ToString("O")
        };
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

// DTOs matching the Azure Functions responses
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

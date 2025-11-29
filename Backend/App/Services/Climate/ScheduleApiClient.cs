using HomeAssistant.JsonConverters;
using HomeAssistant.Services.Climate;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

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

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new FlexibleEnumConverterFactory() }
    };

    public ScheduleApiClient(HttpClient httpClient, ILogger<ScheduleApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<RoomSchedule>> GetSchedulesAsync(Guid houseId)
    {
        try
        {
            HttpResponseMessage response = await _httpClient.GetAsync($"/api/schedules?houseId={houseId}");
            response.EnsureSuccessStatusCode();

            SchedulesResponse? schedulesResponse = await response.Content.ReadFromJsonAsync<SchedulesResponse>(_jsonOptions);
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
            SchedulesResponse dto = MapToDto(schedules);
            var json = JsonSerializer.Serialize(dto, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await _httpClient.PostAsync($"/api/schedules?houseId={houseId}", content);
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
            HttpResponseMessage response = await _httpClient.GetAsync($"/api/room-states?houseId={houseId}");
            response.EnsureSuccessStatusCode();

            RoomStatesResponse? statesResponse = await response.Content.ReadFromJsonAsync<RoomStatesResponse>(_jsonOptions);
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

            var json = JsonSerializer.Serialize(dto, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await _httpClient.PostAsync($"/api/room-states?houseId={houseId}", content);
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
                Id = Guid.TryParse(roomDto.Id, out Guid scheduleId) ? scheduleId : Guid.NewGuid(),
                Room = roomDto.Room,
                Boost = boost,
                ScheduleTracks = []
            };

            foreach (ScheduleTrackDto trackDto in roomDto.Schedules)
            {
                var track = new HeatingScheduleTrack
                {
                    Id = Guid.TryParse(trackDto.Id, out Guid trackId) ? trackId : Guid.NewGuid(),
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

    private SchedulesResponse MapToDto(List<RoomSchedule> schedules)
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

    private RoomState MapRoomStateFromDto(RoomStateDto dto)
    {
        return new RoomState
        {
            RoomId = Guid.TryParse(dto.RoomId, out Guid roomId) ? roomId : Guid.Empty,
            CurrentTemperature = dto.CurrentTemperature,
            HeatingActive = dto.HeatingActive,
            ActiveScheduleTrackId = Guid.TryParse(dto.ActiveScheduleTrackId, out Guid trackId) ? trackId : null,
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
}

// DTOs matching the Azure Functions responses
public class SchedulesResponse
{
    public List<RoomDto> Rooms { get; set; } = [];
}

public class RoomDto
{
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("room")]
    public Room Room { get; set; }

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
    public int RampUpMinutes { get; set; }
    public Days Days { get; set; }
    public ConditionType Conditions { get; set; }
    public ConditionOperatorType ConditionOperator { get; set; }
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

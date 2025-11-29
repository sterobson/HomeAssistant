using HomeAssistant.JsonConverters;
using HomeAssistant.Services.Climate;
using HomeAssistant.Shared.Climate;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace HomeAssistant.Services;

public interface IScheduleApiClient
{
    Task<List<RoomSchedule>> GetSchedulesAsync(string houseId);
    Task SetSchedulesAsync(string houseId, List<RoomSchedule> schedules);
    Task<List<RoomState>> GetRoomStatesAsync(string houseId);
    Task SetRoomStatesAsync(string houseId, List<RoomState> roomStates);
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

    public async Task<List<RoomSchedule>> GetSchedulesAsync(string houseId)
    {
        try
        {
            HttpResponseMessage response = await _httpClient.GetAsync($"/api/schedules?houseId={houseId}");
            response.EnsureSuccessStatusCode();

            RoomSchedulesDto? schedulesResponse = await response.Content.ReadFromJsonAsync<RoomSchedulesDto>(_jsonOptions);
            if (schedulesResponse == null)
            {
                _logger.LogWarning("Received null response when getting schedules for house {HouseId}", houseId);
                return [];
            }

            return ScheduleMapper.MapFromDto(schedulesResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting schedules from API for house {HouseId}", houseId);
            throw;
        }
    }

    public async Task SetSchedulesAsync(string houseId, List<RoomSchedule> schedules)
    {
        try
        {
            RoomSchedulesDto dto = ScheduleMapper.MapToDto(schedules);
            string json = JsonSerializer.Serialize(dto, _jsonOptions);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

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

    public async Task<List<RoomState>> GetRoomStatesAsync(string houseId)
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

            return statesResponse.RoomStates.Select(ScheduleMapper.MapRoomStateFromDto).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting room states from API for house {HouseId}", houseId);
            throw;
        }
    }

    public async Task SetRoomStatesAsync(string houseId, List<RoomState> roomStates)
    {
        try
        {
            RoomStatesResponse dto = new RoomStatesResponse
            {
                RoomStates = roomStates.Select(ScheduleMapper.MapRoomStateToDto).ToList()
            };

            string json = JsonSerializer.Serialize(dto, _jsonOptions);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

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

}
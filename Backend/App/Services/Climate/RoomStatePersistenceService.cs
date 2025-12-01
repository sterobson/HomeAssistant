using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HomeAssistant.Services.Climate;

/// <summary>
/// Handles persistence of room states to Azure API
/// Note: Azure Function automatically broadcasts SignalR notifications when states are updated
/// </summary>
public interface IRoomStatePersistenceService
{
    /// <summary>
    /// Downloads current room states from Azure API
    /// </summary>
    Task<Dictionary<int, RoomState>?> GetStatesAsync();

    /// <summary>
    /// Uploads room states to Azure API
    /// The Azure Function will automatically broadcast a SignalR "RoomStatesUpdated" notification to all connected clients
    /// </summary>
    Task SetStatesAsync(Dictionary<int, RoomState> states);
}

public class RoomStatePersistenceService : IRoomStatePersistenceService
{
    private readonly ILogger<RoomStatePersistenceService> _logger;
    private readonly IScheduleApiClient? _scheduleApiClient;
    private readonly WebSynchronisationConfiguration _configuration;

    public RoomStatePersistenceService(
        ILogger<RoomStatePersistenceService> logger,
        WebSynchronisationConfiguration configuration,
        IScheduleApiClient? scheduleApiClient = null)
    {
        _logger = logger;
        _configuration = configuration;
        _scheduleApiClient = scheduleApiClient;
    }

    public async Task<Dictionary<int, RoomState>?> GetStatesAsync()
    {
        if (_scheduleApiClient == null || string.IsNullOrEmpty(_configuration.HouseId))
        {
            _logger.LogDebug("Cannot get room states - API client or HouseId not configured");
            return null;
        }

        try
        {
            _logger.LogInformation("Downloading room states from API for house {HouseId}", _configuration.HouseId);
            List<RoomState> statesList = await _scheduleApiClient.GetRoomStatesAsync(_configuration.HouseId);

            if (statesList.Count == 0)
            {
                _logger.LogInformation("No room states returned from API for house {HouseId}", _configuration.HouseId);
                return [];
            }

            Dictionary<int, RoomState> states = statesList.ToDictionary(s => s.RoomId);
            _logger.LogInformation("Successfully downloaded {Count} room states from API", states.Count);
            return states;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading room states from API for house {HouseId}", _configuration.HouseId);
            return null;
        }
    }

    public async Task SetStatesAsync(Dictionary<int, RoomState> states)
    {
        if (_scheduleApiClient == null || string.IsNullOrEmpty(_configuration.HouseId))
        {
            _logger.LogWarning("Cannot upload room states - API client or HouseId not configured");
            return;
        }

        try
        {
            List<RoomState> statesList = states.Values.ToList();
            await _scheduleApiClient.SetRoomStatesAsync(_configuration.HouseId, statesList);
            _logger.LogInformation("Successfully uploaded room states to API for house {HouseId} (SignalR notification will be broadcast automatically)", _configuration.HouseId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading room states to API for house {HouseId}", _configuration.HouseId);
            throw;
        }
    }
}

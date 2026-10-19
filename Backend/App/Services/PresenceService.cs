using HomeAssistant.apps;
using HomeAssistant.Shared.Climate;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HomeAssistant.Services;

internal class PresenceService : IPresenceService
{
    private readonly NamedEntities _namedEntities;
    private readonly HistoryService _historyService;

    public PresenceService(IHaContext ha, NamedEntities namedEntities, HistoryService historyService, ILogger<PresenceService> logger)
    {
        _namedEntities = namedEntities;
        _historyService = historyService;

        EntityExistenceValidator.ValidateEntities(ha, logger,
            namedEntities.GamesRoomDeskPlugOnOff.EntityId,
            namedEntities.GamesRoomDeskPlugPower.EntityId,
            namedEntities.DiningRoomDeskPlugOnOff.EntityId,
            namedEntities.DiningRoomDeskPlugPower.EntityId);
    }

    public async Task<bool> IsRoomInUse(string roomName)
    {
        DateTime now = DateTime.Now;
        switch (roomName.ToLower().Trim())
        {
            // Presence is measured from desk plug power draw, so it belongs to the desk
            // zones only. The radiator zones in the same rooms ("dining room", "den")
            // deliberately have no presence: a person at the desk should not make the
            // whole room's radiator come on.
            case "games desk":
                if (_namedEntities.GamesRoomDeskPlugOnOff.IsOn())
                {
                    // Get the power over the last 5 minutes.
                    IReadOnlyList<NumericHistoryEntry> histories = await _historyService.GetEntityNumericHistory(_namedEntities.GamesRoomDeskPlugPower.EntityId, now.AddMinutes(-5), now);
                    if (histories.Count > 0 && histories.Max(h => h.State) >= 40)
                    {
                        return true;
                    }
                }
                break;

            case "dining desk":
                if (_namedEntities.DiningRoomDeskPlugOnOff.IsOn())
                {
                    // Get the power over the last 5 minutes.
                    IReadOnlyList<NumericHistoryEntry> histories = await _historyService.GetEntityNumericHistory(_namedEntities.DiningRoomDeskPlugPower.EntityId, now.AddMinutes(-5), now);
                    if (histories.Count > 0 && histories.Max(h => h.State) >= 40)
                    {
                        return true;
                    }
                }
                break;
        }

        return false;
    }

    public bool CanDetectIfRoomInUse(string roomName)
    {
        return GetPresenceDevices(roomName).Count != 0;
    }

    public IReadOnlyList<(string EntityId, string Role)> GetPresenceDevices(string roomName)
    {
        // Must agree with IsRoomInUse above: these are the entities it reads.
        return roomName.ToLower().Trim() switch
        {
            "games desk" =>
            [
                (_namedEntities.GamesRoomDeskPlugOnOff.EntityId, RoomDeviceRoles.DeskPlug),
                (_namedEntities.GamesRoomDeskPlugPower.EntityId, RoomDeviceRoles.DeskPower)
            ],
            "dining desk" =>
            [
                (_namedEntities.DiningRoomDeskPlugOnOff.EntityId, RoomDeviceRoles.DeskPlug),
                (_namedEntities.DiningRoomDeskPlugPower.EntityId, RoomDeviceRoles.DeskPower)
            ],
            _ => []
        };
    }

}

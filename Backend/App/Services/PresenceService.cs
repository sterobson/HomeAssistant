using HomeAssistant.apps;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HomeAssistant.Services;

internal class PresenceService : IPresenceService
{
    private readonly NamedEntities _namedEntities;
    private readonly HistoryService _historyService;

    public PresenceService(NamedEntities namedEntities, HistoryService historyService)
    {
        _namedEntities = namedEntities;
        _historyService = historyService;
    }

    public async Task<bool> IsRoomInUse(string roomName)
    {
        DateTime now = DateTime.Now;
        switch (roomName.ToLower().Trim())
        {
            case "games room":
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

            case "dining room":
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
}

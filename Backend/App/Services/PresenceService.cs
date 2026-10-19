using HomeAssistant.apps;
using HomeAssistant.Shared.Climate;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HomeAssistant.Services;

internal class PresenceService : IPresenceService
{
    /// <summary>A desk is in use once its plug has drawn this much within <see cref="InUseLookBack"/>.</summary>
    internal const double DeskInUseWatts = 40;

    /// <summary>
    /// The den is in use while its TV draws this much. The same threshold as the Home Assistant
    /// automations that cut the den's motion lights while the TV is on (tools/ha/automations).
    /// Its standby draw is about 4 W; just after its plug is switched on it draws ~40 W for a couple
    /// of minutes, which counts as in use.
    /// </summary>
    internal const double DenTvInUseWatts = 30;

    /// <summary>The living room TV counts as on at this draw, held for <see cref="LivingRoomTvSteadyFor"/>.</summary>
    internal const double LivingRoomTvInUseWatts = 30;

    /// <summary>
    /// The living room TV must have drawn <see cref="LivingRoomTvInUseWatts"/> for this long, unbroken.
    /// Unlike the den's look-back there is no hold: presence ends once the draw has been below the
    /// threshold for a moment.
    /// </summary>
    internal static readonly TimeSpan LivingRoomTvSteadyFor = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How far back a power reading still counts for the desks and the den. It doubles as a hold: a zone
    /// stays in use for this long after the draw stops, so a brief dip does not flip the schedule.
    /// </summary>
    private static readonly TimeSpan InUseLookBack = TimeSpan.FromMinutes(5);

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
            namedEntities.DiningRoomDeskPlugPower.EntityId,
            namedEntities.DenTvPlugPower.EntityId,
            namedEntities.LivingRoomTvPlugPower.EntityId,
            namedEntities.LivingRoomStickLightsPlugOnOff.EntityId);
    }

    public async Task<bool> IsRoomInUse(string roomName)
    {
        // Presence is measured from plugs. The desk zones use their desk plug; the den uses its TV;
        // the living room uses its TV or its stick lights being switched on. The dining room zone
        // deliberately has no presence: a person at Ruth's desk should not make the whole room's
        // radiator come on. The den and living room are different because a TV or the room's lights
        // mean the room itself is being used, not a desk in it.
        //
        // The TVs use their power reading alone (owner, 2026-10-19): a TV plug switched off reads 0 W
        // anyway, so also checking its switch added a second "presence device" and nothing else.
        return roomName.ToLower().Trim() switch
        {
            "games desk" => _namedEntities.GamesRoomDeskPlugOnOff.IsOn() && await IsDrawing(_namedEntities.GamesRoomDeskPlugPower, DeskInUseWatts),
            "dining desk" => _namedEntities.DiningRoomDeskPlugOnOff.IsOn() && await IsDrawing(_namedEntities.DiningRoomDeskPlugPower, DeskInUseWatts),
            "den" => await IsDrawing(_namedEntities.DenTvPlugPower, DenTvInUseWatts),
            "living room" => _namedEntities.LivingRoomStickLightsPlugOnOff.IsOn()
                || await IsDrawingSteadily(_namedEntities.LivingRoomTvPlugPower, LivingRoomTvInUseWatts, LivingRoomTvSteadyFor),
            _ => false
        };
    }

    /// <summary>Must list exactly the zones with an arm in IsRoomInUse and GetPresenceDevices.</summary>
    public IReadOnlyList<string> PresenceZones { get; } = ["games desk", "dining desk", "den", "living room"];

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
            "den" =>
            [
                (_namedEntities.DenTvPlugPower.EntityId, RoomDeviceRoles.TvPower)
            ],
            "living room" =>
            [
                (_namedEntities.LivingRoomTvPlugPower.EntityId, RoomDeviceRoles.TvPower),
                (_namedEntities.LivingRoomStickLightsPlugOnOff.EntityId, RoomDeviceRoles.LightsPlug)
            ],
            _ => []
        };
    }

    /// <summary>
    /// True when the power reached <paramref name="thresholdWatts"/> at any point within
    /// <see cref="InUseLookBack"/>. Recorder history includes the reading in force at the start of the
    /// window, so a steady draw with no recent change still counts.
    /// </summary>
    private async Task<bool> IsDrawing(ICustomNumericSensorEntity power, double thresholdWatts)
    {
        DateTime now = DateTime.Now;
        IReadOnlyList<NumericHistoryEntry> histories = await _historyService.GetEntityNumericHistory(power.EntityId, now.Subtract(InUseLookBack), now);
        return histories.Count > 0 && histories.Max(h => h.State) >= thresholdWatts;
    }

    /// <summary>
    /// True when the power has been at or above <paramref name="thresholdWatts"/> for the whole of the last
    /// <paramref name="steadyFor"/>: every reading in that window, including the one in force at its start,
    /// meets the threshold. A non-numeric reading (unavailable) counts as 0 W.
    /// </summary>
    private async Task<bool> IsDrawingSteadily(ICustomNumericSensorEntity power, double thresholdWatts, TimeSpan steadyFor)
    {
        DateTime now = DateTime.Now;
        IReadOnlyList<NumericHistoryEntry> histories = await _historyService.GetEntityNumericHistory(power.EntityId, now.Subtract(steadyFor), now);
        return histories.Count > 0 && histories.All(h => h.State >= thresholdWatts);
    }
}

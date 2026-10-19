using HomeAssistant.apps;
using HomeAssistant.Shared.Climate;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HomeAssistant.Services;

/// <summary>A presence detector: one way of telling, from plugs, that someone is using something.</summary>
internal sealed record PresenceDetector(
    string Id,
    string Label,
    bool MeansSomeoneHome,
    IReadOnlyList<(string EntityId, string Role)> Devices,
    Func<Task<bool>> IsActiveAsync);

/// <summary>Presence worked out once for an evaluation pass: each zone, "house", and each detector.</summary>
internal sealed record PresenceSnapshot(IReadOnlyDictionary<string, bool> Zones, bool SomeoneHome, IReadOnlyDictionary<string, bool> Detectors);

/// <summary>
/// Room presence. The detectors (what counts as in use, with their thresholds) are fixed here in code; which of them
/// count for each zone is configured from the web UI (owner, 2026-10-19) and saved in device settings as
/// <c>heating.zoneOccupancy</c>. A zone is in use while any of its detectors is. Until anything is saved,
/// <see cref="DefaultZoneDetectors"/> applies, which is what the zones had before it was configurable.
/// "House" means any detector that shows someone is there is active, whichever zone it belongs to.
/// </summary>
internal class PresenceService : IPresenceService
{
    /// <summary>Ste's desk is in use once its plug has drawn this much within <see cref="InUseLookBack"/>.</summary>
    internal const double DeskInUseWatts = 40;

    /// <summary>
    /// Ruth's desk is in use while its plug is on and has drawn this much for the whole of
    /// <see cref="DiningDeskSteadyFor"/> (owner, 2026-10-19). No hold: it ends as soon as the draw drops.
    /// </summary>
    internal const double DiningDeskInUseWatts = 30;

    internal static readonly TimeSpan DiningDeskSteadyFor = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The den's TV is on while it draws this much. The same threshold as the Home Assistant
    /// automations that cut the den's motion lights while the TV is on (tools/ha/automations).
    /// Its standby draw is about 4 W; just after its plug is switched on it draws ~40 W for a couple
    /// of minutes, which counts as on.
    /// </summary>
    internal const double DenTvInUseWatts = 30;

    /// <summary>The living room TV counts as on at this draw, held for <see cref="LivingRoomTvSteadyFor"/>.</summary>
    internal const double LivingRoomTvInUseWatts = 30;

    /// <summary>
    /// The living room TV must have drawn <see cref="LivingRoomTvInUseWatts"/> for this long, unbroken.
    /// Unlike the den's look-back there is no hold: it ends once the draw has been below the threshold for a moment.
    /// </summary>
    internal static readonly TimeSpan LivingRoomTvSteadyFor = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How far back a power reading still counts for Ste's desk and the den's TV. It doubles as a hold: the detector
    /// stays active for this long after the draw stops, so a brief dip does not flip the schedule.
    /// </summary>
    private static readonly TimeSpan InUseLookBack = TimeSpan.FromMinutes(5);

    internal const string GamesDeskInUse = "games-desk-in-use";
    internal const string DiningDeskInUse = "dining-desk-in-use";
    internal const string DiningDeskSwitchedOn = "dining-desk-switched-on";
    // "On recently" keeps its original id, so a saved choice that names it still means the same thing.
    internal const string DenTvOn = "den-tv-on";
    internal const string DenTvOnNow = "den-tv-on-now";

    /// <summary>
    /// The den TV is on right now once it has drawn <see cref="DenTvInUseWatts"/> for this long, unbroken: the same
    /// rule as the HA automations that switch the den's motion lights off while the TV is on (tools/ha/automations).
    /// </summary>
    internal static readonly TimeSpan DenTvSteadyFor = TimeSpan.FromSeconds(5);
    internal const string LivingRoomTvOn = "living-room-tv-on";
    internal const string LivingRoomLightsOn = "living-room-lights-on";

    /// <summary>
    /// Which detectors count for each zone until the owner saves their own: exactly what each zone had when this
    /// became configurable. Ruth's desk plug gives two detectors (owner, 2026-10-19): her desk zone needs it in
    /// use, the dining room radiator zone only needs it switched on. The TVs go by their power alone: a TV plug
    /// switched off reads 0 W anyway.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DefaultZoneDetectors =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["games desk"] = [GamesDeskInUse],
            ["dining desk"] = [DiningDeskInUse],
            ["dining room"] = [DiningDeskSwitchedOn],
            ["den"] = [DenTvOn],
            ["living room"] = [LivingRoomTvOn, LivingRoomLightsOn]
        };

    private readonly HistoryService _historyService;
    private readonly IReadOnlyList<PresenceDetector> _detectors;
    private readonly Dictionary<string, PresenceDetector> _detectorsById;
    private IReadOnlyDictionary<string, IReadOnlyList<string>> _zoneDetectors = DefaultZoneDetectors;

    public PresenceService(IHaContext ha, NamedEntities namedEntities, HistoryService historyService, ILogger<PresenceService> logger)
    {
        _historyService = historyService;

        _detectors =
        [
            new(GamesDeskInUse, "Ste's desk in use (plug on, 40 W or more in the last 5 minutes)", true,
                [(namedEntities.GamesRoomDeskPlugOnOff.EntityId, RoomDeviceRoles.DeskPlug), (namedEntities.GamesRoomDeskPlugPower.EntityId, RoomDeviceRoles.DeskPower)],
                async () => namedEntities.GamesRoomDeskPlugOnOff.IsOn() && await IsDrawing(namedEntities.GamesRoomDeskPlugPower, DeskInUseWatts)),
            new(DiningDeskInUse, "Ruth's desk in use (plug on, 30 W or more for 5 seconds)", true,
                [(namedEntities.DiningRoomDeskPlugOnOff.EntityId, RoomDeviceRoles.DeskPlug), (namedEntities.DiningRoomDeskPlugPower.EntityId, RoomDeviceRoles.DeskPower)],
                async () => namedEntities.DiningRoomDeskPlugOnOff.IsOn()
                    && await IsDrawingSteadily(namedEntities.DiningRoomDeskPlugPower, DiningDeskInUseWatts, DiningDeskSteadyFor)),
            // Says the desk is set up, not that anyone is at it, so it does not count towards "house".
            new(DiningDeskSwitchedOn, "Ruth's desk switched on", false,
                [(namedEntities.DiningRoomDeskPlugOnOff.EntityId, RoomDeviceRoles.DeskPlug)],
                () => Task.FromResult(namedEntities.DiningRoomDeskPlugOnOff.IsOn())),
            // Two from the den TV (owner, 2026-10-19): "recently" holds for 5 minutes after the TV goes off, so the
            // heating does not drop the moment it does; "now" has no hold, like the motion lights' automations.
            // Only "now" counts towards "house": a TV that was on a few minutes ago does not mean anyone is home.
            new(DenTvOn, "Den TV on recently (30 W or more in the last 5 minutes)", false,
                [(namedEntities.DenTvPlugPower.EntityId, RoomDeviceRoles.TvPower)],
                () => IsDrawing(namedEntities.DenTvPlugPower, DenTvInUseWatts)),
            new(DenTvOnNow, "Den TV on now (30 W or more for 5 seconds)", true,
                [(namedEntities.DenTvPlugPower.EntityId, RoomDeviceRoles.TvPower)],
                () => IsDrawingSteadily(namedEntities.DenTvPlugPower, DenTvInUseWatts, DenTvSteadyFor)),
            new(LivingRoomTvOn, "Living room TV on (30 W or more for 5 seconds)", true,
                [(namedEntities.LivingRoomTvPlugPower.EntityId, RoomDeviceRoles.TvPower)],
                () => IsDrawingSteadily(namedEntities.LivingRoomTvPlugPower, LivingRoomTvInUseWatts, LivingRoomTvSteadyFor)),
            // Follows the WiZ lamp through two HA automations, so it inherits that lamp's lag (HOME-ASSISTANT.md gotcha 15).
            new(LivingRoomLightsOn, "Living room stick lights on", true,
                [(namedEntities.LivingRoomStickLightsPlugOnOff.EntityId, RoomDeviceRoles.LightsPlug)],
                () => Task.FromResult(namedEntities.LivingRoomStickLightsPlugOnOff.IsOn()))
        ];
        _detectorsById = _detectors.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);

        EntityExistenceValidator.ValidateEntities(ha, logger,
            [.. _detectors.SelectMany(d => d.Devices.Select(device => device.EntityId)).Distinct()]);
    }

    public IReadOnlyList<PresenceDetectorDto> Detectors =>
        [.. _detectors.Select(d => new PresenceDetectorDto { Id = d.Id, Label = d.Label, MeansSomeoneHome = d.MeansSomeoneHome })];

    public bool ZoneOccupancyIsDefault { get; private set; } = true;

    /// <summary>
    /// The owner's saved choice of detectors per zone, or null for the defaults. Once saved it is the whole truth:
    /// a zone it leaves out has no presence. Unknown detector ids are ignored.
    /// </summary>
    public void UseZoneOccupancy(IReadOnlyDictionary<string, List<string>>? saved)
    {
        ZoneOccupancyIsDefault = saved == null;
        _zoneDetectors = saved == null
            ? DefaultZoneDetectors
            : saved.ToDictionary(
                zone => zone.Key.Trim(),
                zone => (IReadOnlyList<string>)[.. zone.Value.Where(_detectorsById.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase)],
                StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> DetectorsFor(string roomName) =>
        _zoneDetectors.TryGetValue(roomName.Trim(), out IReadOnlyList<string>? ids) ? ids : [];

    /// <summary>The zones with at least one detector.</summary>
    public IReadOnlyList<string> PresenceZones => [.. _zoneDetectors.Where(zone => zone.Value.Count > 0).Select(zone => zone.Key)];

    public async Task<bool> IsRoomInUse(string roomName)
    {
        foreach (string id in DetectorsFor(roomName))
        {
            if (await _detectorsById[id].IsActiveAsync())
            {
                return true;
            }
        }

        return false;
    }

    public bool CanDetectIfRoomInUse(string roomName) => DetectorsFor(roomName).Count != 0;

    public IReadOnlyList<(string EntityId, string Role)> GetPresenceDevices(string roomName) =>
        [.. DetectorsFor(roomName).SelectMany(id => _detectorsById[id].Devices).Distinct()];

    /// <summary>
    /// Every detector checked once, then each zone (any of its detectors) and "house" (any detector that shows
    /// someone is there). A detector whose check fails counts as inactive.
    /// </summary>
    public async Task<PresenceSnapshot> GetSnapshotAsync()
    {
        Dictionary<string, bool> detectors = new(StringComparer.OrdinalIgnoreCase);
        foreach (PresenceDetector detector in _detectors)
        {
            try
            {
                detectors[detector.Id] = await detector.IsActiveAsync();
            }
            catch
            {
                detectors[detector.Id] = false;
            }
        }

        Dictionary<string, bool> zones = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string zone, IReadOnlyList<string> ids) in _zoneDetectors)
        {
            zones[zone] = ids.Any(id => detectors[id]);
        }

        bool someoneHome = _detectors.Any(d => d.MeansSomeoneHome && detectors[d.Id]);
        return new PresenceSnapshot(zones, someoneHome, detectors);
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

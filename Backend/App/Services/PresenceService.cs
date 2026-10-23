using HomeAssistant.apps;
using HomeAssistant.Shared.Climate;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace HomeAssistant.Services;

/// <summary>A presence detector: one way of telling, from plugs or the router, that someone is there or using something.</summary>
internal sealed record PresenceDetector(
    string Id,
    string Label,
    bool MeansSomeoneHome,
    IReadOnlyList<(string EntityId, string Role)> Devices,
    Func<Task<bool>> IsActiveAsync);

/// <summary>A phone whose presence on the home Wi-Fi is a detector. <see cref="DeviceId"/> is the app's own, not Home Assistant's.</summary>
internal sealed record PresencePhone(string DetectorId, string DeviceId, string Name, string RouterId);

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
    /// Ruth's desk comes into use once its plug is on and has drawn this much for the whole of
    /// <see cref="DiningDeskSteadyFor"/> (owner, 2026-10-19), and stays in use until the draw has been under it for
    /// <see cref="DiningDeskHold"/>. Switching the plug off ends it at once.
    /// </summary>
    internal const double DiningDeskInUseWatts = 30;

    internal static readonly TimeSpan DiningDeskSteadyFor = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long Ruth's desk stays in use after its draw drops under <see cref="DiningDeskInUseWatts"/> (owner,
    /// 2026-10-22). Until then there was no hold, and the dip under 30 W as her equipment starts up switched her heater
    /// off for half a minute every time the desk was switched on (HOME-ASSISTANT.md, gotcha 21).
    /// </summary>
    internal static readonly TimeSpan DiningDeskHold = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The den's TV comes on once it draws this much, and stays on until it is down to <see cref="DenTvOffWatts"/>
    /// (a <see cref="PowerLatch"/>; owner, 2026-10-22). The same pair as the Home Assistant automations that cut the
    /// den's motion lights while the TV is on (tools/ha/automations). Standby is about 4 W. Watching YouTube it drew
    /// anywhere from 73 to 160 W (2026-10-22), so a single 100 W threshold flapped. It was a single 30 W until
    /// 2026-10-22, when the plug drew 35–80 W on its own at night (about 40 W for a minute or two most hours, and
    /// 55–80 W for 35 minutes around 3 am), so the den counted as in use and its 21 °C track started the boiler.
    /// </summary>
    internal const double DenTvInUseWatts = 100;

    /// <summary>The den's TV is off again once it draws this little; see <see cref="DenTvInUseWatts"/>.</summary>
    internal const double DenTvOffWatts = 30;

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
    internal const string StePhoneHome = "ste-phone-home";
    internal const string RuthPhoneHome = "ruth-phone-home";
    internal const string EllePhoneHome = "ellie-phone-home";

    /// <summary>
    /// Every device the Linksys Velop router lists as online, in its <c>devices</c> attribute (each with the router's
    /// own <c>id</c>). Phones are looked for here rather than through the integration's device trackers, which see
    /// nothing of a phone joined to a secondary Velop unit (HOME-ASSISTANT.md, router note).
    /// </summary>
    internal const string RouterOnlineDevices = "sensor.mesh_online_devices";

    /// <summary>The router's id for Ste's phone (it gives no name, so the router lists it as "unknown").</summary>
    internal const string StePhoneRouterId = "ce8b9c19-dd29-4250-8b90-6fccf8cef4b9";

    /// <summary>
    /// The router's id for Ruth's phone, listed as "Android_SBFUAFP4". Found 2026-10-20 as the one device that came
    /// online when she woke (her phone is in flight mode overnight, so this reads "away" while she sleeps).
    /// </summary>
    internal const string RuthPhoneRouterId = "ec6744ee-6707-4f8d-8fa4-6de13650bdb7";

    /// <summary>The router's id for Ellie's phone, listed as "Android_17YGONY5".</summary>
    internal const string EllePhoneRouterId = "0461c717-bf51-4b13-b17a-f58c2e0cada0";

    /// <summary>The phones the router can see, each with the id it has as a device in a zone's Devices list.</summary>
    internal static readonly IReadOnlyList<PresencePhone> Phones =
    [
        new(StePhoneHome, "phone.ste", "Ste's phone", StePhoneRouterId),
        new(RuthPhoneHome, "phone.ruth", "Ruth's phone", RuthPhoneRouterId),
        new(EllePhoneHome, "phone.ellie", "Ellie's phone", EllePhoneRouterId)
    ];

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

    private readonly IHaContext _ha;
    private readonly HistoryService _historyService;
    private readonly IReadOnlyList<PresenceDetector> _detectors;
    private readonly Dictionary<string, PresenceDetector> _detectorsById;
    private IReadOnlyDictionary<string, IReadOnlyList<string>> _zoneDetectors = DefaultZoneDetectors;

    public PresenceService(IHaContext ha, NamedEntities namedEntities, HistoryService historyService, TimeProvider timeProvider, ILogger<PresenceService> logger)
    {
        _historyService = historyService;

        // The "for 5 seconds" detectors work from live readings (SustainedCondition), never from the history API,
        // which lags live state by a few seconds (HOME-ASSISTANT.md, gotcha 21).
        // The desk's plug must be on as well, because a plug that is off keeps its last power reading.
        LatchedCondition diningDeskDrawing = new(
            () => namedEntities.DiningRoomDeskPlugOnOff.IsOn() && DrawsAtLeast(namedEntities.DiningRoomDeskPlugPower, DiningDeskInUseWatts),
            DiningDeskSteadyFor, DiningDeskHold, timeProvider);
        WatchForChanges(diningDeskDrawing.Observe, namedEntities.DiningRoomDeskPlugOnOff, namedEntities.DiningRoomDeskPlugPower);
        // Both den TV detectors read the one latch, so they agree on when the TV is on.
        PowerLatch denTv = new(() => namedEntities.DenTvPlugPower.State, DenTvInUseWatts, DenTvOffWatts, timeProvider);
        namedEntities.DenTvPlugPower.SubscribeToStateChangesAsync(_ => { denTv.Observe(); return Task.CompletedTask; });
        _ = ReplayRecentReadingsAsync(denTv, namedEntities.DenTvPlugPower.EntityId, logger);
        SustainedCondition denTvDrawing = WatchSustained(timeProvider, () => denTv.IsOn, namedEntities.DenTvPlugPower);
        SustainedCondition livingRoomTvDrawing = WatchSustained(timeProvider,
            () => DrawsAtLeast(namedEntities.LivingRoomTvPlugPower, LivingRoomTvInUseWatts), namedEntities.LivingRoomTvPlugPower);

        _detectors =
        [
            new(GamesDeskInUse, "Ste's desk in use (plug on, 40 W or more in the last 5 minutes)", true,
                [(namedEntities.GamesRoomDeskPlugOnOff.EntityId, RoomDeviceRoles.DeskPlug), (namedEntities.GamesRoomDeskPlugPower.EntityId, RoomDeviceRoles.DeskPower)],
                async () => namedEntities.GamesRoomDeskPlugOnOff.IsOn() && await IsDrawing(namedEntities.GamesRoomDeskPlugPower, DeskInUseWatts)),
            // The hold bridges dips in the draw, not the plug being switched off, which is a clear sign she has finished.
            new(DiningDeskInUse, "Ruth's desk in use (plug on, 30 W or more for 5 seconds, until under 30 W for 15 seconds)", true,
                [(namedEntities.DiningRoomDeskPlugOnOff.EntityId, RoomDeviceRoles.DeskPlug), (namedEntities.DiningRoomDeskPlugPower.EntityId, RoomDeviceRoles.DeskPower)],
                () => Task.FromResult(namedEntities.DiningRoomDeskPlugOnOff.IsOn() && diningDeskDrawing.IsOn)),
            // Says the desk is set up, not that anyone is at it, so it does not count towards "house".
            new(DiningDeskSwitchedOn, "Ruth's desk switched on", false,
                [(namedEntities.DiningRoomDeskPlugOnOff.EntityId, RoomDeviceRoles.DeskPlug)],
                () => Task.FromResult(namedEntities.DiningRoomDeskPlugOnOff.IsOn())),
            // Two from the den TV (owner, 2026-10-19): "recently" holds for 5 minutes after the TV goes off, so the
            // heating does not drop the moment it does; "now" has no hold, like the motion lights' automations.
            // Only "now" counts towards "house": a TV that was on a few minutes ago does not mean anyone is home.
            new(DenTvOn, "Den TV on recently (on from 100 W until down to 30 W, then 5 minutes more)", false,
                [(namedEntities.DenTvPlugPower.EntityId, RoomDeviceRoles.TvPower)],
                () => Task.FromResult(denTv.OnWithin(InUseLookBack))),
            new(DenTvOnNow, "Den TV on now (on from 100 W until down to 30 W, for 5 seconds)", true,
                [(namedEntities.DenTvPlugPower.EntityId, RoomDeviceRoles.TvPower)],
                () => Task.FromResult(denTvDrawing.HeldFor(DenTvSteadyFor))),
            new(LivingRoomTvOn, "Living room TV on (30 W or more for 5 seconds)", true,
                [(namedEntities.LivingRoomTvPlugPower.EntityId, RoomDeviceRoles.TvPower)],
                () => Task.FromResult(livingRoomTvDrawing.HeldFor(LivingRoomTvSteadyFor))),
            // Follows the WiZ lamp through two HA automations, so it inherits that lamp's lag (HOME-ASSISTANT.md gotcha 15).
            new(LivingRoomLightsOn, "Living room stick lights on", true,
                [(namedEntities.LivingRoomStickLightsPlugOnOff.EntityId, RoomDeviceRoles.LightsPlug)],
                () => Task.FromResult(namedEntities.LivingRoomStickLightsPlugOnOff.IsOn())),
            // Phones on the home Wi-Fi (owner, 2026-10-20): for individual zones only, not "house". No zone uses
            // them until the owner chooses them. The router refreshes its list about once a minute.
            // Each listed as the phone itself, not the router's list it is read from (owner, 2026-10-21).
            .. Phones.Select(phone => new PresenceDetector(phone.DetectorId, $"{phone.Name} on the home Wi-Fi", false,
                [(phone.DeviceId, RoomDeviceRoles.Phone)],
                () => Task.FromResult(IsOnRouter(ha, phone.RouterId))))
        ];
        _detectorsById = _detectors.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);

        _ha = ha;

        // A phone is not a Home Assistant entity; the router's list it is read from is.
        EntityExistenceValidator.ValidateEntities(ha, logger,
            [.. _detectors.SelectMany(d => d.Devices.Where(device => device.Role != RoomDeviceRoles.Phone).Select(device => device.EntityId))
                .Append(RouterOnlineDevices).Distinct()]);
    }

    /// <summary>
    /// A phone as a device of the zones that use it: its name, and "home" while the router lists it, "away" while it
    /// does not, "unavailable" while the router's list cannot be read. Null for anything that is not a phone, which
    /// is a Home Assistant entity to be looked up as usual.
    /// </summary>
    public RoomDeviceDto? DescribeOwnDevice(string deviceId)
    {
        PresencePhone? phone = Phones.FirstOrDefault(p => string.Equals(p.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
        if (phone == null)
        {
            return null;
        }

        string? routerState = _ha.GetState(RouterOnlineDevices)?.State;
        bool readable = routerState != null && routerState != "unavailable" && routerState != "unknown";
        return new RoomDeviceDto
        {
            EntityId = phone.DeviceId,
            Role = RoomDeviceRoles.Phone,
            Name = phone.Name,
            State = !readable ? "unavailable" : IsOnRouter(_ha, phone.RouterId) ? "home" : "away"
        };
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
    /// True when the router's online list (<see cref="RouterOnlineDevices"/>) holds a device with this router id.
    /// A missing or unavailable list counts as nobody online.
    /// </summary>
    internal static bool IsOnRouter(IHaContext ha, string routerId)
    {
        JsonElement? attributes = ha.GetState(RouterOnlineDevices)?.AttributesJson;
        if (!attributes.HasValue
            || attributes.Value.ValueKind != JsonValueKind.Object
            || !attributes.Value.TryGetProperty("devices", out JsonElement devices)
            || devices.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return devices.EnumerateArray().Any(device =>
            device.ValueKind == JsonValueKind.Object
            && device.TryGetProperty("id", out JsonElement id)
            && id.ValueKind == JsonValueKind.String
            && string.Equals(id.GetString(), routerId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// True when the power reached <paramref name="thresholdWatts"/> at any point within
    /// <see cref="InUseLookBack"/>. Recorder history includes the reading in force at the start of the
    /// window, so a steady draw with no recent change still counts. History lags live state by a few seconds, which
    /// is harmless for a five-minute look-back but is why the "for 5 seconds" detectors use
    /// <see cref="SustainedCondition"/> instead.
    /// </summary>
    private async Task<bool> IsDrawing(ICustomNumericSensorEntity power, double thresholdWatts)
    {
        DateTime now = DateTime.Now;
        IReadOnlyList<NumericHistoryEntry> histories = await _historyService.GetEntityNumericHistory(power.EntityId, now.Subtract(InUseLookBack), now);
        return histories.Count > 0 && histories.Max(h => h.State) >= thresholdWatts;
    }

    private static bool DrawsAtLeast(ICustomNumericSensorEntity power, double thresholdWatts) => power.State >= thresholdWatts;

    /// <summary>How far back a latch's readings are replayed at startup: longer than any dark scene is likely to last.</summary>
    internal static readonly TimeSpan LatchReplayWindow = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Starts a latch as it really was before the app started, from the recorder. History lags live state by a few
    /// seconds (gotcha 21), which does not matter for a starting point; the live reading is applied last. If the
    /// history cannot be read the latch starts off, as it did before.
    /// </summary>
    private async Task ReplayRecentReadingsAsync(PowerLatch latch, string entityId, ILogger logger)
    {
        try
        {
            DateTime now = DateTime.Now;
            IReadOnlyList<NumericHistoryEntry> history = await _historyService.GetEntityNumericHistory(entityId, now.Subtract(LatchReplayWindow), now);
            latch.Replay(history.Select(entry => ((DateTimeOffset)entry.LastChanged, entry.State)));
            logger.LogInformation("{Entity} replayed {Count} reading(s) from the last {Minutes} minutes at startup: {State}",
                entityId, history.Count, LatchReplayWindow.TotalMinutes, latch.IsOn ? "on" : "off");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not replay {Entity}'s recent readings at startup, so it starts as off", entityId);
        }
    }

    /// <summary>
    /// A <see cref="SustainedCondition"/> that is told about every change to the given sensors and switches, so a
    /// short break in the condition is never missed. A reading that cannot be read (unavailable) is below any threshold.
    /// </summary>
    private static SustainedCondition WatchSustained(TimeProvider timeProvider, Func<bool> condition, params object[] entities)
    {
        SustainedCondition sustained = new(condition, timeProvider);
        WatchForChanges(sustained.Observe, entities);
        return sustained;
    }

    /// <summary>Calls <paramref name="observe"/> on every change to the given sensors and switches.</summary>
    private static void WatchForChanges(Action observe, params object[] entities)
    {
        foreach (object entity in entities)
        {
            switch (entity)
            {
                case ICustomNumericSensorEntity sensor:
                    sensor.SubscribeToStateChangesAsync(_ => { observe(); return Task.CompletedTask; });
                    break;
                case ICustomSwitchEntity plug:
                    plug.SubscribeToStateChangesAsync(_ => { observe(); return Task.CompletedTask; });
                    break;
                default:
                    throw new ArgumentException($"Cannot watch {entity.GetType().Name} for changes.", nameof(entities));
            }
        }
    }
}

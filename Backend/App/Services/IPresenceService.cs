using HomeAssistant.Shared.Climate;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HomeAssistant.Services;

internal interface IPresenceService
{
    Task<bool> IsRoomInUse(string roomName);
    bool CanDetectIfRoomInUse(string roomName);

    /// <summary>
    /// The entities a zone's presence is read from, with their <see cref="HomeAssistant.Shared.Climate.RoomDeviceRoles"/>
    /// role. Empty for a presence-blind zone.
    /// </summary>
    IReadOnlyList<(string EntityId, string Role)> GetPresenceDevices(string roomName);

    /// <summary>
    /// A presence device that is not a Home Assistant entity (a phone), described for a zone's Devices list; null for
    /// anything else, which is looked up in Home Assistant as usual.
    /// </summary>
    RoomDeviceDto? DescribeOwnDevice(string deviceId);

    /// <summary>The canonical names of every zone that can detect presence.</summary>
    IReadOnlyList<string> PresenceZones { get; }

    /// <summary>Every presence detector the app knows, for the UI to choose from.</summary>
    IReadOnlyList<PresenceDetectorDto> Detectors { get; }

    /// <summary>The ids of the detectors that count for a zone.</summary>
    IReadOnlyList<string> DetectorsFor(string roomName);

    /// <summary>True while no choice of detectors has been saved, so the built-in defaults apply.</summary>
    bool ZoneOccupancyIsDefault { get; }

    /// <summary>The saved choice of detectors per zone (device settings <c>heating.zoneOccupancy</c>), or null for the defaults.</summary>
    void UseZoneOccupancy(IReadOnlyDictionary<string, List<string>>? saved);

    /// <summary>Every detector checked once, with each zone and "house" worked out from them.</summary>
    Task<PresenceSnapshot> GetSnapshotAsync();
}

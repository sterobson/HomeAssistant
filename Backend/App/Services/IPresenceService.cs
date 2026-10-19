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

    /// <summary>The canonical names of every zone that can detect presence.</summary>
    IReadOnlyList<string> PresenceZones { get; }

    /// <summary>
    /// The presence zones that mean someone is home: the "house" presence source is any of these in use. A
    /// subset of <see cref="PresenceZones"/>.
    /// </summary>
    IReadOnlyList<string> HouseZones { get; }
}

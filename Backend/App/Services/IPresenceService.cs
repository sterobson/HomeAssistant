using System.Threading.Tasks;

namespace HomeAssistant.Services;

internal interface IPresenceService
{
    Task<bool> IsRoomInUse(string roomName);
}
using HomeAssistant.Services.Climate;
using System.Threading.Tasks;

namespace HomeAssistant.Services;

internal interface IPresenceService
{
    Task<bool> IsRoomInUse(Room room);
}
using HomeAssistant.Services.Energy;
using System.Threading.Tasks;

namespace HomeAssistant.apps.HassModel.Energy;

[NetDaemonApp]
internal class HomeBatteryManagerV2
{
    private readonly BatteryControlService _batteryControlService;

    public HomeBatteryManagerV2(BatteryControlService batteryControlService)
    {
        _batteryControlService = batteryControlService;

        Task.Delay(1000).ContinueWith(value => _batteryControlService.Start());
    }
}

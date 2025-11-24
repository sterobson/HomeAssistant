using HomeAssistant.Services.Climate;
using System.Threading.Tasks;

namespace HomeAssistant.apps.HassModel.Climate;

[NetDaemonApp]
internal class Heating
{
    private readonly HeatingControlService _heatingControlService;

    public Heating(HeatingControlService heatingControlService)
    {
        _heatingControlService = heatingControlService;

        Task.Delay(1000).ContinueWith(value => _heatingControlService.Start());
    }
}
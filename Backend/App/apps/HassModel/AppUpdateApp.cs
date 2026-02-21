using HomeAssistant.Services;
using System.Threading;
using System.Threading.Tasks;

namespace HomeAssistant.apps.HassModel;

[NetDaemonApp]
internal class AppUpdateApp
{
    public AppUpdateApp(AppUpdateService appUpdateService, IGracefulShutdownService shutdownService)
    {
        CancellationToken shutdownToken = shutdownService.ShutdownToken;

        Task.Delay(TimeSpan.FromSeconds(10), shutdownToken).ContinueWith(_ =>
        {
            appUpdateService.Initialize();
        }, TaskContinuationOptions.NotOnCanceled);
    }
}

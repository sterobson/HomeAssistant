using HomeAssistant.Services;
using HomeAssistant.Services.HouseDevices;
using System.Threading;
using System.Threading.Tasks;

namespace HomeAssistant.apps.HassModel;

/// <summary>
/// Feeds the web UI's 3D house view: a device snapshot shortly after startup, then live states and commands while
/// the page is open (<see cref="HouseDevicesService"/>).
/// </summary>
[NetDaemonApp]
internal class HouseDevicesApp
{
    public HouseDevicesApp(HouseDevicesService houseDevicesService, IGracefulShutdownService shutdownService, ILogger<HouseDevicesApp> logger)
    {
        CancellationToken shutdownToken = shutdownService.ShutdownToken;
        houseDevicesService.Initialize();

        // Give Home Assistant time to populate its entities after a restart, as DeviceBatteryApp does.
        Task.Delay(TimeSpan.FromSeconds(45), shutdownToken).ContinueWith(async _ =>
        {
            try
            {
                await houseDevicesService.PushSnapshotAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not push the first device snapshot for the 3D view");
            }
        }, TaskContinuationOptions.NotOnCanceled);
    }
}

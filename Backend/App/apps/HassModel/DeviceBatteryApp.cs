using HomeAssistant.Services;
using System.Reactive.Concurrency;
using System.Threading;
using System.Threading.Tasks;

namespace HomeAssistant.apps.HassModel;

/// <summary>
/// Records the house's replaceable battery levels once a day.
///
/// This is about coin cells and AAs, not the SolaX home battery - that has its own push every
/// ten minutes in EnergyApp, which is why this deliberately lives outside the energy stack.
/// </summary>
[NetDaemonApp]
internal class DeviceBatteryApp
{
    /// <summary>
    /// Checked well inside a day so a push still lands if the app happens to be down, or is
    /// restarted by an over-the-air update, at whatever hour the previous push happened. The
    /// service and the storage both key on the date, so the extra attempts are no-ops.
    /// </summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(4);

    public DeviceBatteryApp(
        IScheduler scheduler,
        DeviceBatteryPushService pushService,
        IGracefulShutdownService shutdownService)
    {
        CancellationToken shutdownToken = shutdownService.ShutdownToken;

        // Give Home Assistant time to populate its entities after a restart; pushing too
        // early would find no battery sensors and skip the day.
        Task.Delay(TimeSpan.FromSeconds(45), shutdownToken).ContinueWith(async _ =>
            await pushService.PushIfDueAsync(),
            TaskContinuationOptions.NotOnCanceled);

        scheduler.SchedulePeriodic(CheckInterval, async () =>
        {
            if (shutdownToken.IsCancellationRequested) return;
            await pushService.PushIfDueAsync();
        });
    }
}

using HomeAssistant.Services;
using HomeAssistant.Services.Energy;
using System.Reactive.Concurrency;
using System.Threading;
using System.Threading.Tasks;

namespace HomeAssistant.apps.HassModel.Energy;

[NetDaemonApp]
internal class EnergyApp
{
    public EnergyApp(
        IScheduler scheduler,
        BatteryControlService batteryControlService,
        BatteryHistoryPushService batteryHistoryPushService,
        BatteryHistoryBackfillService batteryHistoryBackfillService,
        ElectricityRatePushService electricityRatePushService,
        EntityPushService entityPushService,
        PowerMonitorService powerMonitorService,
        IGracefulShutdownService shutdownService)
    {
        CancellationToken shutdownToken = shutdownService.ShutdownToken;

        Task.Delay(1000, shutdownToken).ContinueWith(value => batteryControlService.Start(),
            TaskContinuationOptions.NotOnCanceled);

        batteryHistoryPushService.Initialize();
        batteryHistoryBackfillService.Initialize();
        electricityRatePushService.Initialize();
        powerMonitorService.Initialize();

        Task.Delay(TimeSpan.FromSeconds(30), shutdownToken).ContinueWith(async _ =>
            await entityPushService.PushEntitiesIfDueAsync(),
            TaskContinuationOptions.NotOnCanceled);

        scheduler.SchedulePeriodic(TimeSpan.FromHours(6), async () =>
        {
            if (shutdownToken.IsCancellationRequested) return;
            await entityPushService.PushEntitiesIfDueAsync();
        });
    }
}

using HomeAssistant.Services;
using HomeAssistant.Services.Energy;
using System.Reactive.Concurrency;
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
        PowerMonitorService powerMonitorService)
    {
        Task.Delay(1000).ContinueWith(value => batteryControlService.Start());

        batteryHistoryPushService.Initialize();
        batteryHistoryBackfillService.Initialize();
        electricityRatePushService.Initialize();
        powerMonitorService.Initialize();

        Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(async _ =>
            await entityPushService.PushEntitiesIfDueAsync());

        scheduler.SchedulePeriodic(TimeSpan.FromHours(6), async () =>
            await entityPushService.PushEntitiesIfDueAsync());
    }
}

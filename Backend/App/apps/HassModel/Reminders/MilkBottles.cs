// Use unique namespaces for your apps if you going to share with others to avoid
// conflicting names

using HomeAssistant.Services;

using NetDaemon.Extensions.Scheduler;
using System.Reactive.Concurrency;
using System.Threading.Tasks;

namespace HassModel.Reminders;

[NetDaemonApp]
internal class MilkBottles
{
    private readonly ILogger<MilkBottles> _logger;
    private readonly NotificationService _notificationService;

    public MilkBottles(IScheduler scheduler, ILogger<MilkBottles> logger, NotificationService notificationService)
    {
        _logger = logger;
        _notificationService = notificationService;

        // Bottles out at 21:15 on Sunday nights, before we go to bed.
        scheduler.ScheduleCron("15 21 * * 0", async () => await SendMilkBottlesOutReminder());

        // Bottles out at 17:45 on a Thursday evening (becasue we're often out when the milk comes later than night).
        scheduler.ScheduleCron("45 17 * * 4", async () => await SendMilkBottlesOutReminder());

        // Bottles in at 6:15 on a Monday and Friday morning.
        scheduler.ScheduleCron("15 6 * * 1,5", async () => await SendMilkBottlesInReminder());
    }

    private async Task SendMilkBottlesOutReminder()
    {
        _notificationService.SendNotificationToGroups("Milk Bottles 🍶", "Put the milk bottles out", "robson");
    }

    private async Task SendMilkBottlesInReminder()
    {
        _notificationService.SendNotificationToGroups("Get the milk in 🐮", "The milk should be outside, so remember to get it in", "robson");
    }
}
// Use unique namespaces for your apps if you going to share with others to avoid
// conflicting names

using HomeAssistant.Services;
using HomeAssistantGenerated;
using NetDaemon.Extensions.Scheduler;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Threading.Tasks;

namespace HassModel.Reminders;

[NetDaemonApp]
internal class MilkBottles
{
    private readonly ILogger<MilkBottles> _logger;
    private readonly NotificationService _notificationService;

    public MilkBottles(IHaContext ha, IScheduler scheduler, ILogger<MilkBottles> logger, NotificationService notificationService)
    {
        _logger = logger;
        _notificationService = notificationService;
        Entities entities = new(ha);

        scheduler.ScheduleCron("15 21 * * *", async () => await CheckAndNotifyUpcomingCollections());
        scheduler.ScheduleCron("45 17 * * *", async () => await CheckAndNotifyUpcomingCollections());
    }

    private async Task CheckAndNotifyUpcomingCollections()
    {
        _logger.LogDebug("Running milk checker");
        switch (DateTime.Now.DayOfWeek)
        {
            case DayOfWeek.Sunday:
            case DayOfWeek.Tuesday:
                if (DateTime.Now.Hour >= 21)
                {
                    _notificationService.SendNotificationToGroups("Milk Bottles 🍶", GetRandomMessaage(), ["Ste", "Ruth"]);
                }

                break;

            case DayOfWeek.Thursday:
                // Warn earlier on a Thursday since we're often out.
                if (DateTime.Now.Hour <= 18)
                {
                    _notificationService.SendNotificationToGroups("Milk Bottles 🍶", GetRandomMessaage(), ["Ste", "Ruth"]);
                }

                break;

            default:
                // No action
                break;
        }
    }

    private static string GetRandomMessaage()
    {
        List<string> messages = [];

        messages.Add("Moo-ve those bottles outside!");
        messages.Add("Dairy duty calls.");
        messages.Add("Milk today, gone tomorrow.");
        messages.Add("Bottles want fresh air.");
        messages.Add("Moo says: out you go!");
        messages.Add("Don’t cry over un-spilled milk.");
        messages.Add("Udderly important chore.");
        messages.Add("Cow’s orders: bottles out!");
        messages.Add("Moo-ment of truth: outside!");
        messages.Add("Bottles are pasture bedtime.");
        messages.Add("Milk run, not marathon.");
        messages.Add("Dairy escape mission.");
        messages.Add("Moo-ving day for bottles.");
        messages.Add("Cowabunga, bottles out!");
        messages.Add("Moo-ving swiftly along.");
        messages.Add("Bottles need a night out.");
        messages.Add("Moo-sic to the milkman’s ears.");
        messages.Add("Dairy dispatch time.");
        messages.Add("Moo-ving bottles = happy cows.");
        messages.Add("Milk’s midnight adventure.");
        messages.Add("Moo-ving ritual begins.");
        messages.Add("Bottles crave the curb.");
        messages.Add("Moo-ving on up (to outside).");
        messages.Add("Cow’s bedtime story: bottles out.");
        messages.Add("Moo-ving bottles, no excuses.");
        messages.Add("Dairy drop-off duty.");
        messages.Add("Moo-ving bottles = peace of mind.");
        messages.Add("Bottles want freedom.");
        messages.Add("Moo-ving bottles, moo-ving life.");
        messages.Add("Cow’s checklist: bottles gone.");
        messages.Add("Moo-ving bottles, moo-ving you.");
        messages.Add("Bottles are herd animals.");
        messages.Add("Moo-ving bottles, moo-ving fast.");
        messages.Add("Cow’s whisper: “Outside now.”");
        messages.Add("Moo-ving bottles, moo-ving joy.");
        messages.Add("Bottles need fresh pasture.");
        messages.Add("Moo-ving bottles, moo-ving smiles.");
        messages.Add("Cow’s prophecy: bottles vanish.");
        messages.Add("Moo-ving bottles, moo-ving destiny.");
        messages.Add("Bottles seek greener grass.");
        messages.Add("Moo-ving bottles, moo-ving calm.");
        messages.Add("Cow’s wisdom: bottles outside.");
        messages.Add("Moo-ving bottles, moo-ving peace.");
        messages.Add("Bottles want starlight.");
        messages.Add("Moo-ving bottles, moo-ving stars.");
        messages.Add("Cow’s bedtime moo: bottles out.");
        messages.Add("Moo-ving bottles, moo-ving dreams.");
        messages.Add("Bottles need moonlight.");
        messages.Add("Moo-ving bottles, moo-ving night.");
        messages.Add("Cow’s lullaby: bottles gone.");
        messages.Add("Moo-ving bottles, moo-ving rest.");
        messages.Add("Bottles want freedom moo.");
        messages.Add("Moo-ving bottles, moo-ving moo.");
        messages.Add("Cow’s mantra: bottles out.");
        messages.Add("Moo-ving bottles, moo-ving mantra.");
        messages.Add("Bottles want moo-vement.");
        messages.Add("Moo-ving bottles, moo-ving moo-ment.");
        messages.Add("Cow’s chant: bottles outside.");
        messages.Add("Moo-ving bottles, moo-ving chant.");
        messages.Add("Bottles want moo-sic.");
        messages.Add("Moo-ving bottles, moo-ving moo-sic.");
        messages.Add("Cow’s song: bottles gone.");
        messages.Add("Moo-ving bottles, moo-ving song.");
        messages.Add("Bottles want moo-nlight.");
        messages.Add("Moo-ving bottles, moo-ving moo-nlight.");
        messages.Add("Cow’s moo-nlight tale.");
        messages.Add("Moo-ving bottles, moo-ving tale.");
        messages.Add("Bottles want moo-nshine.");
        messages.Add("Moo-ving bottles, moo-ving moo-nshine.");
        messages.Add("Cow’s moo-nshine dream.");
        messages.Add("Moo-ving bottles, moo-ving dream.");
        messages.Add("Bottles want moo-nbeam.");
        messages.Add("Moo-ving bottles, moo-ving moo-nbeam.");
        messages.Add("Cow’s moo-nbeam wish.");
        messages.Add("Moo-ving bottles, moo-ving wish.");
        messages.Add("Bottles want moo-nrise.");
        messages.Add("Moo-ving bottles, moo-ving moo-nrise.");
        messages.Add("Cow’s moo-nrise hope.");
        messages.Add("Moo-ving bottles, moo-ving hope.");
        messages.Add("Bottles want moo-nset.");
        messages.Add("Moo-ving bottles, moo-ving moo-nset.");
        messages.Add("Cow’s moo-nset peace.");
        messages.Add("Moo-ving bottles, moo-ving peace.");
        messages.Add("Bottles want moo-nshadow.");
        messages.Add("Moo-ving bottles, moo-ving moo-nshadow.");
        messages.Add("Cow’s moo-nshadow dance.");
        messages.Add("Moo-ving bottles, moo-ving dance.");
        messages.Add("Bottles want moo-nwalk.");
        messages.Add("Moo-ving bottles, moo-ving moo-nwalk.");
        messages.Add("Cow’s moo-nwalk groove.");
        messages.Add("Moo-ving bottles, moo-ving groove.");
        messages.Add("Bottles want moo-nparty.");
        messages.Add("Moo-ving bottles, moo-ving moo-nparty.");
        messages.Add("Cow’s moo-nparty cheer.");
        messages.Add("Moo-ving bottles, moo-ving cheer.");
        messages.Add("Bottles want moo-nlaugh.");
        messages.Add("Moo-ving bottles, moo-ving moo-nlaugh.");
        messages.Add("Cow’s moo-nlaugh joy.");
        messages.Add("Moo-ving bottles, moo-ving joy.");
        messages.Add("Bottles want moo-nlove.");

        return messages[Random.Shared.Next(0, messages.Count)];
    }
}
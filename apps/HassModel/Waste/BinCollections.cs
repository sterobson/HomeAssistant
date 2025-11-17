// Use unique namespaces for your apps if you going to share with others to avoid
// conflicting names

using HomeAssistant.Services;
using HomeAssistant.Services.WasteManagement;
using HomeAssistantGenerated;
using NetDaemon.Extensions.Scheduler;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Threading.Tasks;

namespace HassModel.Energy;

/// <summary>
///     Hello world showcase using the new HassModel API
/// </summary>
[NetDaemonApp]
internal class BinCollections
{
    private readonly ILogger<BinCollections> _logger;
    private readonly IWasteCollectionService _wasteCollectionService;
    private readonly NotificationService _notificationService;

    public BinCollections(IHaContext ha, IScheduler scheduler, ITriggerManager triggerManager, ILogger<BinCollections> logger,
        IWasteCollectionService wasteCollectionService, NotificationService notificationService)
    {
        _logger = logger;
        _wasteCollectionService = wasteCollectionService;
        _notificationService = notificationService;
        Entities entities = new(ha);

        scheduler.ScheduleCron("* 18 * * *", async () => await CheckAndNotifyUpcomingCollections());
    }

    private async Task CheckAndNotifyUpcomingCollections()
    {
        _logger.LogDebug("Running bin checker");
        IReadOnlyList<BinServiceDto> collections = await _wasteCollectionService.GetBinCollectionsAsync();
        List<BinServiceDto> tomorrowCollections = [.. collections.Where(c => c.NextCollection.HasValue && c.NextCollection.Value.Date > DateTime.Now && c.NextCollection.Value.Date < DateTime.Now.AddDays(1))];

        if (tomorrowCollections.Count > 0)
        {
            foreach (BinServiceDto collection in tomorrowCollections)
            {
                _logger.LogInformation("Upcoming collection: {WasteType} on {NextCollection}", collection.WasteType, collection.NextCollection);
            }

            _notificationService.SendNotificationToStePhone(GetRandomTitle(), string.Join(Environment.NewLine, tomorrowCollections.Select(c => " * " + c.WasteType)));
        }
    }

    private static string GetRandomTitle()
    {
        List<string> titles = [];
        titles.Add("Bag it, drag it");
        titles.Add("Bin ballet");
        titles.Add("Bin it to win it");
        titles.Add("Bin parade incoming");
        titles.Add("Bin‑anza");
        titles.Add("Bin‑credible bash");
        titles.Add("Bin‑credible bash night");
        titles.Add("Bin‑credible evening");
        titles.Add("Bin‑credible moment");
        titles.Add("Bin‑credible moment night");
        titles.Add("Bin‑credible roll‑out");
        titles.Add("Bin‑credible roll‑out night");
        titles.Add("Bin‑spirational");
        titles.Add("Can‑do night");
        titles.Add("Garbage gala");
        titles.Add("Garbage gala groove");
        titles.Add("Garbage gala groove night");
        titles.Add("Garbage gig");
        titles.Add("Garbage gig gala");
        titles.Add("Garbage groove");
        titles.Add("Garbage groove gala");
        titles.Add("Garbage groove gala");
        titles.Add("Garbage groove night");
        titles.Add("Lids up, folks");
        titles.Add("Refuse fiesta");
        titles.Add("Refuse fiesta fun");
        titles.Add("Refuse rally");
        titles.Add("Refuse rally roll‑out");
        titles.Add("Refuse rendezvous");
        titles.Add("Refuse revue");
        titles.Add("Refuse ritual");
        titles.Add("Refuse ritual roll‑out");
        titles.Add("Refuse roll‑call");
        titles.Add("Rubbish relay");
        titles.Add("Rubbish roll‑out");
        titles.Add("Rubbish roundup");
        titles.Add("The great wheel‑out");
        titles.Add("Trash dash");
        titles.Add("Trash talk");
        titles.Add("Trash tradition");
        titles.Add("Trash trek");
        titles.Add("Trash triumph");
        titles.Add("Trash triumph time");
        titles.Add("Waste wake‑up");
        titles.Add("Waste walk");
        titles.Add("Waste waltz");
        titles.Add("Waste waltz night");
        titles.Add("Waste warriors assemble");
        titles.Add("Waste watch");
        titles.Add("Wheelie time");
        titles.Add("Wheelie wonder");
        titles.Add("Wheelie wonderland");
        titles.Add("Wheelie workout");
        titles.Add("Wheelie workout night");

        return titles[Random.Shared.Next(0, titles.Count)];
    }
}
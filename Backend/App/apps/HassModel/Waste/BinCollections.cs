// Use unique namespaces for your apps if you going to share with others to avoid
// conflicting names

using HomeAssistant.Services;
using HomeAssistant.Services.WasteManagement;

using NetDaemon.Extensions.Scheduler;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Threading.Tasks;

namespace HassModel.Waste;

[NetDaemonApp]
internal class BinCollections
{
    private readonly ILogger<BinCollections> _logger;
    private readonly IWasteCollectionService _wasteCollectionService;
    private readonly NotificationService _notificationService;
    private readonly YorkBinServiceConfiguration _configuration;

    public BinCollections(IScheduler scheduler, ILogger<BinCollections> logger,
        IWasteCollectionService wasteCollectionService, NotificationService notificationService,
        YorkBinServiceConfiguration configuration)
    {
        _logger = logger;
        _wasteCollectionService = wasteCollectionService;
        _notificationService = notificationService;
        _configuration = configuration;

        foreach (YorkBinServiceProperty property in _configuration.Properties)
        {
            scheduler.ScheduleCron(property.Schedule, async () => await CheckAndNotifyUpcomingCollections(property));
        }
    }

    private async Task CheckAndNotifyUpcomingCollections(YorkBinServiceProperty property)
    {
        _logger.LogDebug("Running bin checker");
        IReadOnlyList<BinServiceDto> collections = await _wasteCollectionService.GetBinCollectionsAsync(property.Uprn);
        List<BinServiceDto> tomorrowCollections = [.. collections.Where(c => c.NextCollection.HasValue && c.NextCollection.Value.Date > DateTime.Now && c.NextCollection.Value.Date < DateTime.Now.AddDays(1))];

        if (tomorrowCollections.Count > 0)
        {
            foreach (BinServiceDto collection in tomorrowCollections)
            {
                _logger.LogInformation("Upcoming collection: {WasteType} on {NextCollection}", collection.WasteType, collection.NextCollection);
            }

            _notificationService.SendNotificationToGroups("Bins tomorrow", string.Join(Environment.NewLine, tomorrowCollections.Select(c => " * " + c.WasteType)), [.. property.NotificationLabels]);
        }
    }
}
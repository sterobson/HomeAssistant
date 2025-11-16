using HomeAssistant.Services;
using HomeAssistantGenerated;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Threading.Tasks;

namespace HomeAssistant.apps.HassModel.Climate;

[NetDaemonApp]
internal class DiningRoomDehumidifier
{
    private readonly HistoryService _historyService;
    private readonly NotificationService _notificationService;
    private readonly ILogger<DiningRoomDehumidifier> _logger;
    private readonly MyDevices _myDevices;
    private const int _minutesOfNoPowerUseBeforeNotification = 10;

    public DiningRoomDehumidifier(IHaContext ha, HistoryService historyService, IScheduler scheduler,
        NotificationService notificationService, ILogger<DiningRoomDehumidifier> logger)
    {
        Entities entities = new(ha);
        _myDevices = new(entities, ha);
        _historyService = historyService;
        _notificationService = notificationService;
        _logger = logger;
        _myDevices.DiningRoomClimateHumidity.StateChanges().SubscribeAsync(async e => _ = SetDehumidifierState());
        _myDevices.DiningRoomDehumidierSmartPlugOnOff.StateChanges().SubscribeAsync(async e => _ = SetDehumidifierState());
        _myDevices.DiningRoomDehumidierLookAheadMinutes.StateChanges().SubscribeAsync(async e => _ = SetDehumidifierState());
        _myDevices.DiningRoomDehumidierLowerThreshold.StateChanges().SubscribeAsync(async e => _ = SetDehumidifierState());
        _myDevices.DiningRoomDehumidierUpperThreshold.StateChanges().SubscribeAsync(async e => _ = SetDehumidifierState());
        _myDevices.DiningRoomDehumidifierSmartPlugPower.StateChanges().SubscribeAsync(async e => _ = CheckPowerState());

        // Every 10 minutes, after a random delay to spread out load, we want to check.
        Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(30, 250))).ContinueWith(e =>
        {
            scheduler.SchedulePeriodic(TimeSpan.FromMinutes(10), async () =>
            {
                await SetDehumidifierState();
                await Task.Delay(3000);
                await CheckPowerState();
            });
        });

        // Run once when starting up.
        _ = SetDehumidifierState();
    }

    private async Task SetDehumidifierState()
    {
        const double trendMinutes = 60;

        IReadOnlyList<NumericHistoryEntry> humidityHistory = await _historyService.GetEntityHistory(_myDevices.DiningRoomClimateHumidity.EntityId, DateTime.Now.AddMinutes(-trendMinutes), DateTime.Now);

        double trendChange = humidityHistory.Count > 0 ? humidityHistory[^1].State - humidityHistory[0].State : 0;
        double currentHumidity = _myDevices.DiningRoomClimateHumidity.State ?? 0;
        double projectedHumidity = currentHumidity;
        double upperThreshold = _myDevices.DiningRoomDehumidierUpperThreshold.State ?? 0;
        double lowerThreshold = _myDevices.DiningRoomDehumidierLowerThreshold.State ?? 0;

        bool isOn = _myDevices.DiningRoomDehumidierSmartPlugOnOff.State == "on";

        if (trendChange > 0)
        {
            projectedHumidity += trendChange * projectedHumidity / trendMinutes;
        }

        if (projectedHumidity > upperThreshold && !isOn)
        {
            _logger.LogInformation($"Projected humidity is {projectedHumidity:F1}%, current hunidity is {currentHumidity:F1}%, lower threshold is {lowerThreshold}%, upper threshold is {upperThreshold}%, plug current state is {(isOn ? "on" : "off")}, turning it on");
            _myDevices.DiningRoomDehumidierSmartPlugOnOff.TurnOn();
        }
        else if (currentHumidity <= lowerThreshold && isOn)
        {
            _logger.LogInformation($"Projected humidity is {projectedHumidity:F1}%, current hunidity is {currentHumidity:F1}%, lower threshold is {lowerThreshold}%, upper threshold is {upperThreshold}%, plug current state is {(isOn ? "on" : "off")}, turning it off");
            _myDevices.DiningRoomDehumidierSmartPlugOnOff.TurnOff();
        }

        await CheckPowerState();
    }

    private DateTime? _beenOnSince = null;
    private bool _notificationSent = false;

    private async Task CheckPowerState()
    {
        if (_myDevices.DiningRoomDehumidierSmartPlugOnOff.State == "on" && _beenOnSince == null)
        {
            // We're just noticing that the plug is on.
            _beenOnSince = DateTime.Now;
            _notificationSent = false;
        }
        else if (_myDevices.DiningRoomDehumidierSmartPlugOnOff.State == "off")
        {
            // Plug is off, may as well reset the checking timer.
            _beenOnSince = null;
            _notificationSent = false;
        }

        if (_beenOnSince == null || DateTime.UtcNow <= _beenOnSince.Value.AddMinutes(_minutesOfNoPowerUseBeforeNotification))
        {
            // Not been on long enough to care.
            return;
        }

        IReadOnlyList<NumericHistoryEntry> powerHistory = await _historyService.GetEntityHistory(_myDevices.DiningRoomDehumidifierSmartPlugPower.EntityId, DateTime.Now.AddMinutes(-_minutesOfNoPowerUseBeforeNotification), DateTime.Now);
        if (powerHistory.Any(h => h.State > _minutesOfNoPowerUseBeforeNotification))
        {
            // The plug has been on for a while and it is delivering power, so reset the time before looking
            // again for zero power.
            _beenOnSince = DateTime.UtcNow;
            _notificationSent = false;
        }
        else if (!_notificationSent)
        {
            // Uh oh, the plug has been on for a while but delivering no power.
            _notificationService.SendNotificationToStePhone("Dining room dehumidifier full?", "The dining room dehumidifier has been on for a while, but isn't drawing power. Is its tank full?");
            _notificationSent = true;
        }

    }
}

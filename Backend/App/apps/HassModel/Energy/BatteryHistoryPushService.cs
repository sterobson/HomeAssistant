using HomeAssistant.Devices.Batteries;
using HomeAssistant.Services.Energy;
using System.Threading.Tasks;

namespace HomeAssistant.apps.HassModel.Energy;

[NetDaemonApp]
internal class BatteryHistoryPushService
{
    private readonly IHomeBattery _homeBattery;
    private readonly IBatteryHistoryApiClient _historyApiClient;
    private readonly WebSynchronisationConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BatteryHistoryPushService> _logger;

    public BatteryHistoryPushService(
        IHomeBattery homeBattery,
        IBatteryHistoryApiClient historyApiClient,
        WebSynchronisationConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<BatteryHistoryPushService> logger)
    {
        _homeBattery = homeBattery;
        _historyApiClient = historyApiClient;
        _configuration = configuration;
        _timeProvider = timeProvider;
        _logger = logger;

        _homeBattery.OnBatteryChargePercentChanged(async change =>
        {
            await HandleBatteryPercentChangedAsync(change.New);
        });
    }

    private async Task HandleBatteryPercentChangedAsync(double? newPercent)
    {
        if (string.IsNullOrEmpty(_configuration.HouseId))
        {
            _logger.LogWarning("Cannot push battery state - HouseId not configured");
            return;
        }

        if (newPercent == null)
        {
            _logger.LogDebug("Battery percent is null, skipping push");
            return;
        }

        try
        {
            string date = _timeProvider.GetLocalNow().DateTime.ToString("yyyy-MM-dd");
            await _historyApiClient.PostBatteryStateAsync(_configuration.HouseId, newPercent.Value, null, date);
            _logger.LogDebug("Pushed battery state {Percent}% for date {Date}", newPercent.Value, date);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pushing battery state");
        }
    }
}

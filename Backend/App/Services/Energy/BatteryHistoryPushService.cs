using HomeAssistant.Devices.Batteries;
using System.Threading;
using System.Threading.Tasks;

namespace HomeAssistant.Services.Energy;

internal class BatteryHistoryPushService
{
    private static readonly TimeSpan MaxPushInterval = TimeSpan.FromMinutes(30);

    private readonly IHomeBattery _homeBattery;
    private readonly IBatteryHistoryApiClient _historyApiClient;
    private readonly WebSynchronisationConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BatteryHistoryPushService> _logger;

    private DateTimeOffset _lastPushTime = DateTimeOffset.MinValue;
    private CancellationTokenSource _delayCts = new();

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
    }

    public void Initialize()
    {
        _homeBattery.OnBatteryChargePercentChanged(async change =>
        {
            await HandleBatteryPercentChangedAsync(change.New);
        });

        _ = RunPeriodicPushAsync();
    }

    private async Task RunPeriodicPushAsync()
    {
        while (true)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            TimeSpan sinceLastPush = now - _lastPushTime;
            TimeSpan delay = MaxPushInterval - sinceLastPush;

            if (delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(delay, _delayCts.Token);
                }
                catch (TaskCanceledException)
                {
                    continue;
                }
            }

            double? currentPercent = _homeBattery.CurrentChargePercent;
            if (currentPercent == null)
            {
                await Task.Delay(TimeSpan.FromMinutes(1));
                continue;
            }

            _logger.LogDebug("No battery push in {Minutes} minutes, pushing current state",
                (int)(_timeProvider.GetUtcNow() - _lastPushTime).TotalMinutes);
            await PushBatteryStateAsync(currentPercent.Value);
        }
    }

    private async Task HandleBatteryPercentChangedAsync(double? newPercent)
    {
        if (newPercent == null)
        {
            _logger.LogDebug("Battery percent is null, skipping push");
            return;
        }

        await PushBatteryStateAsync(newPercent.Value);
    }

    private async Task PushBatteryStateAsync(double percent)
    {
        if (string.IsNullOrEmpty(_configuration.HouseId))
        {
            _logger.LogWarning("Cannot push battery state - HouseId not configured");
            return;
        }

        try
        {
            string date = _timeProvider.GetLocalNow().DateTime.ToString("yyyy-MM-dd");
            await _historyApiClient.PostBatteryStateAsync(_configuration.HouseId, percent, null, date);
            _lastPushTime = _timeProvider.GetUtcNow();
            _delayCts.Cancel();
            _delayCts = new CancellationTokenSource();
            _logger.LogDebug("Pushed battery state {Percent}% for date {Date}", percent, date);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pushing battery state");
        }
    }
}

using HomeAssistant.Devices.Batteries;
using System.Threading;
using System.Threading.Tasks;

namespace HomeAssistant.Services.Energy;

internal class BatteryHistoryPushService
{
    private static readonly TimeSpan MaxPushInterval = TimeSpan.FromMinutes(10);

    private readonly IHomeBattery _homeBattery;
    private readonly IBatteryHistoryApiClient _historyApiClient;
    private readonly WebSynchronisationConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BatteryHistoryPushService> _logger;
    private readonly IGracefulShutdownService _shutdownService;

    private DateTimeOffset _lastPushTime = DateTimeOffset.MinValue;
    private CancellationTokenSource _delayCts = new();
    private readonly object _ctsLock = new();

    public BatteryHistoryPushService(
        IHomeBattery homeBattery,
        IBatteryHistoryApiClient historyApiClient,
        WebSynchronisationConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<BatteryHistoryPushService> logger,
        IGracefulShutdownService shutdownService)
    {
        _homeBattery = homeBattery;
        _historyApiClient = historyApiClient;
        _configuration = configuration;
        _timeProvider = timeProvider;
        _logger = logger;
        _shutdownService = shutdownService;
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
        CancellationToken shutdownToken = _shutdownService.ShutdownToken;

        while (!shutdownToken.IsCancellationRequested)
        {
            try
            {
                DateTimeOffset now = _timeProvider.GetUtcNow();
                TimeSpan sinceLastPush = now - _lastPushTime;
                TimeSpan delay = MaxPushInterval - sinceLastPush;

                if (delay > TimeSpan.Zero)
                {
                    CancellationTokenSource linkedCts;
                    lock (_ctsLock)
                    {
                        linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_delayCts.Token, shutdownToken);
                    }

                    try
                    {
                        await Task.Delay(delay, linkedCts.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        if (shutdownToken.IsCancellationRequested)
                            break;
                        continue;
                    }
                    finally
                    {
                        linkedCts.Dispose();
                    }
                }

                double? currentPercent = _homeBattery.CurrentChargePercent;
                if (currentPercent == null)
                {
                    try { await Task.Delay(TimeSpan.FromMinutes(1), shutdownToken); }
                    catch (TaskCanceledException) { break; }
                    continue;
                }

                _logger.LogDebug("No battery push in {Minutes} minutes, pushing current state",
                    (int)(_timeProvider.GetUtcNow() - _lastPushTime).TotalMinutes);
                await PushBatteryStateAsync(currentPercent.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in periodic battery push loop, will retry");
                try { await Task.Delay(TimeSpan.FromMinutes(1), shutdownToken); }
                catch (TaskCanceledException) { break; }
            }
        }

        _logger.LogInformation("Battery history push service stopped due to shutdown");
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
            lock (_ctsLock)
            {
                _delayCts.Cancel();
                _delayCts = new CancellationTokenSource();
            }
            _logger.LogDebug("Pushed battery state {Percent}% for date {Date}", percent, date);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pushing battery state");
        }
    }
}

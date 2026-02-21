using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;

namespace HomeAssistant.Services;

public interface IGracefulShutdownService
{
    CancellationToken ShutdownToken { get; }
    Task RequestShutdownAsync(string reason);
}

internal class GracefulShutdownService : IGracefulShutdownService
{
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly ILogger<GracefulShutdownService> _logger;
    private readonly CancellationTokenSource _shutdownCts = new();
    private static readonly TimeSpan ShutdownGracePeriod = TimeSpan.FromSeconds(10);

    public CancellationToken ShutdownToken => _shutdownCts.Token;

    public GracefulShutdownService(
        IHostApplicationLifetime appLifetime,
        ILogger<GracefulShutdownService> logger)
    {
        _appLifetime = appLifetime;
        _logger = logger;
    }

    public async Task RequestShutdownAsync(string reason)
    {
        if (_shutdownCts.IsCancellationRequested)
        {
            _logger.LogDebug("Shutdown already requested, ignoring duplicate request: {Reason}", reason);
            return;
        }

        _logger.LogInformation("Graceful shutdown requested: {Reason}. Waiting {GracePeriod}s for in-flight operations to complete...",
            reason, ShutdownGracePeriod.TotalSeconds);

        _shutdownCts.Cancel();

        await Task.Delay(ShutdownGracePeriod);

        _logger.LogInformation("Grace period elapsed, stopping application");
        _appLifetime.StopApplication();
    }
}

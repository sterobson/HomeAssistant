using System.Threading;
using System.Threading.Tasks;

namespace HomeAssistant.Services;

internal class EntityPushCoordinator
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly ILogger<EntityPushCoordinator> _logger;
    private DateTimeOffset _lastPushTime = DateTimeOffset.MinValue;
    private static readonly TimeSpan MinInterval = TimeSpan.FromHours(5);

    public EntityPushCoordinator(ILogger<EntityPushCoordinator> logger)
    {
        _logger = logger;
    }

    public async Task<bool> TryAcquirePushAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (now - _lastPushTime < MinInterval)
            {
                _logger.LogDebug("Entity push skipped - last push was {Elapsed} ago (minimum interval: {Interval})",
                    now - _lastPushTime, MinInterval);
                return false;
            }

            _lastPushTime = now;
            return true;
        }
        finally
        {
            _semaphore.Release();
        }
    }
}

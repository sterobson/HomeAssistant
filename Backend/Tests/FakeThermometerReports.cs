using HomeAssistant.Services.Climate;

namespace HomeAssistant.Tests;

/// <summary>
/// When each thermometer last reported. A thermometer not set reports right now, as a working one does every few
/// minutes; one set to null cannot be told.
/// </summary>
internal sealed class FakeThermometerReports : IThermometerReports
{
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, DateTimeOffset?> _lastReported = new(StringComparer.OrdinalIgnoreCase);

    public FakeThermometerReports(TimeProvider clock)
    {
        _clock = clock;
    }

    /// <summary>The thermometer last reported this long ago, and has said nothing since.</summary>
    public void SilentFor(string entityId, TimeSpan howLong) => _lastReported[entityId] = _clock.GetUtcNow() - howLong;

    /// <summary>When the thermometer last reported cannot be told (HA unreachable).</summary>
    public void Unknown(string entityId) => _lastReported[entityId] = null;

    /// <summary>The thermometer is reporting again.</summary>
    public void Reporting(string entityId) => _lastReported.Remove(entityId);

    public Task<DateTimeOffset?> LastHeardFromAsync(string entityId) =>
        Task.FromResult(_lastReported.TryGetValue(entityId, out DateTimeOffset? at) ? at : _clock.GetUtcNow());
}

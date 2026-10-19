using System.IO;
using System.Text.Json;

namespace HomeAssistant.Services.Climate;

/// <summary>
/// What the app believes the boiler is doing in fingerbot mode, and since when. The boiler has no
/// feedback, so this is a belief, not a reading; it is kept on disk only so that a restart (every
/// over-the-air update is one) does not reset it to "off".
/// </summary>
internal sealed record BoilerBelief(bool BoilerOn, DateTimeOffset Since);

internal interface IBoilerStatePersistence
{
    BoilerBelief? Load();

    void Save(BoilerBelief belief);
}

/// <summary>
/// Keeps the boiler belief in <c>boiler-state.json</c> in the app's local cache folder. Local only:
/// the belief describes this house's boiler as seen by this copy of the app, so it is never synced.
/// </summary>
internal sealed class BoilerStatePersistence : IBoilerStatePersistence
{
    private const string _storageFileName = "boiler-state.json";
    private readonly ILogger<BoilerStatePersistence> _logger;
    private readonly string _localStoragePath;

    public BoilerStatePersistence(ILogger<BoilerStatePersistence> logger)
    {
        _logger = logger;
        _localStoragePath = Path.Combine(LocalStorageFolder.Resolve(), _storageFileName);

        // The add-on log is the only place this can be checked on the host: it should be under /data.
        _logger.LogInformation("Boiler belief is kept in {Path} ({State})", _localStoragePath,
            File.Exists(_localStoragePath) ? "a saved belief is there" : "nothing saved yet");
    }

    public BoilerBelief? Load()
    {
        try
        {
            if (!File.Exists(_localStoragePath))
                return null;

            return JsonSerializer.Deserialize<BoilerBelief>(File.ReadAllText(_localStoragePath));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading the boiler belief from {Path}", _localStoragePath);
            return null;
        }
    }

    public void Save(BoilerBelief belief)
    {
        try
        {
            File.WriteAllText(_localStoragePath, JsonSerializer.Serialize(belief));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving the boiler belief to {Path}", _localStoragePath);
        }
    }
}

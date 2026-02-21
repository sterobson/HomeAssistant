using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HomeAssistant.Services;

internal class AppUpdateService
{
    private readonly IAppUpdateApiClient _apiClient;
    private readonly IGracefulShutdownService _shutdownService;
    private readonly ISignalRConnectionService _signalRConnection;
    private readonly WebSynchronisationConfiguration _configuration;
    private readonly ILogger<AppUpdateService> _logger;
    private readonly SemaphoreSlim _updateLock = new(1, 1);

    private string? _currentVersion;

    private static readonly TimeSpan PollingInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(5);

    private static readonly string[] ConfigFilesToPreserve =
    [
        "appsettings.secrets.all.json",
        "appsettings.secrets.development.json",
        "appsettings.secrets.production.json",
        "appsettings.production.json"
    ];

    public AppUpdateService(
        IAppUpdateApiClient apiClient,
        IGracefulShutdownService shutdownService,
        ISignalRConnectionService signalRConnection,
        WebSynchronisationConfiguration configuration,
        ILogger<AppUpdateService> logger)
    {
        _apiClient = apiClient;
        _shutdownService = shutdownService;
        _signalRConnection = signalRConnection;
        _configuration = configuration;
        _logger = logger;
    }

    public void Initialize()
    {
        _currentVersion = ReadVersionFile();
        _logger.LogInformation("App update service initialized. Current version: {Version}", _currentVersion ?? "unknown");

        if (string.IsNullOrEmpty(_configuration.HouseId))
        {
            _logger.LogWarning("App update service disabled — HouseId not configured");
            return;
        }

        // Report running version
        if (!string.IsNullOrEmpty(_currentVersion))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _apiClient.ReportRunningVersionAsync(_configuration.HouseId, _currentVersion);
                    _logger.LogInformation("Reported running version {Version}", _currentVersion);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to report running version");
                }
            });
        }

        // Register SignalR handler
        _signalRConnection.On<System.Text.Json.JsonElement>("update-available", async (data) =>
        {
            _logger.LogInformation("Received update-available signal");
            await CheckAndApplyUpdateAsync();
        });

        // Start hourly polling loop
        _ = RunPollingLoopAsync();
    }

    private async Task RunPollingLoopAsync()
    {
        CancellationToken shutdownToken = _shutdownService.ShutdownToken;

        try
        {
            await Task.Delay(InitialDelay, shutdownToken);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        while (!shutdownToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndApplyUpdateAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in update polling loop");
            }

            try
            {
                await Task.Delay(PollingInterval, shutdownToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("App update polling loop stopped due to shutdown");
    }

    private async Task CheckAndApplyUpdateAsync()
    {
        if (string.IsNullOrEmpty(_configuration.HouseId))
            return;

        try
        {
            string? desiredVersion = await _apiClient.GetDesiredVersionAsync(_configuration.HouseId);

            if (string.IsNullOrEmpty(desiredVersion))
            {
                _logger.LogDebug("No desired version set, skipping update check");
                return;
            }

            if (desiredVersion == _currentVersion)
            {
                _logger.LogDebug("Already running desired version {Version}, acknowledging", desiredVersion);
                await _apiClient.AcknowledgeUpdateAsync(_configuration.HouseId);
                return;
            }

            _logger.LogInformation("Update available: {DesiredVersion} (current: {CurrentVersion})", desiredVersion, _currentVersion);
            await TryApplyUpdateAsync(desiredVersion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for updates");
        }
    }

    private async Task TryApplyUpdateAsync(string version)
    {
        if (!await _updateLock.WaitAsync(0))
        {
            _logger.LogWarning("Update already in progress, skipping");
            return;
        }

        try
        {
            _logger.LogInformation("Starting update to version {Version}", version);

            // Get download URL
            string? downloadUrl = await _apiClient.GetDownloadUrlAsync(version);
            if (string.IsNullOrEmpty(downloadUrl))
            {
                _logger.LogError("Failed to get download URL for version {Version}", version);
                return;
            }

            string appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            string stagingDir = appDir + "-staging";
            string previousDir = appDir + "-previous";

            // Clean up staging dir if it exists from a failed attempt
            if (Directory.Exists(stagingDir))
            {
                Directory.Delete(stagingDir, true);
            }

            // Download and extract zip
            _logger.LogInformation("Downloading update from {Url}", downloadUrl);
            using (HttpClient httpClient = new())
            {
                using Stream zipStream = await httpClient.GetStreamAsync(downloadUrl);
                string tempZipPath = Path.GetTempFileName();

                try
                {
                    using (FileStream fileStream = File.Create(tempZipPath))
                    {
                        await zipStream.CopyToAsync(fileStream);
                    }

                    _logger.LogInformation("Extracting update to {StagingDir}", stagingDir);
                    ZipFile.ExtractToDirectory(tempZipPath, stagingDir);
                }
                finally
                {
                    File.Delete(tempZipPath);
                }
            }

            // Directory swap: current → previous, staging → current
            _logger.LogInformation("Performing directory swap");

            // Remove old previous if it exists
            if (Directory.Exists(previousDir))
            {
                Directory.Delete(previousDir, true);
            }

            // Rename current → previous
            Directory.Move(appDir, previousDir);

            // Rename staging → current
            Directory.Move(stagingDir, appDir);

            // Copy config files from previous
            foreach (string configFile in ConfigFilesToPreserve)
            {
                string sourcePath = Path.Combine(previousDir, configFile);
                string destPath = Path.Combine(appDir, configFile);

                if (File.Exists(sourcePath) && !File.Exists(destPath))
                {
                    File.Copy(sourcePath, destPath);
                    _logger.LogDebug("Copied config file {File} from previous version", configFile);
                }
            }

            // Acknowledge update
            try
            {
                await _apiClient.AcknowledgeUpdateAsync(_configuration.HouseId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to acknowledge update (non-critical)");
            }

            _logger.LogInformation("Update to version {Version} complete, requesting shutdown", version);
            await _shutdownService.RequestShutdownAsync($"Update applied: {version}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply update to version {Version}", version);
        }
        finally
        {
            _updateLock.Release();
        }
    }

    private static string? ReadVersionFile()
    {
        string versionPath = Path.Combine(AppContext.BaseDirectory, "version.txt");

        if (!File.Exists(versionPath))
            return null;

        try
        {
            return File.ReadAllText(versionPath).Trim();
        }
        catch
        {
            return null;
        }
    }
}

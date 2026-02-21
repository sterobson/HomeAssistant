using HomeAssistant.Functions.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace HomeAssistant.Functions;

public class AppReleaseFunctions
{
    private readonly ILogger<AppReleaseFunctions> _logger;
    private readonly AppReleaseStorageService _storageService;
    private readonly SignalRService _signalRService;

    public AppReleaseFunctions(
        ILogger<AppReleaseFunctions> logger,
        AppReleaseStorageService storageService,
        SignalRService signalRService)
    {
        _logger = logger;
        _storageService = storageService;
        _signalRService = signalRService;
    }

    [Function("GetLatestAppVersion")]
    public async Task<IActionResult> GetLatestAppVersion(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "app-release/latest")] HttpRequest req)
    {
        try
        {
            string? version = await _storageService.GetLatestVersionAsync();
            return new OkObjectResult(new { version });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting latest app version");
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    [Function("GetDesiredAppVersion")]
    public async Task<IActionResult> GetDesiredAppVersion(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "app-release/desired")] HttpRequest req)
    {
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        try
        {
            string? version = await _storageService.GetDesiredVersionAsync(houseIdStr.ToString());
            return new OkObjectResult(new { version });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting desired app version for house {HouseId}", houseIdStr.ToString());
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    [Function("SetDesiredAppVersion")]
    public async Task<IActionResult> SetDesiredAppVersion(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "app-release/desired")] HttpRequest req)
    {
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        if (!req.Query.TryGetValue("version", out StringValues versionStr) ||
            string.IsNullOrWhiteSpace(versionStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing version query parameter" });
        }

        string houseId = houseIdStr.ToString();
        string version = versionStr.ToString();

        try
        {
            await _storageService.SetDesiredVersionAsync(houseId, version);
            _logger.LogInformation("Set desired app version {Version} for house {HouseId}", version, houseId);

            try
            {
                await _signalRService.SendMessageToGroupAsync($"house-{houseId}", "update-available", new { version });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send SignalR update-available message to house {HouseId}", houseId);
            }

            return new OkObjectResult(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting desired app version for house {HouseId}", houseId);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    [Function("GetAppReleaseDownloadUrl")]
    public IActionResult GetAppReleaseDownloadUrl(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "app-release/download")] HttpRequest req)
    {
        if (!req.Query.TryGetValue("version", out StringValues versionStr) ||
            string.IsNullOrWhiteSpace(versionStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing version query parameter" });
        }

        try
        {
            string url = _storageService.GenerateDownloadSasUrl(versionStr.ToString());
            return new OkObjectResult(new { url });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating download URL for version {Version}", versionStr.ToString());
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    [Function("UploadAppRelease")]
    public async Task<IActionResult> UploadAppRelease(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "app-release/upload")] HttpRequest req)
    {
        if (!req.Query.TryGetValue("version", out StringValues versionStr) ||
            string.IsNullOrWhiteSpace(versionStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing version query parameter" });
        }

        string version = versionStr.ToString();

        try
        {
            await _storageService.UploadReleaseAsync(version, req.Body);
            await _storageService.SetLatestVersionAsync(version);

            _logger.LogInformation("Uploaded app release version {Version}", version);
            return new OkObjectResult(new { success = true, version });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading app release version {Version}", version);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    [Function("GetRunningAppVersion")]
    public async Task<IActionResult> GetRunningAppVersion(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "app-release/running")] HttpRequest req)
    {
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        try
        {
            (string? version, DateTimeOffset? reportedAt) = await _storageService.GetRunningVersionAsync(houseIdStr.ToString());
            return new OkObjectResult(new { version, reportedAt });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting running app version for house {HouseId}", houseIdStr.ToString());
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    [Function("SetRunningAppVersion")]
    public async Task<IActionResult> SetRunningAppVersion(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "app-release/running")] HttpRequest req)
    {
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        if (!req.Query.TryGetValue("version", out StringValues versionStr) ||
            string.IsNullOrWhiteSpace(versionStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing version query parameter" });
        }

        try
        {
            await _storageService.SetRunningVersionAsync(houseIdStr.ToString(), versionStr.ToString());
            _logger.LogInformation("House {HouseId} reported running version {Version}", houseIdStr.ToString(), versionStr.ToString());
            return new OkObjectResult(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting running app version for house {HouseId}", houseIdStr.ToString());
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    [Function("AcknowledgeAppUpdate")]
    public async Task<IActionResult> AcknowledgeAppUpdate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "app-release/acknowledge")] HttpRequest req)
    {
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        try
        {
            await _storageService.ClearDesiredVersionAsync(houseIdStr.ToString());
            _logger.LogInformation("House {HouseId} acknowledged update", houseIdStr.ToString());
            return new OkObjectResult(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error acknowledging update for house {HouseId}", houseIdStr.ToString());
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }
}

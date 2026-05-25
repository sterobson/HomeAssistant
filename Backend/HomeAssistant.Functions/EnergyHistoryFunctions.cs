using HomeAssistant.Functions.JsonConverters;
using HomeAssistant.Functions.Models;
using HomeAssistant.Functions.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System.Text.Json;

namespace HomeAssistant.Functions;

public class EnergyHistoryFunctions
{
    private readonly ILogger<EnergyHistoryFunctions> _logger;
    private readonly EnergyHistoryStorageService _storageService;
    private readonly SignalRService _signalRService;

    public EnergyHistoryFunctions(ILogger<EnergyHistoryFunctions> logger, SignalRService signalRService)
    {
        _logger = logger;
        _signalRService = signalRService;

        string connectionString = Environment.GetEnvironmentVariable("ScheduleStorageConnectionString")
            ?? throw new InvalidOperationException("ScheduleStorageConnectionString not configured");

        _storageService = new EnergyHistoryStorageService(connectionString, logger);
    }

    [Function("GetEnergyHistory")]
    public async Task<IActionResult> GetEnergyHistory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "energy-history")] HttpRequest req)
    {
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        if (!req.Query.TryGetValue("date", out StringValues dateStr) ||
            string.IsNullOrWhiteSpace(dateStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing date query parameter" });
        }

        string houseId = houseIdStr.ToString();
        string date = dateStr.ToString();
        _logger.LogInformation("Getting energy history for house {HouseId} on {Date}", houseId, date);

        try
        {
            List<EnergyHistoryPoint> history = await _storageService.GetHistoryAsync(houseId, date);

            EnergyHistoryResponse response = new EnergyHistoryResponse
            {
                Points = history.Select(p => new EnergyHistoryPointDto
                {
                    Hour = p.Hour,
                    GridKwh = p.GridKwh,
                    BatteryKwh = p.BatteryKwh,
                    SolarKwh = p.SolarKwh,
                    HouseKwh = p.HouseKwh,
                    ImportCostPence = p.ImportCostPence,
                    ExportCostPence = p.ExportCostPence
                }).ToList()
            };

            _logger.LogInformation("Retrieved {Count} energy history points for house {HouseId} on {Date}",
                history.Count, houseId, date);
            return new OkObjectResult(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting energy history for house {HouseId} on {Date}", houseId, date);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    [Function("GetEnergyHistoryRange")]
    public async Task<IActionResult> GetEnergyHistoryRange(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "energy-history-range")] HttpRequest req)
    {
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        if (!req.Query.TryGetValue("fromDate", out StringValues fromDateStr) ||
            string.IsNullOrWhiteSpace(fromDateStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing fromDate query parameter" });
        }

        if (!req.Query.TryGetValue("toDate", out StringValues toDateStr) ||
            string.IsNullOrWhiteSpace(toDateStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing toDate query parameter" });
        }

        string houseId = houseIdStr.ToString();
        string fromDate = fromDateStr.ToString();
        string toDate = toDateStr.ToString();

        if (string.Compare(fromDate, toDate, StringComparison.Ordinal) > 0)
        {
            return new BadRequestObjectResult(new { error = "fromDate must be on or before toDate" });
        }

        _logger.LogInformation("Getting energy history range for house {HouseId} between {FromDate} and {ToDate}",
            houseId, fromDate, toDate);

        try
        {
            List<EnergyHistoryPoint> rawPoints = await _storageService.GetHistoryRangeAsync(houseId, fromDate, toDate);

            Dictionary<string, EnergyHistoryDailyTotalsDto> byDate = new();
            foreach (EnergyHistoryPoint point in rawPoints)
            {
                string date = point.Date;
                if (string.IsNullOrEmpty(date))
                {
                    int sep = point.PartitionKey.IndexOf('_');
                    if (sep < 0) continue;
                    date = point.PartitionKey[(sep + 1)..];
                }

                if (!byDate.TryGetValue(date, out EnergyHistoryDailyTotalsDto? totals))
                {
                    totals = new EnergyHistoryDailyTotalsDto { Date = date };
                    byDate[date] = totals;
                }

                totals.GridKwh += point.GridKwh;
                totals.BatteryKwh += point.BatteryKwh;
                totals.SolarKwh += point.SolarKwh;
                totals.HouseKwh += point.HouseKwh;
                totals.ImportCostPence += point.ImportCostPence;
                totals.ExportCostPence += point.ExportCostPence;
            }

            EnergyHistoryRangeResponse response = new EnergyHistoryRangeResponse
            {
                Days = byDate.Values.OrderBy(d => d.Date, StringComparer.Ordinal).ToList()
            };

            _logger.LogInformation("Retrieved {DayCount} days ({PointCount} hourly points) for house {HouseId} between {FromDate} and {ToDate}",
                response.Days.Count, rawPoints.Count, houseId, fromDate, toDate);
            return new OkObjectResult(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting energy history range for house {HouseId} between {FromDate} and {ToDate}",
                houseId, fromDate, toDate);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    [Function("PostEnergyHour")]
    public async Task<IActionResult> PostEnergyHour(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "energy-history")] HttpRequest req)
    {
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        string houseId = houseIdStr.ToString();

        try
        {
            using StreamReader reader = new(req.Body);
            string body = await reader.ReadToEndAsync();

            EnergyHourRequest? hourRequest = JsonSerializer.Deserialize<EnergyHourRequest>(body, JsonConfiguration.CreateOptions());

            if (hourRequest == null)
            {
                return new BadRequestObjectResult(new { error = "Invalid request body" });
            }

            EnergyHistoryPoint point = new EnergyHistoryPoint
            {
                Hour = hourRequest.Hour,
                GridKwh = hourRequest.GridKwh,
                BatteryKwh = hourRequest.BatteryKwh,
                SolarKwh = hourRequest.SolarKwh,
                HouseKwh = hourRequest.HouseKwh,
                ImportCostPence = hourRequest.ImportCostPence,
                ExportCostPence = hourRequest.ExportCostPence,
                RecordedAt = DateTimeOffset.UtcNow
            };

            await _storageService.SaveHourAsync(houseId, hourRequest.Date, point);

            _logger.LogDebug("Recorded energy hour {Hour} for house {HouseId} on {Date}", hourRequest.Hour, houseId, hourRequest.Date);

            try
            {
                await _signalRService.SendMessageToGroupAsync($"house-{houseId}", "energy-history-changed", new
                {
                    houseId,
                    date = hourRequest.Date,
                    hour = hourRequest.Hour
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send SignalR message for energy-history-changed to house {HouseId}", houseId);
            }

            return new OkObjectResult(new { success = true });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error parsing JSON for house {HouseId}", houseId);
            return new BadRequestObjectResult(new { error = "Invalid JSON format" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error posting energy hour for house {HouseId}", houseId);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    [Function("ReplaceEnergyHistory")]
    public async Task<IActionResult> ReplaceEnergyHistory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "energy-history-replace")] HttpRequest req)
    {
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        if (!req.Query.TryGetValue("date", out StringValues dateStr) ||
            string.IsNullOrWhiteSpace(dateStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing date query parameter" });
        }

        string houseId = houseIdStr.ToString();
        string date = dateStr.ToString();

        _logger.LogInformation("Replacing energy history for house {HouseId} on {Date}", houseId, date);

        try
        {
            using StreamReader reader = new(req.Body);
            string body = await reader.ReadToEndAsync();

            List<EnergyHistoryReplacePoint>? points = JsonSerializer.Deserialize<List<EnergyHistoryReplacePoint>>(
                body, JsonConfiguration.CreateOptions());

            if (points == null || points.Count == 0)
            {
                return new BadRequestObjectResult(new { error = "Invalid or empty request body" });
            }

            List<EnergyHistoryPoint> historyPoints = points.Select(p => new EnergyHistoryPoint
            {
                Hour = p.Hour,
                GridKwh = p.GridKwh,
                BatteryKwh = p.BatteryKwh,
                SolarKwh = p.SolarKwh,
                HouseKwh = p.HouseKwh,
                ImportCostPence = p.ImportCostPence,
                ExportCostPence = p.ExportCostPence,
                RecordedAt = DateTimeOffset.UtcNow
            }).ToList();

            await _storageService.ReplaceHistoryAsync(houseId, date, historyPoints);

            try
            {
                await _signalRService.SendMessageToGroupAsync($"house-{houseId}", "energy-history-replaced", new
                {
                    houseId,
                    date
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send SignalR message for energy-history-replaced to house {HouseId}", houseId);
            }

            return new OkObjectResult(new { success = true, pointCount = historyPoints.Count });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error parsing JSON for energy history replace, house {HouseId}", houseId);
            return new BadRequestObjectResult(new { error = "Invalid JSON format" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error replacing energy history for house {HouseId} on {Date}", houseId, date);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    [Function("RequestEnergyHistoryBackfill")]
    public async Task<IActionResult> RequestEnergyHistoryBackfill(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "energy-history-backfill")] HttpRequest req)
    {
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        string houseId = houseIdStr.ToString();

        // Support date range (fromDate/toDate) or single date for backward compatibility
        string? fromDate = req.Query.TryGetValue("fromDate", out StringValues fromDateStr) ? fromDateStr.ToString() : null;
        string? toDate = req.Query.TryGetValue("toDate", out StringValues toDateStr) ? toDateStr.ToString() : null;
        string? date = req.Query.TryGetValue("date", out StringValues dateStr) ? dateStr.ToString() : null;

        if (string.IsNullOrWhiteSpace(fromDate) || string.IsNullOrWhiteSpace(toDate))
        {
            // Fall back to single date
            if (string.IsNullOrWhiteSpace(date))
            {
                return new BadRequestObjectResult(new { error = "Provide fromDate/toDate or date query parameters" });
            }
            fromDate = date;
            toDate = date;
        }

        _logger.LogInformation("Requesting energy history backfill for house {HouseId} from {FromDate} to {ToDate}",
            houseId, fromDate, toDate);

        try
        {
            await _signalRService.SendMessageToGroupAsync($"house-{houseId}", "backfill-energy-history", new
            {
                houseId,
                fromDate,
                toDate
            });

            return new AcceptedResult(string.Empty, new { success = true, message = "Backfill request sent" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error requesting energy history backfill for house {HouseId}", houseId);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    [Function("BulkReplaceEnergyHistory")]
    public async Task<IActionResult> BulkReplaceEnergyHistory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "energy-history-bulk-replace")] HttpRequest req)
    {
        if (!req.Query.TryGetValue("houseId", out StringValues houseIdStr) ||
            string.IsNullOrWhiteSpace(houseIdStr))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        string houseId = houseIdStr.ToString();

        _logger.LogInformation("Bulk replacing energy history for house {HouseId}", houseId);

        try
        {
            using StreamReader reader = new(req.Body);
            string body = await reader.ReadToEndAsync();

            List<EnergyHistoryBulkDateEntry>? entries = JsonSerializer.Deserialize<List<EnergyHistoryBulkDateEntry>>(
                body, JsonConfiguration.CreateOptions());

            if (entries == null || entries.Count == 0)
            {
                return new BadRequestObjectResult(new { error = "Invalid or empty request body" });
            }

            int totalPoints = 0;
            foreach (EnergyHistoryBulkDateEntry entry in entries)
            {
                List<EnergyHistoryPoint> historyPoints = entry.Points.Select(p => new EnergyHistoryPoint
                {
                    Hour = p.Hour,
                    GridKwh = p.GridKwh,
                    BatteryKwh = p.BatteryKwh,
                    SolarKwh = p.SolarKwh,
                    HouseKwh = p.HouseKwh,
                    ImportCostPence = p.ImportCostPence,
                    ExportCostPence = p.ExportCostPence,
                    RecordedAt = DateTimeOffset.UtcNow
                }).ToList();

                await _storageService.ReplaceHistoryAsync(houseId, entry.Date, historyPoints);
                totalPoints += historyPoints.Count;
            }

            try
            {
                await _signalRService.SendMessageToGroupAsync($"house-{houseId}", "energy-history-replaced", new
                {
                    houseId,
                    dates = entries.Select(e => e.Date).ToList()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send SignalR message for bulk energy-history-replaced to house {HouseId}", houseId);
            }

            _logger.LogInformation("Bulk replaced energy history for house {HouseId}: {DateCount} dates, {PointCount} total points",
                houseId, entries.Count, totalPoints);
            return new OkObjectResult(new { success = true, dateCount = entries.Count, pointCount = totalPoints });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error parsing JSON for bulk energy history replace, house {HouseId}", houseId);
            return new BadRequestObjectResult(new { error = "Invalid JSON format" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error bulk replacing energy history for house {HouseId}", houseId);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }
}

public class EnergyHistoryBulkDateEntry
{
    public string Date { get; set; } = string.Empty;
    public List<EnergyHistoryReplacePoint> Points { get; set; } = [];
}

public class EnergyHourRequest
{
    public string Date { get; set; } = string.Empty;
    public int Hour { get; set; }
    public double GridKwh { get; set; }
    public double BatteryKwh { get; set; }
    public double SolarKwh { get; set; }
    public double HouseKwh { get; set; }
    public double ImportCostPence { get; set; }
    public double ExportCostPence { get; set; }
}

public class EnergyHistoryResponse
{
    public List<EnergyHistoryPointDto> Points { get; set; } = [];
}

public class EnergyHistoryPointDto
{
    public int Hour { get; set; }
    public double GridKwh { get; set; }
    public double BatteryKwh { get; set; }
    public double SolarKwh { get; set; }
    public double HouseKwh { get; set; }
    public double ImportCostPence { get; set; }
    public double ExportCostPence { get; set; }
}

public class EnergyHistoryReplacePoint
{
    public int Hour { get; set; }
    public double GridKwh { get; set; }
    public double BatteryKwh { get; set; }
    public double SolarKwh { get; set; }
    public double HouseKwh { get; set; }
    public double ImportCostPence { get; set; }
    public double ExportCostPence { get; set; }
}

public class EnergyHistoryRangeResponse
{
    public List<EnergyHistoryDailyTotalsDto> Days { get; set; } = [];
}

public class EnergyHistoryDailyTotalsDto
{
    public string Date { get; set; } = string.Empty;
    public double GridKwh { get; set; }
    public double BatteryKwh { get; set; }
    public double SolarKwh { get; set; }
    public double HouseKwh { get; set; }
    public double ImportCostPence { get; set; }
    public double ExportCostPence { get; set; }
}

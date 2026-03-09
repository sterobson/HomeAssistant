using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace HomeAssistant.Services.Energy;

public interface IEnergyHistoryApiClient
{
    Task PostEnergyHourAsync(string houseId, string date, int hour, double gridKwh, double batteryKwh, double solarKwh, double houseKwh, double importCostPence, double exportCostPence);
    Task ReplaceEnergyHistoryAsync(string houseId, string date, List<EnergyBackfillPoint> points);
    Task BulkReplaceEnergyHistoryAsync(string houseId, List<EnergyBackfillDateEntry> entries);
}

public class EnergyBackfillDateEntry
{
    public string Date { get; set; } = string.Empty;
    public List<EnergyBackfillPoint> Points { get; set; } = [];
}

public class EnergyBackfillPoint
{
    public int Hour { get; set; }
    public double GridKwh { get; set; }
    public double BatteryKwh { get; set; }
    public double SolarKwh { get; set; }
    public double HouseKwh { get; set; }
    public double ImportCostPence { get; set; }
    public double ExportCostPence { get; set; }
}

public class EnergyHistoryApiClient : IEnergyHistoryApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<EnergyHistoryApiClient> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public EnergyHistoryApiClient(HttpClient httpClient, ILogger<EnergyHistoryApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task PostEnergyHourAsync(string houseId, string date, int hour, double gridKwh, double batteryKwh, double solarKwh, double houseKwh, double importCostPence, double exportCostPence)
    {
        try
        {
            object body = new { date, hour, gridKwh, batteryKwh, solarKwh, houseKwh, importCostPence, exportCostPence };
            string json = JsonSerializer.Serialize(body, _jsonOptions);
            StringContent content = new(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await _httpClient.PostAsync(
                $"/api/energy-history?houseId={houseId}", content);
            response.EnsureSuccessStatusCode();

            _logger.LogDebug("Successfully posted energy hour {Hour} for house {HouseId} on {Date}",
                hour, houseId, date);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error posting energy hour to API for house {HouseId} on {Date}", houseId, date);
            throw;
        }
    }

    public async Task ReplaceEnergyHistoryAsync(string houseId, string date, List<EnergyBackfillPoint> points)
    {
        try
        {
            string json = JsonSerializer.Serialize(points, _jsonOptions);
            StringContent content = new(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await _httpClient.PostAsync(
                $"/api/energy-history-replace?houseId={houseId}&date={date}", content);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("Successfully replaced energy history for house {HouseId} on {Date} ({Count} points)",
                houseId, date, points.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error replacing energy history for house {HouseId} on {Date}", houseId, date);
            throw;
        }
    }

    public async Task BulkReplaceEnergyHistoryAsync(string houseId, List<EnergyBackfillDateEntry> entries)
    {
        try
        {
            string json = JsonSerializer.Serialize(entries, _jsonOptions);
            StringContent content = new(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await _httpClient.PostAsync(
                $"/api/energy-history-bulk-replace?houseId={houseId}", content);
            response.EnsureSuccessStatusCode();

            int totalPoints = System.Linq.Enumerable.Sum(entries, e => e.Points.Count);
            _logger.LogInformation("Successfully bulk replaced energy history for house {HouseId} ({DateCount} dates, {PointCount} points)",
                houseId, entries.Count, totalPoints);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error bulk replacing energy history for house {HouseId}", houseId);
            throw;
        }
    }
}

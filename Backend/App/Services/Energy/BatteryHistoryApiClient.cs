using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace HomeAssistant.Services.Energy;

public interface IBatteryHistoryApiClient
{
    Task PostBatteryStateAsync(string houseId, double batteryPercent, double? powerWatts, string date);
    Task ReplaceBatteryHistoryAsync(string houseId, string date, List<BackfillPoint> points);
}

public class BackfillPoint
{
    public double BatteryPercent { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
}

public class BatteryHistoryApiClient : IBatteryHistoryApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BatteryHistoryApiClient> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public BatteryHistoryApiClient(HttpClient httpClient, ILogger<BatteryHistoryApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task PostBatteryStateAsync(string houseId, double batteryPercent, double? powerWatts, string date)
    {
        try
        {
            object body = new { batteryPercent, powerWatts, date };
            string json = JsonSerializer.Serialize(body, _jsonOptions);
            StringContent content = new(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await _httpClient.PostAsync(
                $"/api/battery-state?houseId={houseId}", content);
            response.EnsureSuccessStatusCode();

            _logger.LogDebug("Successfully posted battery state {Percent}% for house {HouseId} on {Date}",
                batteryPercent, houseId, date);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error posting battery state to API for house {HouseId} on {Date}", houseId, date);
            throw;
        }
    }

    public async Task ReplaceBatteryHistoryAsync(string houseId, string date, List<BackfillPoint> points)
    {
        try
        {
            string json = JsonSerializer.Serialize(points, _jsonOptions);
            StringContent content = new(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await _httpClient.PostAsync(
                $"/api/battery-history-replace?houseId={houseId}&date={date}", content);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("Successfully replaced battery history for house {HouseId} on {Date} ({Count} points)",
                houseId, date, points.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error replacing battery history for house {HouseId} on {Date}", houseId, date);
            throw;
        }
    }
}

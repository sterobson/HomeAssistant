using HomeAssistantGenerated;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace HomeAssistant.Weather.WeatherApi;

internal class LocationProvider
{
    public record Location(double Longitude, double Latitude);

    private Location? _location = null;
    private DateTime _lastUpdated = DateTime.MinValue;

    private IServiceProvider _serviceProvider { get; }

    public LocationProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<Location?> GetLocationAsync()
    {
        if (DateTime.UtcNow - _lastUpdated < TimeSpan.FromHours(1))
        {
            return _location;
        }

        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();

        IHaContext? ha = scope.ServiceProvider.GetService<IHaContext>();
        double? longitude = null, latitude = null;

        if (ha != null)
        {
            Entities entities = new(ha);
            longitude = entities.Zone.Home.Attributes?.Longitude;
            latitude = entities.Zone.Home.Attributes?.Latitude;
        }

        _location = new(longitude ?? 0, latitude ?? 0);
        _lastUpdated = DateTime.UtcNow;

        return _location;
    }
}

internal class WeatherApiProvider : IWeatherProvider
{
    private readonly HttpClient _httpClient;
    private readonly LocationProvider _locationProvider;
    private readonly WeatherApiConfiguration _weatherApiConfiguration;
    private readonly JsonSerializerOptions _jsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };
    private WeatherForecast _weatherForecast = new();
    private string? _forecastLocation = null;
    private DateTime _lastUpdated = default(DateTime);

    public WeatherApiProvider(HttpClient httpClient, LocationProvider locationProvider, WeatherApiConfiguration weatherApiConfiguration)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _locationProvider = locationProvider;
        _weatherApiConfiguration = weatherApiConfiguration;
    }

    public async Task<WeatherForecast> GetWeatherAsync()
    {
        string apiKey = _weatherApiConfiguration.Token;
        LocationProvider.Location? location = await _locationProvider.GetLocationAsync();

        string locationQueryString = $"{location?.Latitude:F4},{location?.Longitude:F4}";

        // If nothing has changed, then return the last known weather forecast.
        if (locationQueryString != _forecastLocation
            || _weatherForecast.Days.Count == 0
            || DateTime.UtcNow.Hour != _lastUpdated.Hour
            || DateTime.UtcNow - _lastUpdated > TimeSpan.FromHours(1))
        {
            await RefreshWeatherForecast(apiKey, locationQueryString);
        }

        return _weatherForecast;
    }

    private async Task RefreshWeatherForecast(string apiKey, string locationQueryString)
    {
        Task<WeatherForecast> currentForecastTask = GetCurrentForecast(apiKey, locationQueryString);
        Task<WeatherForecast> historyForecastTask = GetHistoryForecast(apiKey, locationQueryString, DateOnly.FromDateTime(DateTime.Today.AddDays(-1)));

        await Task.WhenAll(currentForecastTask, historyForecastTask);

        WeatherForecast currentForecast = await currentForecastTask;
        WeatherForecast historyForecast = await historyForecastTask;

        foreach (WeatherDay historicDay in historyForecast.Days ?? [])
        {
            if (currentForecast.Days.Any(d => d.DateLocal == historicDay.DateLocal))
            {
                continue;
            }

            currentForecast.Days.Add(historicDay);
        }

        currentForecast.Days = [.. currentForecast.Days.OrderBy(d => d.DateLocal)];

        _weatherForecast = currentForecast;
        _forecastLocation = locationQueryString;
        _lastUpdated = DateTime.UtcNow;
    }

    private async Task<WeatherForecast> GetCurrentForecast(string apiKey, string location)
    {
        int days = 3;
        string url = $"http://api.weatherapi.com/v1/forecast.json?key={apiKey}&q={location}&days={days}&aqi=no&alerts=no";
        return await QueryApi(_httpClient, url, _jsonSerializerOptions);
    }

    private async Task<WeatherForecast> GetHistoryForecast(string apiKey, string location, DateOnly date)
    {
        string url = $"http://api.weatherapi.com/v1/history.json?key={apiKey}&q={location}&dt={date:yyyy-MM-dd}";
        return await QueryApi(_httpClient, url, _jsonSerializerOptions);
    }

    private static async Task<WeatherForecast> QueryApi(HttpClient client, string url, JsonSerializerOptions jsonSerializerOptions)
    {
        // Call WeatherAPI
        using HttpResponseMessage response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();

        // Deserialize into provider-specific DTOs
        WeatherApiResponse? providerResponse = JsonSerializer.Deserialize<WeatherApiResponse>(json, jsonSerializerOptions);

        WeatherForecast forecast = ConvertToWeatherForecast(providerResponse);

        return forecast;
    }

    private static WeatherForecast ConvertToWeatherForecast(WeatherApiResponse? providerResponse)
    {
        // Map into common DTOs
        WeatherForecast forecast = new()
        {
            Days = []
        };

        if (providerResponse?.Forecast?.ForecastDays == null)
        {
            return forecast;
        }

        foreach (ForecastDay day in providerResponse.Forecast.ForecastDays)
        {
            WeatherDay weatherDay = new()
            {
                DateLocal = day.DateLocal,
                SunriseLocal = day.Astro?.SunriseLocal,
                SunsetLocal = day.Astro?.SunsetLocal,
                Hours = []
            };

            if (day.Hourly == null)
            {
                continue;
            }

            foreach (HourlyForecast hour in day.Hourly)
            {
                weatherDay.Hours.Add(new WeatherHour
                {
                    TimeLocal = hour.TimeLocal,
                    TemperatureCelsius = hour.TemperatureCelsius,
                    LastUpdated = providerResponse.Current?.LastUpdatedLocal,
                    WindKph = hour.WindKph,
                    WindDegree = hour.WindDegree,
                    PrecipitationMm = hour.PrecipitationMm,
                    Humidity = hour.Humidity,
                    CloudCover = hour.Cloud,
                    VisibilityKm = hour.VisibilityKm,
                    UvIndex = hour.Uv,
                    ChanceOfRain = hour.ChanceOfRain,
                    ChanceOfSnow = hour.ChanceOfSnow
                });
            }

            forecast.Days.Add(weatherDay);
        }

        return forecast;
    }
}

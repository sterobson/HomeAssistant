using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace HomeAssistant.Weather.WeatherApi;

internal class WeatherApiProvider : IWeatherProvider
{
    private readonly HttpClient _httpClient;

    private readonly JsonSerializerOptions _jsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };

    public WeatherApiProvider(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<WeatherForecast> GetForecast()
    {
        string apiKey = "9d58c93ae3374243bca161954231810";
        string location = "YO232SU";
        int days = 3;

        // Build request URL
        string url = $"http://api.weatherapi.com/v1/forecast.json?key={apiKey}&q={location}&days={days}&aqi=no&alerts=no";

        // Call WeatherAPI
        using HttpResponseMessage response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();

        // Deserialize into provider-specific DTOs
        WeatherApiResponse? providerResponse = JsonSerializer.Deserialize<WeatherApiResponse>(json, _jsonSerializerOptions);

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
                Date = day.DateLocal,
                Sunrise = day.Astro?.SunriseLocal,
                Sunset = day.Astro?.SunsetLocal,
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

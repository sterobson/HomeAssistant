using System.Threading.Tasks;

namespace HomeAssistant.Weather;

internal interface IWeatherProvider
{
    Task<WeatherForecast> GetWeatherAsync();
}

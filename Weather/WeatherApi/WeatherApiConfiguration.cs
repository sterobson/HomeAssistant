namespace HomeAssistant.Weather.WeatherApi;

public class WeatherApiConfiguration
{
    public string Endpoint { get; set; }
    public string Token { get; set; } = string.Empty;
}
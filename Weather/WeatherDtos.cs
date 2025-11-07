using System.Collections.Generic;

namespace HomeAssistant.Weather;

public class WeatherForecast
{
    /// <summary>
    /// Collection of forecast days.
    /// </summary>
    public List<WeatherDay>? Days { get; set; }
}

public class WeatherDay
{
    /// <summary>
    /// The date of the forecast.
    /// </summary>
    public DateTime? Date { get; set; }

    /// <summary>
    /// Sunrise time for the day.
    /// </summary>
    public DateTime? Sunrise { get; set; }

    /// <summary>
    /// Sunset time for the day.
    /// </summary>
    public DateTime? Sunset { get; set; }

    /// <summary>
    /// Hourly forecasts for this day.
    /// </summary>
    public List<WeatherHour>? Hours { get; set; }
}

public class WeatherHour
{
    /// <summary>
    /// The local time of this forecast hour.
    /// </summary>
    public DateTime? TimeLocal { get; set; }

    /// <summary>
    /// Temperature in Celsius.
    /// </summary>
    public decimal? TemperatureCelsius { get; set; }

    /// <summary>
    /// When the forecast was last updated.
    /// </summary>
    public DateTime? LastUpdated { get; set; }

    /// <summary>
    /// Wind speed in kilometers per hour.
    /// </summary>
    public decimal? WindKph { get; set; }

    /// <summary>
    /// Wind direction in degrees.
    /// </summary>
    public int? WindDegree { get; set; }

    /// <summary>
    /// Precipitation in millimeters.
    /// </summary>
    public decimal? PrecipitationMm { get; set; }

    /// <summary>
    /// Humidity percentage.
    /// </summary>
    public int? Humidity { get; set; }

    /// <summary>
    /// Cloud cover percentage.
    /// </summary>
    public int? CloudCover { get; set; }

    /// <summary>
    /// Visibility in kilometers.
    /// </summary>
    public decimal? VisibilityKm { get; set; }

    /// <summary>
    /// UV index.
    /// </summary>
    public decimal? UvIndex { get; set; }

    /// <summary>
    /// Chance of rain (percentage).
    /// </summary>
    public int? ChanceOfRain { get; set; }

    /// <summary>
    /// Chance of snow (percentage).
    /// </summary>
    public int? ChanceOfSnow { get; set; }
}

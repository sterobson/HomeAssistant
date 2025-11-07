using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HomeAssistant.Weather.WeatherApi;

public class WeatherApiResponse
{
    [JsonPropertyName("location")]
    public Location? Location { get; set; }

    [JsonPropertyName("current")]
    public CurrentWeather? Current { get; set; }

    [JsonPropertyName("forecast")]
    public Forecast? Forecast { get; set; }
}

public class Location
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("region")]
    public string? Region { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("lat")]
    public decimal? Latitude { get; set; }

    [JsonPropertyName("lon")]
    public decimal? Longitude { get; set; }

    [JsonPropertyName("tz_id")]
    public string? TimeZoneId { get; set; }

    [JsonPropertyName("localtime_epoch")]
    public long? LocalTimeEpoch { get; set; }

    [JsonPropertyName("localtime")]
    public DateTime? LocalTime { get; set; }
}

public class CurrentWeather
{
    [JsonPropertyName("last_updated_epoch")]
    public long? LastUpdatedEpoch { get; set; }

    [JsonPropertyName("last_updated")]
    public DateTime? LastUpdatedLocal { get; set; }

    [JsonPropertyName("temp_c")]
    public decimal? TemperatureCelsius { get; set; }

    [JsonPropertyName("is_day")]
    public int? IsDay { get; set; }

    [JsonPropertyName("condition")]
    public Condition? Condition { get; set; }

    [JsonPropertyName("wind_kph")]
    public decimal? WindKph { get; set; }

    [JsonPropertyName("wind_degree")]
    public int? WindDegree { get; set; }

    [JsonPropertyName("wind_dir")]
    public string? WindDirection { get; set; }

    [JsonPropertyName("pressure_mb")]
    public decimal? PressureMb { get; set; }

    [JsonPropertyName("precip_mm")]
    public decimal? PrecipitationMm { get; set; }

    [JsonPropertyName("humidity")]
    public int? Humidity { get; set; }

    [JsonPropertyName("cloud")]
    public int? Cloud { get; set; }

    [JsonPropertyName("feelslike_c")]
    public decimal? FeelsLikeCelsius { get; set; }

    [JsonPropertyName("windchill_c")]
    public decimal? WindChillCelsius { get; set; }

    [JsonPropertyName("heatindex_c")]
    public decimal? HeatIndexCelsius { get; set; }

    [JsonPropertyName("dewpoint_c")]
    public decimal? DewPointCelsius { get; set; }

    [JsonPropertyName("vis_km")]
    public decimal? VisibilityKm { get; set; }

    [JsonPropertyName("uv")]
    public decimal? Uv { get; set; }

    [JsonPropertyName("gust_kph")]
    public decimal? GustKph { get; set; }
}

public class Condition
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("code")]
    public int? Code { get; set; }
}

public class Forecast
{
    [JsonPropertyName("forecastday")]
    public List<ForecastDay>? ForecastDays { get; set; }
}

public class ForecastDay
{
    [JsonPropertyName("date")]
    public DateTime? DateLocal { get; set; }

    [JsonPropertyName("date_epoch")]
    public long? DateEpoch { get; set; }

    [JsonPropertyName("day")]
    public DaySummary? Day { get; set; }

    [JsonPropertyName("astro")]
    public Astro? Astro { get; set; }

    [JsonPropertyName("hour")]
    public List<HourlyForecast>? Hourly { get; set; }
}

public class DaySummary
{
    [JsonPropertyName("maxtemp_c")]
    public decimal? MaximumTemperatureCelsius { get; set; }

    [JsonPropertyName("mintemp_c")]
    public decimal? MinimumTemperatureCelsius { get; set; }

    [JsonPropertyName("avgtemp_c")]
    public decimal? AverageTemperatureCelsius { get; set; }

    [JsonPropertyName("maxwind_kph")]
    public decimal? MaximumWindKph { get; set; }

    [JsonPropertyName("totalprecip_mm")]
    public decimal? TotalPrecipitationMm { get; set; }

    [JsonPropertyName("totalsnow_cm")]
    public decimal? TotalSnowCm { get; set; }

    [JsonPropertyName("avgvis_km")]
    public decimal? AverageVisibilityKm { get; set; }

    [JsonPropertyName("avghumidity")]
    public int? AverageHumidity { get; set; }

    [JsonPropertyName("daily_will_it_rain")]
    public int? DailyWillItRain { get; set; }

    [JsonPropertyName("daily_chance_of_rain")]
    public int? DailyChanceOfRain { get; set; }

    [JsonPropertyName("daily_will_it_snow")]
    public int? DailyWillItSnow { get; set; }

    [JsonPropertyName("daily_chance_of_snow")]
    public int? DailyChanceOfSnow { get; set; }

    [JsonPropertyName("condition")]
    public Condition? Condition { get; set; }

    [JsonPropertyName("uv")]
    public decimal? Uv { get; set; }
}

public class Astro
{
    [JsonPropertyName("sunrise")]
    public DateTime? SunriseLocal { get; set; }

    [JsonPropertyName("sunset")]
    public DateTime? SunsetLocal { get; set; }

    [JsonPropertyName("moonrise")]
    public DateTime? MoonriseLocal { get; set; }

    [JsonPropertyName("moonset")]
    public DateTime? MoonsetLocal { get; set; }

    [JsonPropertyName("moon_phase")]
    public string? MoonPhase { get; set; }

    [JsonPropertyName("moon_illumination")]
    public int? MoonIllumination { get; set; }

    [JsonPropertyName("is_moon_up")]
    [JsonConverter(typeof(IntToBoolConverter))]
    public bool? IsMoonUp { get; set; }

    [JsonPropertyName("is_sun_up")]
    [JsonConverter(typeof(IntToBoolConverter))]
    public bool? IsSunUp { get; set; }
}

public class HourlyForecast
{
    [JsonPropertyName("time_epoch")]
    public long? TimeEpoch { get; set; }

    [JsonPropertyName("time")]
    public DateTime? TimeLocal { get; set; }

    [JsonPropertyName("temp_c")]
    public decimal? TemperatureCelsius { get; set; }

    [JsonPropertyName("is_day")]
    public int? IsDay { get; set; }

    [JsonPropertyName("condition")]
    public Condition? Condition { get; set; }

    [JsonPropertyName("wind_kph")]
    public decimal? WindKph { get; set; }

    [JsonPropertyName("wind_degree")]
    public int? WindDegree { get; set; }

    [JsonPropertyName("wind_dir")]
    public string? WindDirection { get; set; }

    [JsonPropertyName("pressure_mb")]
    public decimal? PressureMb { get; set; }

    [JsonPropertyName("precip_mm")]
    public decimal? PrecipitationMm { get; set; }

    [JsonPropertyName("snow_cm")]
    public decimal? SnowCm { get; set; }

    [JsonPropertyName("humidity")]
    public int? Humidity { get; set; }

    [JsonPropertyName("cloud")]
    public int? Cloud { get; set; }

    [JsonPropertyName("feelslike_c")]
    public decimal? FeelsLikeCelsius { get; set; }

    [JsonPropertyName("windchill_c")]
    public decimal? WindChillCelsius { get; set; }

    [JsonPropertyName("heatindex_c")]
    public decimal? HeatIndexCelsius { get; set; }

    [JsonPropertyName("dewpoint_c")]
    public decimal? DewPointCelsius { get; set; }

    [JsonPropertyName("will_it_rain")]
    [JsonConverter(typeof(IntToBoolConverter))]
    public bool? WillItRain { get; set; }

    [JsonPropertyName("chance_of_rain")]
    public int? ChanceOfRain { get; set; }

    [JsonPropertyName("will_it_snow")]
    [JsonConverter(typeof(IntToBoolConverter))]
    public bool? WillItSnow { get; set; }

    [JsonPropertyName("chance_of_snow")]
    public int? ChanceOfSnow { get; set; }

    [JsonPropertyName("vis_km")]
    public decimal? VisibilityKm { get; set; }

    [JsonPropertyName("gust_kph")]
    public decimal? GustKph { get; set; }

    [JsonPropertyName("uv")]
    public decimal? Uv { get; set; }
}

public class IntToBoolConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int value))
        {
            return value == 1;
        }
        throw new JsonException("Expected number 0 or 1 for boolean conversion.");
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value ? 1 : 0);
    }
}
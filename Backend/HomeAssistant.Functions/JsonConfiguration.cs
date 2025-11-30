using HomeAssistant.Functions.JsonConverters;
using System.Text.Json;

namespace HomeAssistant.Functions;

/// <summary>
/// Provides shared JSON serialization configuration for the application
/// </summary>
public static class JsonConfiguration
{
    /// <summary>
    /// Creates JsonSerializerOptions configured for the application.
    /// Uses camelCase naming and flexible enum conversion (strings and numbers).
    /// </summary>
    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        options.Converters.Add(new FlexibleEnumConverterFactory());
        return options;
    }

    /// <summary>
    /// Configures an existing JsonSerializerOptions instance with application settings
    /// </summary>
    public static void ConfigureOptions(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.Converters.Add(new FlexibleEnumConverterFactory());
    }
}

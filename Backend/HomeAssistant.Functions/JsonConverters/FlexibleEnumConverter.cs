using System.Text.Json;
using System.Text.Json.Serialization;

namespace HomeAssistant.Functions.JsonConverters;

/// <summary>
/// JSON converter that allows enums to be deserialized from either numbers or strings
/// </summary>
public class FlexibleEnumConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            // Return default value (0) for null - this handles legacy data
            return (TEnum)Enum.ToObject(typeof(TEnum), 0);
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            string? stringValue = reader.GetString();
            if (stringValue != null && Enum.TryParse<TEnum>(stringValue, ignoreCase: true, out TEnum result))
            {
                return result;
            }
        }
        else if (reader.TokenType == JsonTokenType.Number)
        {
            if (reader.TryGetInt32(out int intValue))
            {
                return (TEnum)Enum.ToObject(typeof(TEnum), intValue);
            }
        }

        throw new JsonException($"Unable to convert value to {typeof(TEnum).Name}");
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(Convert.ToInt32(value));
    }
}

/// <summary>
/// Factory for creating FlexibleEnumConverter instances
/// </summary>
public class FlexibleEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsEnum;
    }

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        Type converterType = typeof(FlexibleEnumConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter?)Activator.CreateInstance(converterType);
    }
}

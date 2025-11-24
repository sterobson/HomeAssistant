// Use unique namespaces for your apps if you going to share with others to avoid
// conflicting names

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HomeAssistant.apps.Energy.Octopus;

/// <summary>
/// Represents the root object for the Octopus JSON structure.
/// All related types are nested.
/// </summary>
internal class AccountInfo
{
    [JsonPropertyName("number")]
    public string Number { get; set; } = string.Empty;

    [JsonPropertyName("properties")]
    public List<Property> Properties { get; set; } = new();
}

internal class Property
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("moved_in_at")]
    public DateTime MovedInAt { get; set; }

    [JsonPropertyName("moved_out_at")]
    public DateTime? MovedOutAt { get; set; }

    [JsonPropertyName("address_line_1")]
    public string AddressLine1 { get; set; } = string.Empty;

    [JsonPropertyName("address_line_2")]
    public string AddressLine2 { get; set; } = string.Empty;

    [JsonPropertyName("address_line_3")]
    public string AddressLine3 { get; set; } = string.Empty;

    [JsonPropertyName("town")]
    public string Town { get; set; } = string.Empty;

    [JsonPropertyName("county")]
    public string County { get; set; } = string.Empty;

    [JsonPropertyName("postcode")]
    public string Postcode { get; set; } = string.Empty;

    [JsonPropertyName("electricity_meter_points")]
    public List<ElectricityMeterPoint> ElectricityMeterPoints { get; set; } = new();

    [JsonPropertyName("gas_meter_points")]
    public List<GasMeterPoint> GasMeterPoints { get; set; } = new();
}

internal class ElectricityMeterPoint
{
    [JsonPropertyName("mpan")]
    public string Mpan { get; set; } = string.Empty;

    [JsonPropertyName("profile_class")]
    public int ProfileClass { get; set; }

    [JsonPropertyName("consumption_standard")]
    public int ConsumptionStandard { get; set; }

    [JsonPropertyName("meters")]
    public List<ElectricityMeter> Meters { get; set; } = new();

    [JsonPropertyName("agreements")]
    public List<Agreement> Agreements { get; set; } = new();

    [JsonPropertyName("is_export")]
    public bool IsExport { get; set; }
}

internal class ElectricityMeter
{
    [JsonPropertyName("serial_number")]
    public string SerialNumber { get; set; } = string.Empty;

    [JsonPropertyName("registers")]
    public List<Register> Registers { get; set; } = new();
}

internal class Register
{
    [JsonPropertyName("identifier")]
    public string Identifier { get; set; } = string.Empty;

    [JsonPropertyName("rate")]
    public string Rate { get; set; } = string.Empty;

    [JsonPropertyName("is_settlement_register")]
    public bool IsSettlementRegister { get; set; }
}

internal class Agreement
{
    [JsonPropertyName("tariff_code")]
    public string TariffCode { get; set; } = string.Empty;

    [JsonPropertyName("valid_from")]
    public DateTime ValidFrom { get; set; }

    [JsonPropertyName("valid_to")]
    public DateTime? ValidTo { get; set; }
}

internal class GasMeterPoint
{
    [JsonPropertyName("mprn")]
    public string Mprn { get; set; } = string.Empty;

    [JsonPropertyName("consumption_standard")]
    public int ConsumptionStandard { get; set; }

    [JsonPropertyName("meters")]
    public List<GasMeter> Meters { get; set; } = new();

    [JsonPropertyName("agreements")]
    public List<Agreement> Agreements { get; set; } = new();
}

internal class GasMeter
{
    [JsonPropertyName("serial_number")]
    public string SerialNumber { get; set; } = string.Empty;
}
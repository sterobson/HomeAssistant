using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HomeAssistant.apps.Energy.Octopus;

public class OctopusTariffRates
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("next")]
    public string? Next { get; set; }

    [JsonPropertyName("previous")]
    public string? Previous { get; set; }

    [JsonPropertyName("results")]
    public List<OctopusTariffRate> Results { get; set; } = [];
}

public class OctopusTariffRate
{
    [JsonPropertyName("value_exc_vat")]
    public decimal ValueExcVat { get; set; }

    [JsonPropertyName("value_inc_vat")]
    public decimal ValueIncVat { get; set; }

    [JsonPropertyName("valid_from")]
    public DateTime ValidFrom { get; set; }

    [JsonPropertyName("valid_to")]
    public DateTime ValidTo { get; set; }

    [JsonPropertyName("payment_method")]
    public string? PaymentMethod { get; set; }

    public EnergyRate ToEnergyRate()
    {
        return new EnergyRate
        {
            StartTimeUtc = ValidFrom.ToUniversalTime(),
            EndTimeUtc = ValidTo.ToUniversalTime(),
            RateExcVat = (double)ValueExcVat,
            RateIncVat = (double)ValueIncVat
        };
    }
}

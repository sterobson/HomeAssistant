using System.Text.Json.Serialization;

namespace HomeAssistant.apps.Energy.Solax;

internal class SolaxApiResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("exception")]
    public string Exception { get; set; } = string.Empty;

    [JsonPropertyName("result")]
    public SolaxResult? Result { get; set; }

    [JsonPropertyName("code")]
    public int Code { get; set; }

    public class SolaxResult
    {
        [JsonPropertyName("inverterSN")]
        public string InverterSN { get; set; } = string.Empty;

        [JsonPropertyName("sn")]
        public string SN { get; set; } = string.Empty;

        [JsonPropertyName("acpower")]
        public int AcPower { get; set; }

        [JsonPropertyName("yieldtoday")]
        public double YieldToday { get; set; }

        [JsonPropertyName("yieldtotal")]
        public double YieldTotal { get; set; }

        [JsonPropertyName("feedinpower")]
        public int FeedInPower { get; set; }

        [JsonPropertyName("feedinenergy")]
        public double FeedInEnergy { get; set; }

        [JsonPropertyName("consumeenergy")]
        public double ConsumeEnergy { get; set; }

        [JsonPropertyName("feedinpowerM2")]
        public int FeedInPowerM2 { get; set; }

        [JsonPropertyName("soc")]
        public int StateOfCharge { get; set; }

        [JsonPropertyName("peps1")]
        public int Peps1 { get; set; }

        [JsonPropertyName("peps2")]
        public int? Peps2 { get; set; }

        [JsonPropertyName("peps3")]
        public int? Peps3 { get; set; }

        [JsonPropertyName("inverterType")]
        public string InverterType { get; set; } = string.Empty;

        [JsonPropertyName("inverterStatus")]
        public string? InverterStatus { get; set; }

        [JsonPropertyName("uploadTime")]
        public string UploadTime { get; set; } = string.Empty;

        [JsonPropertyName("batPower")]
        public int BatPower { get; set; }

        [JsonPropertyName("powerdc1")]
        public int PowerDc1 { get; set; }

        [JsonPropertyName("powerdc2")]
        public int PowerDc2 { get; set; }

        [JsonPropertyName("powerdc3")]
        public int PowerDc3 { get; set; }

        [JsonPropertyName("powerdc4")]
        public int? PowerDc4 { get; set; }

        [JsonPropertyName("batStatus")]
        public string BatStatus { get; set; } = string.Empty;

        [JsonPropertyName("utcDateTime")]
        public DateTime UtcDateTime { get; set; }
    }
}
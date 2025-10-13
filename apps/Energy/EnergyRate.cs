namespace HomeAssistant.apps.Energy;

public class EnergyRate
{
    public DateTime StartTimeUtc { get; set; }
    public DateTime EndTimeUtc { get; set; }
    public double RateIncVat { get; set; }
    public double RateExcVat { get; set; }
}
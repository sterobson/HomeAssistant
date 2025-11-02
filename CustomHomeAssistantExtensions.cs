namespace HomeAssistantGenerated;

public partial record BinarySensorEntity
{
    public const string StateOn = "on";
    public const string StateOff = "off";
    public const string StateUnknown = "unknown";
    public const string StateUnavailble = "unavailble";

    public bool? IsOn
    {
        get
        {
            return State switch
            {
                StateOn => true,
                StateOff => false,
                _ => null
            };
        }
    }
}

public partial record ClimateSetFanModeParameters
{
    public const string FanModeLow = "low";
    public const string FanModeMedium = "medium";
    public const string FanModeHigh = "high";
}
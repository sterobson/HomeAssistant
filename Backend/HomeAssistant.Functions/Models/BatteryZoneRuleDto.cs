namespace HomeAssistant.Functions.Models;

public class BatteryZoneRulesDto
{
    public List<BatteryZoneRuleDto> Rules { get; set; } = [];
}

public class BatteryZoneRuleDto
{
    public string Id { get; set; } = string.Empty;
    public TimeDefinitionDto StartTime { get; set; } = new();
    public TimeDefinitionDto EndTime { get; set; } = new();
    public string Action { get; set; } = string.Empty;
    public int TargetPercent { get; set; }
}

public class TimeDefinitionDto
{
    public string Type { get; set; } = string.Empty;
    public int? FixedTimeMinutes { get; set; }
    public string? PriceType { get; set; }
    public string? ExtremaType { get; set; }
    public string? RegionType { get; set; }
}

using System.Collections.Generic;

namespace HomeAssistant.Services.Energy;

public class BatteryZoneRules
{
    public List<BatteryZoneRule> Rules { get; set; } = [];
}

public class BatteryZoneRule
{
    public string Id { get; set; } = string.Empty;
    public TimeDefinition StartTime { get; set; } = new();
    public TimeDefinition EndTime { get; set; } = new();
    public BatteryZoneAction Action { get; set; } = BatteryZoneAction.Import;
    public int TargetPercent { get; set; }
}

public enum BatteryZoneAction
{
    Import,
    Export
}

public class TimeDefinition
{
    public TimeDefinitionType Type { get; set; } = TimeDefinitionType.FixedTime;
    public int? FixedTimeMinutes { get; set; }
    public string? PriceType { get; set; }
    public string? ExtremaType { get; set; }
    public string? RegionType { get; set; }
    public string? ThresholdType { get; set; }
    public double? ThresholdValue { get; set; }
}

public enum TimeDefinitionType
{
    FixedTime,
    StartOfCheapImport,
    EndOfCheapImport,
    StartOfExpensiveImport,
    EndOfExpensiveImport,
    ExportExceedsImport,
    ImportExceedsExport
}

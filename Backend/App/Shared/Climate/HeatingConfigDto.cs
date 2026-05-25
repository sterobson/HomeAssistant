using System.Collections.Generic;

namespace HomeAssistant.Shared.Climate;

public class HeatingConfigDto
{
    public List<HeatingRoomDto> Rooms { get; set; } = [];
    public List<AutomationRuleDto> Rules { get; set; } = [];
}

public class HeatingRoomDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Floor { get; set; }
    public BoilerPriority Priority { get; set; } = BoilerPriority.Medium;
    public List<HeatingDeviceDto> Devices { get; set; } = [];
    public List<HeatingScheduleDto> Schedules { get; set; } = [];
}

public class HeatingDeviceDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public HeatingDeviceType Type { get; set; }
    public string? SensorEntityId { get; set; }
    public string? TargetEntityId { get; set; }
    public string? SwitchEntityId { get; set; }
    public string? PowerSensorEntityId { get; set; }
    public BoilerControlMode? ControlMode { get; set; }
    public List<string> RuleIds { get; set; } = [];
    public RuleCombinator RuleCombinator { get; set; } = RuleCombinator.And;
}

public class HeatingScheduleDto
{
    public string Id { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public double Temperature { get; set; }
    public int RampUpMinutes { get; set; }
    public Days Days { get; set; } = Days.Everyday;
    public ConditionType Conditions { get; set; } = ConditionType.None;
}

public class AutomationRuleDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;

    // Presence rule fields
    public string? SensorEntityId { get; set; }
    public int? MotionWithinMinutes { get; set; }

    // Power rule fields
    public double? PowerThresholdWatts { get; set; }

    // Time rule fields
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
}

public enum BoilerPriority
{
    Low = 1,
    Medium = 2,
    High = 3
}

public enum HeatingDeviceType
{
    RadiatorValve = 0,
    GasBoiler = 1,
    TemperatureSensor = 2,
    HumiditySensor = 3,
    ElectricHeater = 4,
    PresenceSensor = 5,
    PlugSocket = 6
}

public enum BoilerControlMode
{
    OnWhenOn = 0,
    OffWhenOn = 1,
    Toggle = 2
}

public enum RuleCombinator
{
    And = 0,
    Or = 1
}

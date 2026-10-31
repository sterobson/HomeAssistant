namespace HomeAssistant.Functions.Models;

public class DeviceSettingsDto
{
    public BatterySettingsDto? Battery { get; set; }
    public CarChargerSettingsDto? CarCharger { get; set; }
    public RealtimePowerSettingsDto? RealtimePower { get; set; }
    public HeatingSettingsDto? Heating { get; set; }
}

public class RealtimePowerSettingsDto
{
    public string? GridImportPowerSensorId { get; set; }
    public string? GridExportPowerSensorId { get; set; }
    public string? SolarPowerSensorId { get; set; }
    public string? BatteryPowerSensorId { get; set; }
    public string? HousePowerSensorId { get; set; }
}

public class CarChargerSettingsDto
{
    public string? ChargerCurrentSensorId { get; set; }
}

public class BatterySettingsDto
{
    public string? BatteryChargePercentSensorId { get; set; }
    public string? ChargerUseModeSelectorId { get; set; }
    public string? ManualModeSelectorId { get; set; }
    public string? BatteryChargeMaxCurrentNumberId { get; set; }
    public string? TotalBatteryPowerChargeSensorId { get; set; }
    public string? TotalPvPowerSensorId { get; set; }
    public string? ExportLimitNumberId { get; set; }
    public string? ElectricityRateSensorId { get; set; }
    public string? ExportRateSensorId { get; set; }
    public double? BatteryCapacityKwh { get; set; }
    public int? MaxChargeCurrentAmps { get; set; }
    public int? BatteryFullPercent { get; set; }
    public int? MinDischargePercent { get; set; }
}

public class HeatingSettingsDto
{
    /// <summary>
    /// Whether the app runs the boiler through its relay, or leaves it to the boiler thermostat (the default).
    /// Unrelated to the heating-config stack's BoilerControlMode, which describes how a boiler switch is actuated.
    /// </summary>
    public BoilerControlPath BoilerControlPath { get; set; } = BoilerControlPath.BoilerThermostat;

    /// <summary>
    /// Per-device heating rules keyed by entity ID; null = never configured. Stored and served as-is:
    /// the NetDaemon app evaluates them. Mirrors Backend/App/Shared/DeviceSettingsDto.cs — a field
    /// missing here is dropped on save, so keep the two in step.
    /// </summary>
    public Dictionary<string, HomeAssistant.Shared.Climate.DeviceRulesDto>? DeviceRules { get; set; }

    /// <summary>
    /// Presence detector ids per zone (canonical name); null = never configured. Stored and served as-is: the
    /// NetDaemon app evaluates them. Mirrors Backend/App/Shared/DeviceSettingsDto.cs — keep the two in step.
    /// </summary>
    public Dictionary<string, List<string>>? ZoneOccupancy { get; set; }

    /// <summary>
    /// Where the web UI shows battery devices outside any zone's heating, by Home Assistant area name:
    /// <c>"room:{id}"</c> or <c>"group:{id}"</c>. Presentation only. Mirrors Backend/App/Shared/DeviceSettingsDto.cs
    /// — keep the two in step.
    /// </summary>
    public Dictionary<string, string>? AreaPlacement { get; set; }
}

/// <summary>
/// Who runs the boiler. Stored as a number; keep the values. Mirrors Backend/App/Shared/DeviceSettingsDto.cs.
/// </summary>
public enum BoilerControlPath
{
    /// <summary>Run by hand with the boiler thermostat; the app leaves the boiler alone. The default.</summary>
    BoilerThermostat = 0,

    /// <summary>The app runs the boiler through its relay (was the fingerbot until 2026-10-31).</summary>
    Relay = 1
}

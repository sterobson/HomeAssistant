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
    /// Which control path the boiler is physically wired to. Nothing in Home Assistant can
    /// read the switch at the boiler, so this has to be set by hand to match it, and defaults
    /// to the safe option of leaving the boiler alone. Unrelated to the heating-config stack's
    /// BoilerControlMode, which describes how a boiler switch is actuated.
    /// </summary>
    public BoilerControlPath BoilerControlPath { get; set; } = BoilerControlPath.ExternalScheduler;
}

/// <summary>
/// The two exclusive boiler control paths. A physical switch at the boiler selects between
/// them and is invisible to Home Assistant, so this value is a statement of belief about the
/// house, not a reading from it.
/// </summary>
public enum BoilerControlPath
{
    /// <summary>
    /// The boiler is driven by its own temperature/time scheduler in the kitchen, which
    /// leaves the boiler's physical button dead. Fingerbot presses are no-ops, so the app
    /// must not attempt boiler control at all. This is the default.
    /// </summary>
    ExternalScheduler = 0,

    /// <summary>
    /// The boiler's button is live and the fingerbot can press it, so the app owns boiler
    /// control.
    /// </summary>
    Fingerbot = 1
}

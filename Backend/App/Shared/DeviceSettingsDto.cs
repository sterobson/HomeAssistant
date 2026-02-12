namespace HomeAssistant.Shared;

public class DeviceSettingsDto
{
    public BatterySettingsDto? Battery { get; set; }
    public CarChargerSettingsDto? CarCharger { get; set; }
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

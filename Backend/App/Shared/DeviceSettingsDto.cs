using System.Collections.Generic;

namespace HomeAssistant.Shared;

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
    /// Extra rules for individual heating devices (heater plugs and radiator valves), keyed by the
    /// device's Home Assistant entity ID. A device runs only while its zone calls for heat AND its
    /// rules allow it. <c>null</c> means never configured, and the app falls back to its built-in
    /// defaults (DefaultDeviceRules); once saved from the UI this map is authoritative and a device
    /// with no entry has no rules. Keep in step with the copy in the Functions project.
    /// </summary>
    public Dictionary<string, HomeAssistant.Shared.Climate.DeviceRulesDto>? DeviceRules { get; set; }

    /// <summary>
    /// Which presence detectors count for each zone, keyed by the zone's canonical name, each a list of detector
    /// ids (PresenceService). A zone is in use while any of its detectors is. <c>null</c> means never configured:
    /// the built-in defaults apply. Once saved it is authoritative and a zone with no entry has no presence.
    /// Keep in step with the copy in the Functions project.
    /// </summary>
    public Dictionary<string, List<string>>? ZoneOccupancy { get; set; }

    /// <summary>
    /// Where the web UI shows battery devices that are not part of any zone's heating (buttons, motion and flood
    /// sensors), by their Home Assistant area name: <c>"room:{id}"</c> for a zone or <c>"group:{id}"</c> for a
    /// layout group (owner, 2026-10-20). Presentation only; the NetDaemon app never reads it. An area with no
    /// entry, or a device with no area, shows at the top of the heating page. Keep in step with the copy in the
    /// Functions project.
    /// </summary>
    public Dictionary<string, string>? AreaPlacement { get; set; }
}

/// <summary>
/// Who runs the boiler. Stored as a number; keep the values.
/// </summary>
public enum BoilerControlPath
{
    /// <summary>
    /// Run by hand with the boiler thermostat (portable, normally in the kitchen). The app leaves the boiler, and its
    /// relay, alone, and only controls radiator valves and heater plugs. This is the default.
    /// </summary>
    BoilerThermostat = 0,

    /// <summary>
    /// The app runs the boiler through its relay (<c>switch.den_boiler_relay</c>, since 2026-10-31). Until then this was
    /// the fingerbot pressing the boiler's button; the relay replaced it, so a saved 1 now means the relay.
    /// </summary>
    Relay = 1
}

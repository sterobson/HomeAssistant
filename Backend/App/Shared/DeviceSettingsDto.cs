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
    /// Which control path the boiler is physically wired to. Nothing in Home Assistant can
    /// read the switch at the boiler, so this has to be set by hand to match it, and defaults
    /// to the safe option of leaving the boiler alone. Unrelated to the heating-config stack's
    /// BoilerControlMode, which describes how a boiler switch is actuated.
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
/// The two exclusive boiler control paths. A physical switch at the boiler selects between
/// them and is invisible to Home Assistant, so this value is a statement of belief about the
/// house, not a reading from it.
/// </summary>
public enum BoilerControlPath
{
    /// <summary>
    /// The boiler is driven by the boiler thermostat (portable, normally in the kitchen), which
    /// leaves the boiler's physical button dead. Fingerbot presses are no-ops, so the app
    /// must not attempt boiler control at all. This is the default.
    /// </summary>
    BoilerThermostat = 0,

    /// <summary>
    /// The boiler's button is live and the fingerbot can press it, so the app owns boiler
    /// control.
    /// </summary>
    Fingerbot = 1
}

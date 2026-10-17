using System;
using System.Collections.Generic;

namespace HomeAssistant.Shared;

/// <summary>
/// A day's reading for one replaceable battery in the house — the coin cells and AAs in the
/// thermometers, TRVs, buttons and the boiler fingerbot. Not the SolaX home battery, the car,
/// or anyone's phone; those are charge levels, not cells anyone is going to replace.
/// </summary>
public class DeviceBatteryReadingDto
{
    /// <summary>
    /// The Home Assistant battery sensor, e.g. sensor.bedroom_1_radiator_battery. Stable
    /// across renames, unlike the friendly name, so this is the identity of the reading.
    /// </summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>
    /// The sensor's friendly name at the time of the reading, e.g. "Bedroom 1 - Radiator
    /// Battery". Recorded per reading rather than looked up later because entity IDs are not
    /// renamed when a device is, so the ID alone can name the wrong room.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Charge percentage, or null when the sensor did not report one.
    /// </summary>
    public double? Percent { get; set; }

    /// <summary>
    /// False when the sensor was unavailable. This is deliberately separate from
    /// <see cref="Percent"/>: an unavailable battery sensor is not a flat battery, and
    /// recording it as 0% would turn a dead or unreachable device into a false reading.
    /// A run of unavailable days is itself the signal that something has stopped reporting.
    /// </summary>
    public bool IsAvailable { get; set; }
}

/// <summary>
/// One day's readings for every replaceable battery in the house.
/// </summary>
public class DeviceBatterySnapshotDto
{
    public DateTimeOffset RecordedAt { get; set; }

    public List<DeviceBatteryReadingDto> Readings { get; set; } = [];
}

/// <summary>
/// Battery readings over a range of days, newest first.
/// </summary>
public class DeviceBatteryHistoryDto
{
    public List<DeviceBatterySnapshotDto> Snapshots { get; set; } = [];
}

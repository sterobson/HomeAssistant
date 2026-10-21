// How long a radiator or electric heater takes to warm its room (owner, 2026-10-20; heater plugs since 2026-10-21),
// as measured by the NetDaemon app and published on each radiator valve and heater plug in a room state's `devices`:
//
//   { preHeatAuto, heatingRateCelsiusPerHour, heatingRateSamples, warmUpMinutes }
//
// The rate is the average of the device's last few warm-ups (up to 5, each ending when the room reached its target):
// a valve's with the boiler on and of at least half an hour, a heater's of at least 5 minutes (owner, 2026-10-21). `warmUpMinutes` is how long that rate says it would take to
// bring the room up to the next warmer schedule track from the temperature it is now; null with no rate, or with no
// warmer track to warm up for. An Auto valve with no rate yet pre-heats for 30 minutes.

/** What an Auto valve uses until it has a measured rate. Matches DeviceRulesDto.DefaultValvePreHeatMinutes in the app. */
export const FALLBACK_WARM_UP_MINUTES = 30

/** "45 min", "1 h" or "1 h 10 min". */
export const formatMinutes = (minutes) => {
  const total = Math.round(minutes)
  if (total < 60) return `${total} min`
  const hours = Math.floor(total / 60)
  const rest = total % 60
  return rest === 0 ? `${hours} h` : `${hours} h ${rest} min`
}

/**
 * A heating rate for display in the user's unit. It is a difference, not a temperature, so Fahrenheit scales by
 * 9/5 without the 32 offset.
 * @param {number} celsiusPerHour
 * @param {string} unit 'C' or 'F'
 */
export const formatRate = (celsiusPerHour, unit = 'C') => {
  const value = unit === 'F' ? celsiusPerHour * 9 / 5 : celsiusPerHour
  return `${value.toFixed(1)}°${unit} an hour`
}

const isRadiatorValve = (device) => device?.role === 'radiatorValve'
const isHeaterPlug = (device) => device?.role === 'heaterPlug'

/** The shortest warm-up the app counts, in minutes: WarmUpRates.MinimumMinutes / HeaterMinimumMinutes. */
const minimumWarmUpMinutes = (device) => (isHeaterPlug(device) ? 5 : 30)

/**
 * How long a radiator valve or heater plug is expected to need: its measured warm-up, or for an Auto valve with no
 * data the 30 minutes it uses meanwhile (flagged as assumed; an Auto heater with no data has none). Null for a device
 * that has a rate but no warmer track coming, and for a fixed pre-heat with no rate.
 * @returns {{ minutes: number, assumed: boolean }|null}
 */
export const expectedWarmUp = (device) => {
  if (!isRadiatorValve(device) && !isHeaterPlug(device)) return null
  if (device.warmUpMinutes != null) return { minutes: device.warmUpMinutes, assumed: false }
  if (isRadiatorValve(device) && device.preHeatAuto && device.heatingRateCelsiusPerHour == null) {
    return { minutes: FALLBACK_WARM_UP_MINUTES, assumed: true }
  }
  return null
}

/**
 * The longest warm-up among a zone's radiator valves, since the room is only warm once the slowest has done its
 * part. Null when the zone has no radiator valve, or none has anything to say.
 * @param {object[]} devices a zone's devices
 * @returns {{ minutes: number, assumed: boolean }|null}
 */
export const zoneWarmUp = (devices) => {
  let longest = null
  for (const device of (devices ?? []).filter(isRadiatorValve)) {
    const warmUp = expectedWarmUp(device)
    if (warmUp && (longest === null || warmUp.minutes > longest.minutes)) longest = warmUp
  }
  return longest
}

/**
 * What a radiator valve's or heater plug's warm-up rests on, for the line under its name: "Warms its room about
 * 1.6°C an hour (average of its last 3 heatings)", or that there is nothing measured yet. Null for anything else.
 * @param {object} device
 * @param {string} [unit]
 */
export const describeWarmUpRate = (device, unit = 'C') => {
  if (!isRadiatorValve(device) && !isHeaterPlug(device)) return null
  if (device.heatingRateCelsiusPerHour == null) {
    const minimum = minimumWarmUpMinutes(device)
    return `Warm-up not measured yet: it takes a heating of at least ${minimum === 30 ? 'half an hour' : `${minimum} minutes`} that reaches the target`
  }
  const count = device.heatingRateSamples ?? 0
  const basis = count === 1 ? 'its last heating' : `the average of its last ${count} heatings`
  return `Warms its room about ${formatRate(device.heatingRateCelsiusPerHour, unit)} (${basis})`
}

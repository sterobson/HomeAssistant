// Pure helpers for the guest override page (/r/:token).
//
// The server sends the limits with every zone (`limits` on GET guest/room) and enforces them again on
// POST, so these helpers only keep the form inside what the server will accept. DEFAULT_LIMITS is used
// until the first response arrives.

export const DEFAULT_LIMITS = Object.freeze({
  minTemperature: 16,
  maxTemperature: 24,
  maxMinutes: 360
})

export const TEMPERATURE_STEP = 0.5
export const DURATION_STEP_MINUTES = 30
export const DEFAULT_TEMPERATURE = 20
export const DEFAULT_DURATION_MINUTES = 60

const resolveLimits = (limits) => ({ ...DEFAULT_LIMITS, ...(limits ?? {}) })

/**
 * Human wording for a length of time: "30 minutes", "1 hour", "1 hour 30 minutes", "6 hours".
 * @param {number} minutes
 * @returns {string}
 */
export const formatDuration = (minutes) => {
  const hours = Math.floor(minutes / 60)
  const rest = minutes % 60
  const parts = []
  if (hours > 0) parts.push(`${hours} ${hours === 1 ? 'hour' : 'hours'}`)
  if (rest > 0) parts.push(`${rest} ${rest === 1 ? 'minute' : 'minutes'}`)
  return parts.length > 0 ? parts.join(' ') : '0 minutes'
}

/**
 * The durations a guest can pick, from 30 minutes up to the limit in 30-minute steps.
 * @param {{ maxMinutes?: number }} [limits]
 * @returns {{ minutes: number, label: string }[]}
 */
export const durationOptions = (limits) => {
  const { maxMinutes } = resolveLimits(limits)
  const options = []
  for (let minutes = DURATION_STEP_MINUTES; minutes <= maxMinutes; minutes += DURATION_STEP_MINUTES) {
    options.push({ minutes, label: formatDuration(minutes) })
  }
  return options
}

/**
 * Round to the nearest half degree and keep inside the limits. Anything that is not a number falls back
 * to the default (itself clamped).
 * @param {number} value
 * @param {{ minTemperature?: number, maxTemperature?: number }} [limits]
 * @returns {number}
 */
export const clampTemperature = (value, limits) => {
  const { minTemperature, maxTemperature } = resolveLimits(limits)
  const numeric = typeof value === 'number' && Number.isFinite(value) ? value : DEFAULT_TEMPERATURE
  const stepped = Math.round(numeric / TEMPERATURE_STEP) * TEMPERATURE_STEP
  return Math.min(maxTemperature, Math.max(minTemperature, stepped))
}

/**
 * Snap to a whole 30-minute step and keep inside 30 minutes … the limit.
 * @param {number} value
 * @param {{ maxMinutes?: number }} [limits]
 * @returns {number}
 */
export const clampDuration = (value, limits) => {
  const { maxMinutes } = resolveLimits(limits)
  const numeric = typeof value === 'number' && Number.isFinite(value) ? value : DEFAULT_DURATION_MINUTES
  const stepped = Math.round(numeric / DURATION_STEP_MINUTES) * DURATION_STEP_MINUTES
  return Math.min(maxMinutes, Math.max(DURATION_STEP_MINUTES, stepped))
}

/**
 * Check a request before sending it. Returns the first problem as a sentence, or null when it is fine.
 * @param {number} temperature
 * @param {number} durationMinutes
 * @param {object} [limits]
 * @returns {string|null}
 */
export const validateOverride = (temperature, durationMinutes, limits) => {
  const { minTemperature, maxTemperature, maxMinutes } = resolveLimits(limits)

  if (typeof temperature !== 'number' || !Number.isFinite(temperature)) {
    return 'Choose a temperature.'
  }
  if (temperature < minTemperature || temperature > maxTemperature) {
    return `The temperature must be between ${minTemperature} and ${maxTemperature} °C.`
  }
  if (typeof durationMinutes !== 'number' || !Number.isFinite(durationMinutes) || durationMinutes < DURATION_STEP_MINUTES) {
    return 'Choose how long it should last.'
  }
  if (durationMinutes > maxMinutes) {
    return `It can last at most ${formatDuration(maxMinutes)}.`
  }
  return null
}

/**
 * Move a temperature one step up or down, staying inside the limits.
 * @param {number} current
 * @param {1|-1} direction
 * @param {object} [limits]
 * @returns {number}
 */
export const stepTemperature = (current, direction, limits) =>
  clampTemperature(current + direction * TEMPERATURE_STEP, limits)

/**
 * "17.5" / "20" — no trailing ".0".
 * @param {number} temperature
 * @returns {string}
 */
export const formatTemperature = (temperature) => {
  if (typeof temperature !== 'number' || !Number.isFinite(temperature)) return '–'
  return `${Number.isInteger(temperature) ? temperature : temperature.toFixed(1)} °C`
}

/**
 * When an override ends, in the viewer's local time: "17:30" if it is today, otherwise
 * "Tomorrow 01:30" or "Wed 01:30". Returns '' for anything that is not a date.
 * @param {string|Date} endTime ISO string or Date
 * @param {Date} [now]
 * @returns {string}
 */
export const formatEndTime = (endTime, now = new Date()) => {
  const end = endTime instanceof Date ? endTime : new Date(endTime)
  if (Number.isNaN(end.getTime())) return ''

  const clock = end.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', hour12: false })
  const startOfDay = (date) => new Date(date.getFullYear(), date.getMonth(), date.getDate()).getTime()
  const dayDifference = Math.round((startOfDay(end) - startOfDay(now)) / 86_400_000)

  if (dayDifference === 0) return clock
  if (dayDifference === 1) return `Tomorrow ${clock}`
  return `${end.toLocaleDateString([], { weekday: 'short' })} ${clock}`
}

/**
 * True while the override has not yet ended.
 * @param {{ endTime?: string }|null|undefined} override
 * @param {Date} [now]
 * @returns {boolean}
 */
export const isOverrideActive = (override, now = new Date()) => {
  if (!override?.endTime) return false
  const end = new Date(override.endTime)
  return !Number.isNaN(end.getTime()) && end.getTime() > now.getTime()
}

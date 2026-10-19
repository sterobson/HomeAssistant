// Per-device heating rules: when a heater plug or radiator valve may be switched on.
//
// Stored in the device-settings blob as `heating.deviceRules`, which is either null (never
// configured, so the app uses its built-in defaults) or an object keyed by Home Assistant entity ID:
//
//   { timeWindows: [{ days, start: 'HH:mm', end: 'HH:mm' }], presence: { match, sources } | null }
//
// - `days` is the Days flags enum used by schedules (Mon = 1 ... Sun = 64, 127 = every day).
// - An `end` at or before `start` means the window runs past midnight.
// - No windows means any time. Null presence, or no sources, means no presence is needed.
// - `match` is 0 for Any and 1 for All.
// - A source is 'house' (any presence zone in use) or a zone's canonical `name`.
//
// While the blob is null each drivable device on a room state carries its effective built-in
// `rules` and `rulesAreDefault`.

import { roomLabel } from './rooms.js'

export const HOUSE_SOURCE = 'house'
export const PRESENCE_MATCH = { ANY: 0, ALL: 1 }
export const ALL_DAYS = 127

// Display order is Monday first, matching the flag values.
export const DAYS = [
  { value: 1, short: 'M', label: 'Mon', full: 'Monday' },
  { value: 2, short: 'T', label: 'Tue', full: 'Tuesday' },
  { value: 4, short: 'W', label: 'Wed', full: 'Wednesday' },
  { value: 8, short: 'T', label: 'Thu', full: 'Thursday' },
  { value: 16, short: 'F', label: 'Fri', full: 'Friday' },
  { value: 32, short: 'S', label: 'Sat', full: 'Saturday' },
  { value: 64, short: 'S', label: 'Sun', full: 'Sunday' }
]

const WEEKDAYS = 31
const WEEKEND = 96
const TIME_PATTERN = /^([01]\d|2[0-3]):[0-5]\d$/
const DRIVABLE_ROLES = ['heaterPlug', 'radiatorValve']

const naturalCompare = (a, b) => a.localeCompare(b, undefined, { numeric: true, sensitivity: 'base' })

/** True for the devices the heating code switches, which are the only ones that carry rules. */
export const isDrivable = (device) => DRIVABLE_ROLES.includes(device?.role)

/** Rules that constrain nothing: any time, no presence needed. */
export const noRules = () => ({ timeWindows: [], presence: null })

/**
 * The rules in force for a device. Once the blob exists it is the whole truth, so a device it does
 * not mention has no rules; until then the device carries the built-in defaults.
 * @param {{ entityId: string, rules?: object|null }} device
 * @param {Record<string, object>|null|undefined} deviceRulesBlob `heating.deviceRules` from the settings
 * @returns {object|null}
 */
export const effectiveRules = (device, deviceRulesBlob) => {
  if (deviceRulesBlob != null) {
    return deviceRulesBlob[device?.entityId] ?? null
  }
  return device?.rules ?? null
}

/**
 * The blob to save when it is still null. Saving one device must not wipe the built-in defaults of
 * every other, so each drivable device's current default is written out alongside the new rules.
 * @param {object[]} allDevices every drivable device, possibly with repeats (a device can sit in two zones)
 * @param {string} entityId
 * @param {object} newRules
 * @returns {Record<string, object>}
 */
export const firstSaveMap = (allDevices, entityId, newRules) => {
  const map = {}
  for (const device of allDevices ?? []) {
    if (device?.entityId && isDrivable(device) && device.rules != null && !(device.entityId in map)) {
      map[device.entityId] = device.rules
    }
  }
  map[entityId] = newRules
  return map
}

const daysText = (days) => {
  if (days === ALL_DAYS) return 'every day'
  if (days === WEEKDAYS) return 'Mon–Fri'
  if (days === WEEKEND) return 'Sat–Sun'
  return DAYS.filter(d => (days & d.value) !== 0).map(d => d.label).join(', ')
}

const joinList = (items, word) =>
  items.length <= 1 ? items.join('') : `${items.slice(0, -1).join(', ')} ${word} ${items[items.length - 1]}`

/**
 * A short sentence-case description, e.g. "05:30–22:00 every day · House".
 * @param {object|null} rules
 * @param {(source: string) => string} zoneLabel label for a zone name; 'house' is handled here
 */
export const summariseRules = (rules, zoneLabel = (name) => name) => {
  const windows = rules?.timeWindows ?? []
  const everyDay = windows.every(w => w.days === ALL_DAYS)

  let timeText = 'Any time'
  if (windows.length > 0) {
    timeText = windows
      .map(w => {
        const range = `${w.start}–${w.end}`
        // Only a single window spells out "every day"; a list of them would repeat it on each.
        return everyDay ? range : `${range} ${daysText(w.days)}`
      })
      .join(', ')
    if (everyDay && windows.length === 1) timeText += ' every day'
  }

  const sources = rules?.presence?.sources ?? []
  if (sources.length === 0) return `${timeText} · no presence needed`

  const labels = sources.map(s => (s === HOUSE_SOURCE ? 'House' : zoneLabel(s)))
  const word = rules.presence.match === PRESENCE_MATCH.ALL ? 'and' : 'or'
  return `${timeText} · ${joinList(labels, word)}`
}

/**
 * Why a window cannot be saved, or null when it is fine.
 * @param {{ days: number, start: string, end: string }} window
 * @returns {string|null}
 */
export const validateWindow = (window) => {
  if (!window || !TIME_PATTERN.test(window.start ?? '')) return 'Enter a start time.'
  if (!TIME_PATTERN.test(window.end ?? '')) return 'Enter an end time.'
  if (!Number.isInteger(window.days) || (window.days & ALL_DAYS) === 0) return 'Choose at least one day.'
  return null
}

/**
 * The zones a presence rule can name: every room whose state has the occupancy capability (flag 2),
 * labelled and naturally sorted. Rooms sharing a name appear once.
 * @param {object[]} rooms rooms merged with their states
 * @returns {{ name: string, label: string }[]}
 */
export const presenceZones = (rooms) => {
  const byName = new Map()
  for (const room of rooms ?? []) {
    if (room && ((room.capabilities ?? 0) & 2) !== 0 && !byName.has(room.name)) {
      byName.set(room.name, { name: room.name, label: roomLabel(room) })
    }
  }
  return [...byName.values()].sort((a, b) => naturalCompare(a.label, b.label))
}

/** Every drivable device across the rooms, each entity once. */
export const drivableDevices = (rooms) => {
  const byEntity = new Map()
  for (const room of rooms ?? []) {
    for (const device of room?.devices ?? []) {
      if (isDrivable(device) && !byEntity.has(device.entityId)) byEntity.set(device.entityId, device)
    }
  }
  return [...byEntity.values()]
}

/** Tidies rules for saving: drops a presence rule with no sources and keeps only known fields. */
export const normaliseRules = (rules) => {
  const sources = [...new Set(rules?.presence?.sources ?? [])]
  return {
    timeWindows: (rules?.timeWindows ?? []).map(w => ({ days: w.days, start: w.start, end: w.end })),
    presence: sources.length === 0
      ? null
      : { match: sources.length > 1 ? (rules.presence.match ?? PRESENCE_MATCH.ANY) : PRESENCE_MATCH.ANY, sources }
  }
}

// The boiler's on/off history (owner, 2026-10-21), from GET /api/boiler-history:
//
//   { from, to, before: { at, on, appControlsBoiler } | null, events: [{ at, on, appControlsBoiler }] }
//
// Each event is a change in what the NetDaemon app believed, recorded by the Functions app from the room-state uploads.
// A belief, not a reading: nothing in Home Assistant can see the boiler (CLAUDE.md, "The boiler has no feedback"), and
// while the boiler thermostat runs the boiler the app knows nothing of it. History starts when the Functions app that
// records it was deployed; before that the window is "unknown", unless it was backfilled. Backfilled events carry
// `inferred: true`: worked out from the radiator valves' own sensors warming and cooling, so what the boiler evidently
// did (whoever ran it), not what the app believed. Their stretches carry `inferred` too, and are drawn differently.

/** What the boiler was doing over a stretch: 'on', 'off', 'thermostat' (the app was not running it) or 'unknown'. */
const stateOf = (event) => {
  if (!event) return 'unknown'
  if (!event.appControlsBoiler) return 'thermostat'
  return event.on ? 'on' : 'off'
}

/**
 * The window cut into stretches of one state each, oldest first, adjacent stretches of the same state merged.
 * @param {{ before?: object|null, events?: object[] }} history
 * @param {Date} from
 * @param {Date} to
 * @returns {{ start: Date, end: Date, state: string }[]}
 */
export const boilerSegments = (history, from, to) => {
  const segments = []
  const push = (start, end, state, inferred) => {
    if (end <= start) return
    const last = segments.at(-1)
    if (last && last.state === state && last.inferred === inferred && last.end.getTime() === start.getTime()) {
      last.end = end
    } else {
      segments.push({ start, end, state, inferred })
    }
  }

  let cursor = from
  let state = stateOf(history?.before)
  let inferred = history?.before?.inferred === true
  const events = [...(history?.events ?? [])].sort((a, b) => new Date(a.at) - new Date(b.at))
  for (const event of events) {
    const at = new Date(event.at)
    if (Number.isNaN(at.getTime()) || at >= to) continue
    if (at > cursor) {
      push(cursor, at, state, inferred)
      cursor = at
    }
    state = stateOf(event)
    inferred = event.inferred === true
  }
  push(cursor, to, state, inferred)
  return segments
}

/** How long the segments say the boiler was on, in minutes. */
export const minutesOn = (segments) =>
  Math.round(segments.filter(s => s.state === 'on').reduce((total, s) => total + (s.end - s.start), 0) / 60000)

/** How many times the app turned the boiler on in the window (stretches of 'on' that began inside it). */
export const timesOn = (segments, from) =>
  segments.filter(s => s.state === 'on' && s.start > from).length

/** A label for a state, in sentence case. */
export const stateLabel = (state) => ({
  on: 'On',
  off: 'Off',
  thermostat: 'Run by the boiler thermostat',
  unknown: 'Not recorded'
})[state] ?? state

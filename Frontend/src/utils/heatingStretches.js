// When a zone was genuinely heating (owner, 2026-11-03), and from which source.
//
// A zone has up to two kinds of heat: the boiler, through its radiator valves and towel rail, and electric heater
// plugs. A history point (GET /api/room-history) says whether the zone was heating (`heatingActive`) and, since
// 2026-11-03, how: `heatingFromBoiler` and `heatingFromElectric`. A point holds until the next one.
//
// The boiler's heat only counts while the boiler was on: its stretches have the time the boiler is known to have been
// off (utils/boilerHistory.js) taken out. A heater plug needs no boiler, so its stretches are left as they are. Where
// the boiler's state is not known (nothing recorded, or the boiler thermostat was running it and the app could not
// see) the boiler's stretches stand as recorded, which is what the chart showed before.
//
// The total is the stretches of all the sources laid over each other, so two sources heating at once count once.
// Each source's own total can therefore add up to more than it.

/** Which kinds of heat a zone has hardware for. */
export const heatSources = (devices) => {
  const roles = (devices ?? []).map(device => device.role)
  return {
    boiler: roles.includes('radiatorValve') || roles.includes('towelRail'),
    electric: roles.includes('heaterPlug')
  }
}

const sortedByTime = (points) => [...(points ?? [])].sort((a, b) => new Date(a.timestamp) - new Date(b.timestamp))

/** The stretches over which `predicate(point, index)` holds, each point running to the next: `[{ start, end }]`. */
const stretchesWhere = (sorted, predicate) => {
  const stretches = []
  for (let i = 0; i < sorted.length - 1; i++) {
    if (!predicate(sorted[i], i)) continue
    const start = new Date(sorted[i].timestamp)
    const end = new Date(sorted[i + 1].timestamp)
    if (end <= start) continue
    const last = stretches.at(-1)
    if (last && last.end.getTime() === start.getTime()) {
      last.end = end
    } else {
      stretches.push({ start, end })
    }
  }
  return stretches
}

/**
 * The stretches the history points say the zone was heating, whatever the source.
 * @param {{ timestamp: string, heatingActive: boolean }[]} points
 */
export const callingStretches = (points) => stretchesWhere(sortedByTime(points), point => point.heatingActive)

/**
 * What a point says about each source: true, false, or null when it cannot be told. Points from before 2026-11-03
 * do not say, so a zone with only one kind of hardware is taken to be heating from that; a zone with both is unknown.
 */
const sourceFlags = (point, sources) => {
  if (point.heatingFromBoiler != null || point.heatingFromElectric != null) {
    return { boiler: point.heatingFromBoiler === true, electric: point.heatingFromElectric === true }
  }
  if (!point.heatingActive) return { boiler: false, electric: false }
  if (sources.boiler && !sources.electric) return { boiler: true, electric: false }
  if (sources.electric && !sources.boiler) return { boiler: false, electric: true }
  return { boiler: null, electric: null }
}

const subtract = (stretches, removed) => {
  let remaining = stretches
  for (const gap of removed) {
    remaining = remaining.flatMap(stretch => {
      if (gap.end <= stretch.start || gap.start >= stretch.end) return [stretch]
      const pieces = []
      if (gap.start > stretch.start) pieces.push({ start: stretch.start, end: gap.start })
      if (gap.end < stretch.end) pieces.push({ start: gap.end, end: stretch.end })
      return pieces
    })
  }
  return remaining
}

/** Several lists of stretches as one: the time when any of them applied, overlapping stretches merged. */
export const unionStretches = (lists) => {
  const sorted = lists.flat().sort((a, b) => a.start - b.start)
  const merged = []
  for (const stretch of sorted) {
    const last = merged.at(-1)
    if (last && stretch.start <= last.end) {
      if (stretch.end > last.end) last.end = stretch.end
    } else {
      merged.push({ start: stretch.start, end: stretch.end })
    }
  }
  return merged
}

/**
 * The stretches the zone was genuinely heating, in total and by source.
 * @param {object[]} points - the zone's history points
 * @param {{ start: Date, end: Date, state: string }[]|null} boilerSegments - from `boilerSegments`, or null if not loaded
 * @param {object[]} devices - the zone's devices (`role`), which say what kinds of heat it has
 * @returns {{ stretches: object[], boiler: object[]|null, electric: object[]|null, unsplit: boolean,
 *   usesBoilerHistory: boolean }}
 *   `boiler` and `electric` are that source's stretches, or null if the zone has no such hardware. `unsplit` is whether
 *   some of the total could not be given to a source (older points of a zone with both). `usesBoilerHistory` is whether
 *   any of the window's boiler state was known and used.
 */
export const genuineHeating = (points, boilerSegments, devices) => {
  const sources = heatSources(devices)
  const sorted = sortedByTime(points)
  const flags = sorted.map(point => sourceFlags(point, sources))

  const known = (boilerSegments ?? []).some(s => s.state === 'on' || s.state === 'off')
  const boilerOff = known ? boilerSegments.filter(s => s.state === 'off') : []

  const boiler = subtract(stretchesWhere(sorted, (_, i) => flags[i].boiler === true), boilerOff)
  const electric = stretchesWhere(sorted, (_, i) => flags[i].electric === true)
  const unsplit = stretchesWhere(sorted, (point, i) => point.heatingActive && flags[i].boiler === null)

  return {
    stretches: unionStretches([boiler, electric, unsplit]),
    boiler: sources.boiler ? boiler : null,
    electric: sources.electric ? electric : null,
    unsplit: unsplit.length > 0,
    usesBoilerHistory: known && sources.boiler
  }
}

/** The total length of the stretches, in minutes. */
export const totalMinutes = (stretches) =>
  stretches.reduce((total, s) => total + (s.end - s.start) / 60000, 0)

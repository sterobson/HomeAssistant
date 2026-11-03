// A group's temperature history (owner, 2026-11-03): at any moment, the range across the zones inside it.
//
// Each zone's history (GET /api/room-history) is a list of points recorded when something changed, and a point holds
// until the next one. A group's series is those step functions laid over each other: one point wherever any zone has
// one, carrying the lowest and highest current temperature and target among the zones that have a value then, and
// the mean current temperature. A zone with no value at that moment (it has not reported yet, its thermometer was
// silent, or it has no target, like the hallway) is left out of that moment's range rather than counted as zero.

/** A step function's value at `time`: the last point at or before it, or null before the first. */
const valueAt = (points, cursor, time) => {
  let index = cursor
  while (index + 1 < points.length && points[index + 1].time <= time) index++
  return { index, point: points[index]?.time <= time ? points[index] : null }
}

/**
 * Lays the zones' histories over each other.
 * @param {{ timestamp: string, currentTemperature: number|null, targetTemperature: number|null }[][]} zoneSeries
 *   one list of history points per zone
 * @returns {{ timestamp: string, currentTemperature: number|null, currentTemperatureMax: number|null,
 *   currentTemperatureMean: number|null, targetTemperature: number|null, targetTemperatureMax: number|null }[]}
 *   oldest first; `currentTemperature` and `targetTemperature` are the lowest, the `...Max` fields the highest
 */
export const mergeGroupHistory = (zoneSeries) => {
  const zones = zoneSeries
    .map(points => points
      .map(point => ({ ...point, time: new Date(point.timestamp).getTime() }))
      .filter(point => !Number.isNaN(point.time))
      .sort((a, b) => a.time - b.time))
    .filter(points => points.length > 0)

  const times = [...new Set(zones.flatMap(points => points.map(point => point.time)))].sort((a, b) => a - b)
  const cursors = zones.map(() => 0)

  return times.map(time => {
    const current = []
    const target = []
    zones.forEach((points, i) => {
      const { index, point } = valueAt(points, cursors[i], time)
      cursors[i] = index
      if (point?.currentTemperature != null) current.push(point.currentTemperature)
      if (point?.targetTemperature != null) target.push(point.targetTemperature)
    })

    return {
      timestamp: new Date(time).toISOString(),
      currentTemperature: current.length ? Math.min(...current) : null,
      currentTemperatureMax: current.length ? Math.max(...current) : null,
      currentTemperatureMean: current.length ? current.reduce((sum, t) => sum + t, 0) / current.length : null,
      targetTemperature: target.length ? Math.min(...target) : null,
      targetTemperatureMax: target.length ? Math.max(...target) : null
    }
  })
}

import { describe, expect, it } from 'vitest'
import { callingStretches, genuineHeating, heatSources, totalMinutes, unionStretches } from './heatingStretches.js'

const point = (time, heatingActive, extra = {}) => ({ timestamp: `2026-11-03T${time}:00Z`, heatingActive, ...extra })
const seg = (start, end, state) => ({ start: new Date(`2026-11-03T${start}:00Z`), end: new Date(`2026-11-03T${end}:00Z`), state })
const shape = (stretches) => stretches.map(s => [s.start.toISOString().slice(11, 16), s.end.toISOString().slice(11, 16)])

const radiator = [{ role: 'thermometer' }, { role: 'radiatorValve' }]
const heater = [{ role: 'heaterPlug' }]
const both = [{ role: 'radiatorValve' }, { role: 'heaterPlug' }]

// Calling for heat 06:00-08:00 and 18:00-20:00, from points that do not say which source.
const points = [point('00:00', false), point('06:00', true), point('08:00', false), point('18:00', true), point('20:00', false), point('23:00', false)]

describe('callingStretches', () => {
  it('runs each heating point to the next point, and sorts what it is given', () => {
    expect(shape(callingStretches([...points].reverse()))).toEqual([['06:00', '08:00'], ['18:00', '20:00']])
  })

  it('merges consecutive heating points, and ignores a final point with nothing after it', () => {
    expect(shape(callingStretches([point('06:00', true), point('06:30', true), point('07:00', false), point('09:00', true)])))
      .toEqual([['06:00', '07:00']])
  })
})

describe('heatSources', () => {
  it('says which kinds of heat the hardware gives', () => {
    expect(heatSources(radiator)).toEqual({ boiler: true, electric: false })
    expect(heatSources([{ role: 'towelRail' }])).toEqual({ boiler: true, electric: false })
    expect(heatSources(heater)).toEqual({ boiler: false, electric: true })
    expect(heatSources(both)).toEqual({ boiler: true, electric: true })
    expect(heatSources(undefined)).toEqual({ boiler: false, electric: false })
  })
})

describe('genuineHeating with a radiator', () => {
  it('cuts out the time the boiler was off', () => {
    const boiler = [seg('00:00', '06:45', 'off'), seg('06:45', '07:30', 'on'), seg('07:30', '18:30', 'off'), seg('18:30', '24:00', 'on')]
    const result = genuineHeating(points, boiler, radiator)
    expect(shape(result.stretches)).toEqual([['06:45', '07:30'], ['18:30', '20:00']])
    expect(totalMinutes(result.stretches)).toBe(45 + 90)
    expect(shape(result.boiler)).toEqual(shape(result.stretches))
    expect(result.electric).toBeNull()
    expect(result.usesBoilerHistory).toBe(true)
  })

  it('keeps a stretch whole when the boiler was on throughout, and drops it when it was off throughout', () => {
    expect(shape(genuineHeating(points, [seg('00:00', '24:00', 'on')], radiator).stretches)).toEqual([['06:00', '08:00'], ['18:00', '20:00']])
    expect(genuineHeating(points, [seg('00:00', '24:00', 'off')], radiator).stretches).toEqual([])
  })

  it('splits a stretch when the boiler went off in the middle of it', () => {
    const boiler = [seg('00:00', '06:30', 'on'), seg('06:30', '07:00', 'off'), seg('07:00', '24:00', 'on')]
    expect(shape(genuineHeating(points, boiler, radiator).stretches)).toEqual([['06:00', '06:30'], ['07:00', '08:00'], ['18:00', '20:00']])
  })

  it('leaves the stretches as they are where the boiler is not known, or there is no boiler history at all', () => {
    const unknown = [seg('00:00', '07:00', 'unknown'), seg('07:00', '12:00', 'thermostat'), seg('12:00', '24:00', 'unknown')]
    const result = genuineHeating(points, unknown, radiator)
    expect(shape(result.stretches)).toEqual([['06:00', '08:00'], ['18:00', '20:00']])
    expect(result.usesBoilerHistory).toBe(false)
    expect(shape(genuineHeating(points, null, radiator).stretches)).toEqual([['06:00', '08:00'], ['18:00', '20:00']])
    expect(shape(genuineHeating(points, [], radiator).stretches)).toEqual([['06:00', '08:00'], ['18:00', '20:00']])
  })

  it('trusts the boiler only where it is known', () => {
    const boiler = [seg('00:00', '07:00', 'unknown'), seg('07:00', '24:00', 'off')]
    expect(shape(genuineHeating(points, boiler, radiator).stretches)).toEqual([['06:00', '07:00']])
  })
})

describe('genuineHeating with a heater plug', () => {
  it('ignores the boiler, which a heater plug does not need', () => {
    const result = genuineHeating(points, [seg('00:00', '24:00', 'off')], heater)
    expect(shape(result.stretches)).toEqual([['06:00', '08:00'], ['18:00', '20:00']])
    expect(result.boiler).toBeNull()
    expect(shape(result.electric)).toEqual([['06:00', '08:00'], ['18:00', '20:00']])
    expect(result.usesBoilerHistory).toBe(false)
  })
})

describe('genuineHeating with both kinds of heat', () => {
  // The radiator 06:00-09:00 and the heater 08:00-10:00, so they overlap 08:00-09:00.
  const sourced = [
    point('00:00', false, { heatingFromBoiler: false, heatingFromElectric: false }),
    point('06:00', true, { heatingFromBoiler: true, heatingFromElectric: false }),
    point('08:00', true, { heatingFromBoiler: true, heatingFromElectric: true }),
    point('09:00', true, { heatingFromBoiler: false, heatingFromElectric: true }),
    point('10:00', false, { heatingFromBoiler: false, heatingFromElectric: false }),
    point('12:00', false, { heatingFromBoiler: false, heatingFromElectric: false })
  ]

  it('counts the time both were on once in the total, and in each source', () => {
    const result = genuineHeating(sourced, null, both)
    expect(shape(result.stretches)).toEqual([['06:00', '10:00']])
    expect(shape(result.boiler)).toEqual([['06:00', '09:00']])
    expect(shape(result.electric)).toEqual([['08:00', '10:00']])
    expect(totalMinutes(result.stretches)).toBe(240)
    expect(totalMinutes(result.boiler) + totalMinutes(result.electric)).toBe(300)
    expect(result.unsplit).toBe(false)
  })

  it('takes boiler-off time from the boiler only, leaving the heater', () => {
    const result = genuineHeating(sourced, [seg('00:00', '07:00', 'on'), seg('07:00', '24:00', 'off')], both)
    expect(shape(result.boiler)).toEqual([['06:00', '07:00']])
    expect(shape(result.electric)).toEqual([['08:00', '10:00']])
    expect(shape(result.stretches)).toEqual([['06:00', '07:00'], ['08:00', '10:00']])
  })

  it('cannot give older points of a zone with both to either source, but counts them in the total', () => {
    const result = genuineHeating(points, [seg('00:00', '24:00', 'off')], both)
    expect(shape(result.stretches)).toEqual([['06:00', '08:00'], ['18:00', '20:00']])
    expect(totalMinutes(result.boiler)).toBe(0)
    expect(totalMinutes(result.electric)).toBe(0)
    expect(result.unsplit).toBe(true)
  })
})

describe('unionStretches', () => {
  it('merges overlapping and touching stretches from several zones', () => {
    const a = [{ start: new Date('2026-11-03T06:00:00Z'), end: new Date('2026-11-03T08:00:00Z') }]
    const b = [{ start: new Date('2026-11-03T07:00:00Z'), end: new Date('2026-11-03T09:00:00Z') }, { start: new Date('2026-11-03T12:00:00Z'), end: new Date('2026-11-03T13:00:00Z') }]
    expect(shape(unionStretches([a, b]))).toEqual([['06:00', '09:00'], ['12:00', '13:00']])
    expect(unionStretches([])).toEqual([])
  })
})

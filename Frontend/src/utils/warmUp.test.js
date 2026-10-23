import { describe, expect, it } from 'vitest'
import {
  describeWarmUpRate,
  expectedWarmUp,
  FALLBACK_WARM_UP_MINUTES,
  formatMinutes,
  formatRate,
  zoneWarmUp
} from './warmUp.js'

const valve = (extra = {}) => ({ role: 'radiatorValve', entityId: 'climate.x', preHeatAuto: true, ...extra })

describe('formatMinutes', () => {
  it('uses minutes below an hour and hours above', () => {
    expect(formatMinutes(45)).toBe('45 min')
    expect(formatMinutes(60)).toBe('1 h')
    expect(formatMinutes(70)).toBe('1 h 10 min')
    expect(formatMinutes(119.6)).toBe('2 h')
  })
})

describe('formatRate', () => {
  it('scales a rate, with no offset, for Fahrenheit', () => {
    expect(formatRate(2, 'C')).toBe('2.0°C an hour')
    expect(formatRate(2, 'F')).toBe('3.6°F an hour')
  })
})

describe('expectedWarmUp', () => {
  it('is the published warm-up when there is one', () => {
    expect(expectedWarmUp(valve({ warmUpMinutes: 45, heatingRateCelsiusPerHour: 1.6 }))).toEqual({ minutes: 45, assumed: false })
  })

  it('is the 30-minute fallback, flagged as assumed, for an Auto valve with no data', () => {
    expect(expectedWarmUp(valve())).toEqual({ minutes: FALLBACK_WARM_UP_MINUTES, assumed: true })
  })

  it('has nothing to say for a valve with a rate but no warmer track coming', () => {
    expect(expectedWarmUp(valve({ heatingRateCelsiusPerHour: 1.6, warmUpMinutes: null }))).toBeNull()
  })

  it('has nothing to say for a fixed pre-heat with no rate, or for anything but a valve', () => {
    expect(expectedWarmUp(valve({ preHeatAuto: false }))).toBeNull()
    expect(expectedWarmUp({ role: 'heaterPlug', preHeatAuto: false })).toBeNull()
    expect(expectedWarmUp({ role: 'thermometer' })).toBeNull()
  })
})

describe('zoneWarmUp', () => {
  it('shows the longest of a zone\'s radiators', () => {
    const devices = [
      valve({ entityId: 'a', warmUpMinutes: 35, heatingRateCelsiusPerHour: 2 }),
      valve({ entityId: 'b', warmUpMinutes: 50, heatingRateCelsiusPerHour: 1.5 }),
      { role: 'thermometer' }
    ]
    expect(zoneWarmUp(devices)).toEqual({ minutes: 50, assumed: false })
  })

  it('lets an assumed figure count too, and says it is assumed', () => {
    const devices = [valve({ entityId: 'a', warmUpMinutes: 20, heatingRateCelsiusPerHour: 3 }), valve({ entityId: 'b' })]
    expect(zoneWarmUp(devices)).toEqual({ minutes: 30, assumed: true })
  })

  it('is null for a zone with no radiators', () => {
    expect(zoneWarmUp([{ role: 'heaterPlug' }, { role: 'thermometer' }])).toBeNull()
    expect(zoneWarmUp([])).toBeNull()
    expect(zoneWarmUp(undefined)).toBeNull()
  })
})

describe('describeWarmUpRate', () => {
  it('says what the rate rests on', () => {
    expect(describeWarmUpRate(valve({ heatingRateCelsiusPerHour: 1.6, heatingRateSamples: 3 }))).toBe(
      'Warms its room about 1.6°C an hour (the average of its last 3 heatings)')
    expect(describeWarmUpRate(valve({ heatingRateCelsiusPerHour: 1.6, heatingRateSamples: 1 }))).toBe(
      'Warms its room about 1.6°C an hour (its last heating)')
  })

  it('says when nothing is measured yet, and says nothing for devices that do not heat', () => {
    expect(describeWarmUpRate(valve())).toMatch(/not measured yet, so Auto starts it 30 min ahead.*half an hour/)
    expect(describeWarmUpRate(valve({ preHeatAuto: false }))).toMatch(/^Warm-up not measured yet: .*half an hour/)
    expect(describeWarmUpRate({ role: 'thermometer' })).toBeNull()
  })

  it('says whose rate an unmeasured valve goes by (since 2026-10-23)', () => {
    const borrowing = valve({ heatingRateCelsiusPerHour: 0.8, heatingRateSamples: 2, heatingRateFromEntityId: 'climate.den_radiator_1_thermostat' })
    const names = { 'climate.den_radiator_1_thermostat': 'Den - Radiator 1 Thermostat' }
    expect(describeWarmUpRate(borrowing, 'C', (id) => names[id])).toBe(
      'Warm-up not measured yet, so it goes by Den - Radiator 1 Thermostat: about 0.8°C an hour (the average of its last 2 heatings)')
    expect(describeWarmUpRate(borrowing)).toMatch(/goes by climate\.den_radiator_1_thermostat/)
  })

  it('counts a borrowed rate as measured, not assumed', () => {
    const borrowing = valve({ heatingRateCelsiusPerHour: 0.8, heatingRateSamples: 2, heatingRateFromEntityId: 'a', warmUpMinutes: 70 })
    expect(expectedWarmUp(borrowing)).toEqual({ minutes: 70, assumed: false })
  })

  it('describes a heater plug too, whose shortest warm-up is 5 minutes (since 2026-10-21)', () => {
    expect(describeWarmUpRate({ role: 'heaterPlug' })).toMatch(/not measured yet.*5 minutes/)
    expect(describeWarmUpRate({ role: 'heaterPlug', heatingRateCelsiusPerHour: 6, heatingRateSamples: 2 }))
      .toBe('Warms its room about 6.0°C an hour (the average of its last 2 heatings)')
  })

  it('gives a heater plug no assumed warm-up while unmeasured, and keeps heaters out of the radiators\' zone figure', () => {
    expect(expectedWarmUp({ role: 'heaterPlug', preHeatAuto: true })).toBeNull()
    expect(expectedWarmUp({ role: 'heaterPlug', warmUpMinutes: 12 })).toEqual({ minutes: 12, assumed: false })
    expect(zoneWarmUp([{ role: 'heaterPlug', warmUpMinutes: 90 }])).toBeNull()
  })
})

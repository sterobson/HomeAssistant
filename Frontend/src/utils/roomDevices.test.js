import { describe, expect, it } from 'vitest'
import {
  describeState,
  deviceName,
  deviceSections,
  distinctDeviceCount,
  groupDevices,
  isUnavailable,
  roleLabel,
  sortDevices
} from './roomDevices.js'

const celsius = (value) => `${value}°C`

describe('groupDevices', () => {
  it('splits a zone into readers, heaters and presence, in that order', () => {
    const groups = groupDevices([
      { role: 'tvPower', name: 'Den - Plug - TV Power' },
      { role: 'radiatorValve', name: 'Den - Radiator 2' },
      { role: 'thermometer', name: 'Den - Climate' },
      { role: 'heaterPlug', name: 'Heater' },
      { role: 'radiatorValve', name: 'Den - Radiator 10' }
    ])

    expect(groups.map(g => g.title)).toEqual(['Readers', 'Heaters', 'Presence'])
    expect(groups[1].devices.map(d => d.name)).toEqual(['Den - Radiator 2', 'Den - Radiator 10', 'Heater'])
  })

  it('leaves out empty groups', () => {
    expect(groupDevices([{ role: 'thermometer', name: 'Bathroom' }]).map(g => g.key)).toEqual(['readers'])
    expect(groupDevices([])).toEqual([])
  })

  it('keeps a device with an unknown role, in "Other"', () => {
    expect(groupDevices([{ role: 'somethingNew', name: 'X' }]).map(g => g.title)).toEqual(['Other'])
  })
})

describe('describeState', () => {
  it('formats a thermometer reading in the user unit', () => {
    expect(describeState({ role: 'thermometer', state: '20.8' }, celsius)).toBe('20.8°C')
  })

  it('shows a valve target and its own reading', () => {
    const valve = { role: 'radiatorValve', state: 'heat', targetTemperature: 21, currentTemperature: 19.5 }
    expect(describeState(valve, celsius)).toBe('Set to 21°C, reads 19.5°C')
  })

  it('says a valve is off rather than showing its target', () => {
    expect(describeState({ role: 'radiatorValve', state: 'off', targetTemperature: 7 }, celsius)).toBe('Off')
  })

  it('capitalises plug states', () => {
    expect(describeState({ role: 'heaterPlug', state: 'on' }, celsius)).toBe('On')
    expect(describeState({ role: 'deskPlug', state: 'off' }, celsius)).toBe('Off')
    expect(describeState({ role: 'lightsPlug', state: 'off' }, celsius)).toBe('Off')
  })

  it('adds the unit for other sensors', () => {
    expect(describeState({ role: 'deskPower', state: '45.2', unit: 'W' }, celsius)).toBe('45.2 W')
    expect(describeState({ role: 'tvPower', state: '4.0', unit: 'W' }, celsius)).toBe('4.0 W')
  })

  it('never shows an unreachable device as a reading', () => {
    expect(describeState({ role: 'thermometer', state: 'unavailable' }, celsius)).toBe('Unavailable')
    expect(describeState({ role: 'thermometer', state: 'unknown' }, celsius)).toBe('Unknown')
    expect(describeState({ role: 'thermometer', state: null }, celsius)).toBe('Not found in Home Assistant')
  })
})

describe('isUnavailable', () => {
  it('treats unavailable, unknown and missing as unavailable', () => {
    expect(isUnavailable({ state: 'unavailable' })).toBe(true)
    expect(isUnavailable({ state: 'unknown' })).toBe(true)
    expect(isUnavailable({ state: null })).toBe(true)
    expect(isUnavailable({ state: 'on' })).toBe(false)
  })
})

describe('labels', () => {
  it('names each role in sentence case', () => {
    expect(roleLabel('radiatorValve')).toBe('Radiator valve')
    expect(roleLabel('tvPower')).toBe('TV power')
    expect(roleLabel('lightsPlug')).toBe('Lights plug')
    expect(roleLabel('deskPlug')).toBe('Desk plug')
    expect(roleLabel('somethingNew')).toBe('Device')
  })

  it('falls back to the entity ID when there is no friendly name', () => {
    expect(deviceName({ entityId: 'sensor.x', name: null })).toBe('sensor.x')
    expect(deviceName({ entityId: 'sensor.x', name: 'Den - Climate' })).toBe('Den - Climate')
  })
})

describe('sortDevices', () => {
  it('orders by role, then naturally by name', () => {
    const devices = [
      { role: 'radiatorValve', name: 'Den - Radiator 10' },
      { role: 'thermometer', name: 'Den - Climate' },
      { role: 'radiatorValve', name: 'Den - Radiator 2' }
    ]
    expect(sortDevices(devices).map(d => d.name)).toEqual(['Den - Climate', 'Den - Radiator 2', 'Den - Radiator 10'])
    expect(devices[0].name).toBe('Den - Radiator 10')
  })
})

describe('groups', () => {
  const thermometer = { entityId: 'sensor.dining', role: 'thermometer' }
  const desk = { id: 1, name: 'dining desk', displayName: "Ruth's desk", devices: [thermometer, { entityId: 'switch.plug', role: 'heaterPlug' }] }
  const room = { id: 2, name: 'dining room', devices: [thermometer, { entityId: 'climate.trv', role: 'radiatorValve' }] }

  it('keeps one section per zone, labelled with its display name', () => {
    expect(deviceSections([desk, room]).map(s => s.title)).toEqual(["Ruth's desk", 'dining room'])
  })

  it('counts a shared thermometer once', () => {
    expect(distinctDeviceCount([desk, room])).toBe(3)
  })

  it('copes with rooms that have not reported yet', () => {
    expect(distinctDeviceCount([{ id: 3 }, undefined])).toBe(0)
    expect(deviceSections([{ id: 3, name: 'kitchen' }])[0].devices).toEqual([])
  })
})

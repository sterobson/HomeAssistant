import { describe, expect, it } from 'vitest'
import {
  describeState,
  deviceName,
  deviceSections,
  distinctDeviceCount,
  filterSection,
  groupDevices,
  houseDeviceSections,
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
    expect(describeState({ role: 'routerDevices', state: '21', unit: 'devices' }, celsius)).toBe('21 devices')
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

  it('never greys out a towel rail, which is not in Home Assistant', () => {
    expect(isUnavailable({ role: 'towelRail', state: null })).toBe(false)
  })
})

describe('towel rail', () => {
  it('is a heater', () => {
    expect(groupDevices([{ role: 'towelRail', name: 'Heated towel rail' }]).map(g => g.key)).toEqual(['heaters'])
  })

  it('follows the boiler when the app runs it, and says what it does otherwise', () => {
    expect(describeState({ role: 'towelRail', state: 'on' }, celsius)).toBe('Hot, the boiler is on')
    expect(describeState({ role: 'towelRail', state: 'off' }, celsius)).toBe('Cold, the boiler is off')
    expect(describeState({ role: 'towelRail', state: null }, celsius)).toBe('Hot whenever the boiler runs')
  })
})

describe('labels', () => {
  it('names each role in sentence case', () => {
    expect(roleLabel('radiatorValve')).toBe('Radiator valve')
    expect(roleLabel('tvPower')).toBe('TV power')
    expect(roleLabel('lightsPlug')).toBe('Lights plug')
    expect(roleLabel('deskPlug')).toBe('Desk plug')
    expect(roleLabel('routerDevices')).toBe('Router')
    expect(roleLabel('phone')).toBe('Phone')
    expect(describeState({ role: 'phone', state: 'home' }, celsius)).toBe('On the home Wi-Fi')
    expect(describeState({ role: 'phone', state: 'away' }, celsius)).toBe('Not on the home Wi-Fi')
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

describe('filterSection', () => {
  const section = {
    roomId: 1,
    devices: [
      { entityId: 'sensor.t', role: 'thermometer' },
      { entityId: 'climate.v', role: 'radiatorValve' },
      { entityId: 'switch.h', role: 'heaterPlug' },
      { entityId: 'switch.d', role: 'deskPlug' },
      { entityId: 'sensor.p', role: 'tvPower' },
      { entityId: 'phone.ellie', role: 'phone' }
    ],
    batteries: [{ entityId: 'sensor.v_battery', deviceEntityIds: ['climate.v'] }],
    occupancy: { detectorIds: [] }
  }
  const ids = (key) => filterSection(section, key)?.devices.map(d => d.entityId)

  it('keeps everything, with the zone-wide lines, for All', () => {
    const all = filterSection(section, 'all')
    expect(all.devices).toHaveLength(6)
    expect(all.batteries).toHaveLength(1)
    expect(all.showOccupancy).toBe(true)
  })

  it('keeps only what each filter names', () => {
    expect(ids('trv')).toEqual(['climate.v'])
    expect(ids('plug')).toEqual(['switch.h', 'switch.d', 'sensor.p'])
    expect(ids('heating')).toEqual(['climate.v', 'switch.h'])
    expect(ids('thermometer')).toEqual(['sensor.t'])
  })

  it('shows only the batteries for Battery, and no batteries otherwise', () => {
    const battery = filterSection(section, 'battery')
    expect(battery.devices).toEqual([])
    expect(battery.batteries).toHaveLength(1)
    expect(filterSection(section, 'trv').batteries).toEqual([])
    expect(filterSection(section, 'trv').showOccupancy).toBe(false)
  })

  it('hides a section the filter leaves empty, except under All', () => {
    expect(filterSection({ roomId: 2, devices: [{ entityId: 'sensor.t', role: 'thermometer' }] }, 'trv')).toBeNull()
    expect(filterSection({ roomId: 2, devices: [] }, 'all')).not.toBeNull()
  })
})

describe('houseDeviceSections', () => {
  const rooms = new Map([
    [1, { id: 1, name: 'kitchen', devices: [{ entityId: 'sensor.k', role: 'thermometer' }] }],
    [2, { id: 2, name: 'den', displayName: 'Den', devices: [] }]
  ])
  const tree = [
    { type: 'group', id: 'g1', name: 'Downstairs', icon: null, children: [{ type: 'room', roomId: 2 }] },
    { type: 'room', roomId: 1 }
  ]
  const placement = {
    byRoom: new Map([['2', [{ entityId: 'sensor.den_battery' }]]]),
    byGroup: new Map([['g1', [{ entityId: 'sensor.button_battery' }]]]),
    unplaced: [{ entityId: 'sensor.loft_battery' }]
  }

  it('lists every zone in layout order, with group and unplaced batteries in their own sections', () => {
    const sections = houseDeviceSections(tree, rooms, placement)
    expect(sections.map(s => s.title)).toEqual(['Downstairs', 'Den', 'kitchen', 'Not in any room or group'])
    expect(sections[1].batteries).toEqual([{ entityId: 'sensor.den_battery' }])
    expect(sections[2].devices.map(d => d.entityId)).toEqual(['sensor.k'])
  })

  it('copes with no battery placement yet', () => {
    expect(houseDeviceSections(tree, rooms, null).map(s => s.title)).toEqual(['Den', 'kitchen'])
  })
})

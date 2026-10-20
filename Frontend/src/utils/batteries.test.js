import { describe, it, expect } from 'vitest'
import {
  LOW_BATTERY_PERCENT, isLow, lowestLow, parsePlace, placeBatteries, batteriesForRoom, batteriesForGroup,
  placeTargets, batteriesByArea
} from './batteries.js'

const battery = (entityId, percent, area, deviceEntityIds = [entityId]) => ({ entityId, name: entityId, percent, area, deviceEntityIds })

// Two dining zones sharing a thermometer, a kitchen, and the den's radiator.
const rooms = [
  { id: 4, name: 'dining room', devices: [{ entityId: 'sensor.dining_temp' }, { entityId: 'climate.dining_trv' }] },
  { id: 9, name: 'dining desk', devices: [{ entityId: 'sensor.dining_temp' }, { entityId: 'switch.dining_heater' }] },
  { id: 5, name: 'kitchen', devices: [{ entityId: 'sensor.kitchen_temp' }] },
  { id: 10, name: 'den', devices: [{ entityId: 'climate.den_trv' }] }
]

// Downstairs > (Dining room > dining room, dining desk), kitchen; den at the top level.
const layout = [
  {
    type: 'group', key: 'group-down', id: 'down', name: 'Downstairs', children: [
      { type: 'group', key: 'group-dining', id: 'dining', name: 'Dining room', children: [
        { type: 'room', key: 'room-4', roomId: 4 }, { type: 'room', key: 'room-9', roomId: 9 }
      ] },
      { type: 'room', key: 'room-5', roomId: 5 }
    ]
  },
  { type: 'room', key: 'room-10', roomId: 10 }
]

describe('isLow and lowestLow', () => {
  it('counts the threshold itself as low, and an unavailable sensor as not low', () => {
    expect(LOW_BATTERY_PERCENT).toBe(20)
    expect(isLow({ percent: 20 })).toBe(true)
    expect(isLow({ percent: 20.5 })).toBe(false)
    expect(isLow({ percent: null })).toBe(false)
  })

  it('picks the lowest of the low ones, or null', () => {
    expect(lowestLow([{ percent: 90 }, { percent: 18 }, { percent: 7 }])).toEqual({ percent: 7 })
    expect(lowestLow([{ percent: 90 }, { percent: null }])).toBeNull()
    expect(lowestLow(undefined)).toBeNull()
  })
})

describe('parsePlace', () => {
  it('reads zone and group places and rejects anything else', () => {
    expect(parsePlace('room:5')).toEqual({ kind: 'room', id: '5' })
    expect(parsePlace('group:g-1')).toEqual({ kind: 'group', id: 'g-1' })
    expect(parsePlace('kitchen')).toBeNull()
    expect(parsePlace(undefined)).toBeNull()
  })
})

describe('placeBatteries', () => {
  it('puts a heating device on every zone that uses it, whatever its area says', () => {
    const thermometer = battery('sensor.dining_temp_battery', 50, 'Kitchen', ['sensor.dining_temp', 'sensor.dining_temp_battery'])
    const placement = placeBatteries([thermometer], rooms, layout, { Kitchen: 'room:5' })

    expect(batteriesForRoom(placement, 4)).toEqual([thermometer])
    expect(batteriesForRoom(placement, 9)).toEqual([thermometer])
    expect(batteriesForRoom(placement, 5)).toEqual([])
  })

  it('places other devices by their area, on a zone or a group', () => {
    const flood = battery('sensor.flood_battery', 80, 'Kitchen')
    const button = battery('sensor.desk_button_battery', 12, 'Dining Room')
    const placement = placeBatteries([flood, button], rooms, layout, { Kitchen: 'room:5', 'Dining Room': 'group:dining' })

    expect(batteriesForRoom(placement, 5)).toEqual([flood])
    expect(placement.byGroup.get('dining')).toEqual([button])
    expect(placement.unplaced).toEqual([])
  })

  it('leaves a device unplaced when it has no area, its area is not mapped, or the place has gone', () => {
    const fingerbot = battery('sensor.fingerbot_battery', 15, null)
    const porch = battery('sensor.porch_motion_battery', 60, 'Porch')
    const orphan = battery('sensor.old_battery', 60, 'Hallway')
    const placement = placeBatteries([fingerbot, porch, orphan], rooms, layout, { Hallway: 'group:deleted' })

    expect(placement.unplaced).toEqual([fingerbot, porch, orphan])
  })
})

describe('batteriesForGroup', () => {
  const thermometer = battery('sensor.dining_temp_battery', 10, 'Dining Room', ['sensor.dining_temp'])
  const button = battery('sensor.desk_button_battery', 12, 'Dining Room')
  const flood = battery('sensor.flood_battery', 80, 'Kitchen')
  const placement = placeBatteries([thermometer, button, flood], rooms, layout, { 'Dining Room': 'group:dining', Kitchen: 'room:5' })
  const downstairs = layout[0]
  const dining = downstairs.children[0]

  it('shows only what is placed on the group itself while it is open', () => {
    expect(batteriesForGroup(placement, dining, false)).toEqual([button])
    expect(batteriesForGroup(placement, downstairs, false)).toEqual([])
  })

  it('takes in everything inside, at any depth, while collapsed, counting a shared device once', () => {
    expect(batteriesForGroup(placement, dining, true).map(b => b.entityId)).toEqual(['sensor.desk_button_battery', 'sensor.dining_temp_battery'])
    expect(batteriesForGroup(placement, downstairs, true).map(b => b.entityId).sort())
      .toEqual(['sensor.desk_button_battery', 'sensor.dining_temp_battery', 'sensor.flood_battery'])
    expect(lowestLow(batteriesForGroup(placement, downstairs, true))).toBe(thermometer)
  })
})

describe('placeTargets', () => {
  it('lists groups and zones in layout order with their depth', () => {
    const roomsById = new Map(rooms.map(room => [room.id, room]))
    expect(placeTargets(layout, roomsById)).toEqual([
      { value: 'group:down', label: 'Downstairs', depth: 0 },
      { value: 'group:dining', label: 'Dining room', depth: 1 },
      { value: 'room:4', label: 'dining room', depth: 2 },
      { value: 'room:9', label: 'dining desk', depth: 2 },
      { value: 'room:5', label: 'kitchen', depth: 1 },
      { value: 'room:10', label: 'den', depth: 0 }
    ])
  })
})

describe('batteriesByArea', () => {
  it('groups by area in natural order with no area last, naming the zones that use each device', () => {
    const list = batteriesByArea([
      battery('sensor.fingerbot_battery', 90, null),
      battery('sensor.b_battery', 90, 'Bedroom 10'),
      battery('sensor.a_battery', 90, 'Bedroom 2'),
      battery('sensor.den_trv_battery', 90, 'Games Room', ['climate.den_trv'])
    ], rooms)

    expect(list.map(entry => entry.area)).toEqual(['Bedroom 2', 'Bedroom 10', 'Games Room', null])
    expect(list[2].batteries[0].zones.map(room => room.id)).toEqual([10])
  })
})

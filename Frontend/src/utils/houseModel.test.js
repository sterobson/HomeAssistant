import { describe, it, expect } from 'vitest'
import {
  ROOMS, UNPLACED, OPENINGS, FURNITURE, DEVICE_SPOTS, PLAN_UNIT_METRES, roomForArea, roomById, entityLabel, primaryEntity, deviceKind,
  deviceStatus, placeDevices, applyStateChanges, groupEntities, cutOpenings, deviceNamed, roomAt, arrangement, deviceKey,
  placeOpening, floorWalls, flipHinge, flipSwing, resizeOpening, resizeFurniture, rotateFurniture, duplicateItem, layoutJson,
  defaultLayout,
} from './houseModel.js'

const plug = {
  id: 'd1',
  name: 'Den - Plug - TV',
  model: 'S60ZBTPG',
  area: 'Games Room',
  entities: [
    { entityId: 'button.den_plug_tv_identify', name: 'Den - Plug - TV Identify', state: 'unknown', actions: ['press'] },
    { entityId: 'sensor.den_plug_tv_power', name: 'Den - Plug - TV Power', state: '35.2', unit: 'W', deviceClass: 'power', actions: [] },
    { entityId: 'switch.den_plug_tv_child_lock', name: 'Den - Plug - TV Child lock', state: 'off', actions: ['turn_on', 'turn_off', 'toggle'] },
    { entityId: 'switch.den_plug_tv_switch', name: 'Den - Plug - TV', state: 'on', actions: ['turn_on', 'turn_off', 'toggle'] },
  ],
}

const thermometer = {
  id: 'd2',
  name: 'Den - Climate',
  model: 'SNZB-02D',
  area: 'Games Room',
  entities: [
    { entityId: 'sensor.den_climate_battery', name: 'Den - Climate Battery', state: '100', unit: '%', deviceClass: 'battery' },
    { entityId: 'sensor.den_climate_humidity', name: 'Den', state: '55.24', unit: '%', deviceClass: 'humidity' },
    { entityId: 'sensor.den_climate_temperature', name: 'Den', state: '20.83', unit: '°C', deviceClass: 'temperature' },
  ],
}

describe('the plan', () => {
  it('puts the den on the left, running from the back wall to the front', () => {
    const den = ROOMS.find((room) => room.id === 'den')
    const kitchen = ROOMS.find((room) => room.id === 'kitchen')
    expect(den.x + den.w).toBe(kitchen.x)
    expect(den.y).toBe(0)
    expect(den.y + den.h).toBe(70)
  })

  it('makes the den 9 m long and 3.6 m across, as measured', () => {
    const den = ROOMS.find((room) => room.id === 'den')
    expect(den.h * PLAN_UNIT_METRES).toBeCloseTo(9, 5)
    expect(den.w * PLAN_UNIT_METRES).toBeCloseTo(3.6, 5)
  })

  it('maps Home Assistant areas to rooms whatever their case', () => {
    expect(roomForArea('Games Room').id).toBe('den')
    expect(roomForArea('bedroom 1').id).toBe('bed1')
    expect(roomForArea('  Under the  stairs ').id).toBe('understairs')
  })

  it('puts a device with no area, or an unknown one, beside the house', () => {
    expect(roomForArea(null)).toBe(UNPLACED)
    expect(roomForArea('Garage')).toBe(UNPLACED)
  })
})

describe('describing a device', () => {
  it('strips the device name from its entities', () => {
    expect(entityLabel(plug.entities[1], plug)).toBe('Power')
    expect(entityLabel(plug.entities[3], plug)).toBe('')
    expect(entityLabel({ name: 'Den', entityId: 'sensor.x' }, thermometer)).toBe('Den')
  })

  it("takes a plug's own switch as its main entity, not its child lock", () => {
    expect(primaryEntity(plug).entityId).toBe('switch.den_plug_tv_switch')
    expect(primaryEntity(thermometer).entityId).toBe('sensor.den_climate_temperature')
  })

  it('tells the kinds apart', () => {
    expect(deviceKind(plug)).toBe('plug')
    expect(deviceKind(thermometer)).toBe('thermometer')
    expect(deviceKind({ name: 'Den - Radiator 1', model: 'TRVZB', entities: [{ entityId: 'climate.den_radiator_1_thermostat' }] })).toBe('radiator')
    expect(deviceKind({ name: 'Boiler-Fingerbot', model: 'TS0001', entities: [{ entityId: 'switch.tz3210_j4pdtz9v_ts0001' }] })).toBe('fingerbot')
  })

  it('shows a plug as on with its power, and a thermometer with its humidity', () => {
    expect(deviceStatus(plug)).toEqual({ text: 'On · 35.2 W', tone: 'on' })
    expect(deviceStatus(thermometer)).toEqual({ text: '20.8 °C · 55.2 %', tone: 'reading' })
  })

  it('flags an unavailable device', () => {
    const gone = { ...plug, entities: plug.entities.map((e) => ({ ...e, state: 'unavailable' })) }
    expect(deviceStatus(gone)).toEqual({ text: 'Unavailable', tone: 'alert' })
  })

  it("lists a device's own switch first among its controls", () => {
    const { controls, readings } = groupEntities(plug)
    expect(controls[0].entityId).toBe('switch.den_plug_tv_switch')
    expect(readings.map((e) => e.entityId)).toEqual(['sensor.den_plug_tv_power'])
  })
})

describe('placing devices', () => {
  it('puts every device inside its room, alphabetically, without two on one spot', () => {
    const spare = { ...plug, name: 'Den - Plug - Spare' } // no fixed spot, so it goes on the grid
    const placed = placeDevices([spare, thermometer, { ...thermometer, id: 'd3', name: 'Another', area: null }])
    for (const { room, x, y } of placed) {
      expect(x).toBeGreaterThan(room.x)
      expect(x).toBeLessThan(room.x + room.w)
      expect(y).toBeGreaterThan(room.y)
      expect(y).toBeLessThan(room.y + room.h)
    }
    const den = placed.filter((p) => p.room.id === 'den')
    expect(den.map((p) => p.device.name)).toEqual(['Den - Climate', 'Den - Plug - Spare'])
    expect(new Set(den.map((p) => `${p.x},${p.y}`)).size).toBe(2)
    expect(placed.find((p) => p.device.id === 'd3').room).toBe(UNPLACED)
  })
})

describe('fittings and known spots', () => {
  const inside = (r, x, y) => x >= r.x && x <= r.x + r.w && y >= r.y && y <= r.y + r.h

  it('puts a device with a known spot there, whatever its area says', () => {
    const fingerbot = { id: 'f', name: 'Boiler-Fingerbot', area: null, entities: [{ entityId: 'switch.tz3210_j4pdtz9v_ts0001', state: 'off' }] }
    const [spot] = placeDevices([fingerbot])
    expect(spot.room.id).toBe('boiler')
    expect(inside(spot.room, spot.x, spot.y)).toBe(true)
  })

  it('keeps every known spot inside its room', () => {
    for (const [name, spot] of Object.entries(DEVICE_SPOTS)) {
      const room = roomById(spot.room)
      expect(room, name).not.toBeNull()
      expect(inside(room, spot.x, spot.y), name).toBe(true)
    }
  })

  it('puts every door and window on a wall of a room on its floor', () => {
    for (const o of OPENINGS) {
      const onWall = ROOMS.filter((r) => r.floor === o.floor && !r.nested).some((r) => (o.axis === 'h'
        ? (r.y === o.at || r.y + r.h === o.at) && o.from >= r.x && o.to <= r.x + r.w
        : (r.x === o.at || r.x + r.w === o.at) && o.from >= r.y && o.to <= r.y + r.h))
      expect(onWall, JSON.stringify(o)).toBe(true)
    }
  })

  it('keeps all the furniture inside the house', () => {
    for (const item of FURNITURE) {
      // A turned item's footprint is its unturned box, so only its middle can be checked.
      const fits = item.rotation
        ? ROOMS.some((r) => r.floor === item.floor && inside(r, item.x + item.w / 2, item.y + item.h / 2))
        : ROOMS.some((r) => r.floor === item.floor && inside(r, item.x, item.y) && inside(r, item.x + item.w, item.y + item.h))
      expect(fits, JSON.stringify(item)).toBe(true)
    }
  })

  it('puts every radiator valve at the end of a radiator', () => {
    const radiators = FURNITURE.filter((f) => f.type === 'radiator')
    for (const key of ['den - radiator 1', 'den - radiator 2', 'dining room - radiator', 'living room - radiator']) {
      const { x, y } = DEVICE_SPOTS[key]
      const near = radiators.some((r) => x >= r.x - 0.6 && x <= r.x + r.w + 0.6 && y >= r.y - 0.6 && y <= r.y + r.h + 0.6)
      expect(near, key).toBe(true)
    }
  })

  it('cuts doors and windows out of a wall, leaving the pieces either side', () => {
    const { pieces, cuts } = cutOpenings({ axis: 'h', at: 0, start: -4, end: 24 }, OPENINGS)
    expect(cuts.map((c) => c.type)).toEqual(['window', 'door'])
    expect(pieces.map((p) => [p.start, p.end])).toEqual([[-4, -2], [10.5, 11.5], [17.5, 24]])
  })

  it('leaves the open-plan half of the living room wall open', () => {
    const { pieces } = cutOpenings({ axis: 'h', at: 34, start: 40, end: 72 }, OPENINGS.filter((o) => o.type === 'open'))
    expect(pieces.map((p) => [p.start, p.end])).toEqual([[40, 56]])
  })

  it('finds the device a lit fitting follows by name, ignoring an empty duplicate', () => {
    const empty = { id: 'a', name: 'LivingRoomLightTreePlug', entities: [] }
    const real = { id: 'b', name: 'LivingRoomLightTreePlug', entities: [{ entityId: 'switch.livingroomlighttreeplug', state: 'on' }] }
    expect(deviceNamed([empty, real], 'livingroomlighttreeplug')).toBe(real)
  })
})

describe('arranging devices', () => {
  it('finds the room a dropped device is in, the inner room first', () => {
    expect(roomAt('ground', 20, 3).id).toBe('boiler')
    expect(roomAt('ground', 26, 38).id).toBe('understairs')
    expect(roomAt('ground', 5, 30).id).toBe('den')
    expect(roomAt('first', 30, 60).id).toBe('bed3')
    expect(roomAt('ground', 85, 30).id).toBe('unplaced')
    expect(roomAt('ground', 50, 100)).toBeNull()
  })

  it('lets an arrangement made in the page win over the built-in spot', () => {
    const tv = { id: 't', name: 'Den - Plug - TV', area: 'Games Room', entities: [] }
    const [placed] = placeDevices([tv], { 'den - plug - tv': { room: 'den', x: 10, y: 20, height: 0.3, rotation: 45 } })
    expect([placed.x, placed.y, placed.rotation]).toEqual([10, 20, 45])
  })

  it('prints an arrangement that reproduces every position when fed back in', () => {
    const devices = [plug, thermometer, { id: 'h1', name: 'Charger', area: null, entities: [] }, { id: 'h2', name: 'Charger', area: null, entities: [] }]
    const printed = arrangement(devices, { 'den - climate': { room: 'den', x: 3.33, y: 30, height: 1.4, rotation: 405 } })
    expect(printed['den - climate']).toEqual({ room: 'den', x: 3.3, y: 30, height: 1.4, rotation: 45 })
    expect(Object.keys(printed)).toEqual(expect.arrayContaining(['id:h1', 'id:h2']))
    const again = Object.fromEntries(placeDevices(devices, printed).map((p) => [p.device.id, [p.room.id, p.x, p.y]]))
    for (const p of placeDevices(devices, printed)) {
      expect(again[p.device.id]).toEqual([printed[deviceKey(p.device, devices)].room, printed[deviceKey(p.device, devices)].x, printed[deviceKey(p.device, devices)].y])
    }
  })
})

describe('editing the layout', () => {
  const door = OPENINGS.find((o) => o.type === 'door' && o.axis === 'v' && o.at === 24)

  it('gives every piece of furniture and every opening its own id', () => {
    const ids = [...FURNITURE, ...OPENINGS].map((i) => i.id)
    expect(new Set(ids).size).toBe(ids.length)
    expect(ids.every(Boolean)).toBe(true)
  })

  it('slides a door along its wall, and onto the nearest other wall when dragged there', () => {
    const along = placeOpening(door, 24.3, 10)
    expect([along.axis, along.at, along.to - along.from]).toEqual(['v', 24, door.to - door.from])
    expect((along.from + along.to) / 2).toBeCloseTo(10, 5)
    const across = placeOpening(door, 10, 0.4)
    expect([across.axis, across.at]).toEqual(['h', 0])
  })

  it('keeps a moved door within its wall', () => {
    const moved = placeOpening(door, 24, -50)
    const wall = floorWalls('ground').find((w) => w.axis === 'v' && w.at === 24)
    expect(moved.from).toBeGreaterThanOrEqual(wall.start)
  })

  it('flips a door\'s hinge and swing, and resizes it about its middle', () => {
    expect(flipHinge(door).hinge).toBe('end')
    expect(flipHinge(flipHinge(door)).hinge).toBe('start')
    expect(flipSwing(door).swing).toBe(-(door.swing ?? 1))
    const wider = resizeOpening(door, 2)
    expect(wider.to - wider.from).toBeCloseTo(door.to - door.from + 2, 5)
    expect((wider.from + wider.to) / 2).toBeCloseTo((door.from + door.to) / 2, 5)
  })

  it('makes a radiator longer along its length only, about its middle', () => {
    const radiator = FURNITURE.find((f) => f.type === 'radiator' && f.w > f.h)
    const longer = resizeFurniture(radiator, 1, 0)
    expect(longer.w).toBeCloseTo(radiator.w + 1, 5)
    expect(longer.h).toBe(radiator.h)
    expect(longer.x + longer.w / 2).toBeCloseTo(radiator.x + radiator.w / 2, 5)
  })

  it('turns furniture in whole turns of 360', () => {
    expect(rotateFurniture({ rotation: 315 }, 45).rotation).toBe(0)
    expect(rotateFurniture({}, -45).rotation).toBe(315)
  })

  it('duplicates with a fresh id', () => {
    const chair = FURNITURE.find((f) => f.type === 'chair')
    const copy = duplicateItem(FURNITURE, chair)
    expect(FURNITURE.some((f) => f.id === copy.id)).toBe(false)
    const doorCopy = duplicateItem(OPENINGS, door)
    expect(OPENINGS.some((o) => o.id === doorCopy.id)).toBe(false)
  })

  it('prints devices, furniture and openings together', () => {
    const json = layoutJson([plug], null, defaultLayout())
    expect(Object.keys(json)).toEqual(['devices', 'furniture', 'openings'])
    expect(json.furniture).toHaveLength(FURNITURE.length)
  })
})

describe('live updates', () => {
  it('replaces only the devices whose entities changed', () => {
    const devices = [plug, thermometer]
    const updated = applyStateChanges(devices, [{ entityId: 'switch.den_plug_tv_switch', state: 'off' }])
    expect(updated[1]).toBe(thermometer)
    expect(updated[0]).not.toBe(plug)
    expect(primaryEntity(updated[0]).state).toBe('off')
    expect(primaryEntity(plug).state).toBe('on')
  })
})

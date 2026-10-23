import { describe, it, expect } from 'vitest'
import { HOUSEHOLD_RULES, whereIs, placeHousehold, detectorStates, tvOn, plugOn } from './household.js'

const plug = (name, on, watts = 0) => ({
  id: name,
  name,
  entities: [
    { entityId: `switch.${name.replace(/\W+/g, '_').toLowerCase()}`, name, state: on ? 'on' : 'off' },
    { entityId: `sensor.${name.replace(/\W+/g, '_').toLowerCase()}_power`, name: `${name} Power`, state: String(watts), deviceClass: 'power' },
  ],
})

const atHome = new Map([['ruth-phone-home', true], ['ste-phone-home', true]])
const noon = new Date(2026, 9, 22, 12, 0)
const ruth = HOUSEHOLD_RULES['person-ruth']
const ste = HOUSEHOLD_RULES['person-ste']

describe('where the household is', () => {
  it('hides someone whose phone is away, or not known yet', () => {
    expect(whereIs(ruth, { devices: [], detectors: new Map([['ruth-phone-home', false]]), now: noon })).toBe('hidden')
    expect(whereIs(ruth, { devices: [], detectors: new Map(), now: noon })).toBe('hidden')
  })

  it('puts each at their own desk when its plug is switched on, before anything else', () => {
    const devices = [plug('Dining Room - Desk - Plug', true), plug('Den - Plug - TV', true, 80)]
    expect(whereIs(ruth, { devices, detectors: atHome, now: noon })).toBe('desk')
    expect(whereIs(ste, { devices, detectors: atHome, now: noon })).toBe('den')
  })

  it('sits them in front of a TV that is drawing 30 W or more, the den first', () => {
    expect(tvOn([plug('Den - Plug - TV', true, 29)], 'Den - Plug - TV')).toBe(false)
    const devices = [plug('Den - Plug - TV', true, 4), plug('Living Room - Plug - TV', true, 64)]
    expect(whereIs(ste, { devices, detectors: atHome, now: noon })).toBe('living')
  })

  it('otherwise the kitchen from 07:00 until 21:00, Bedroom 1 outside those hours', () => {
    const context = (hour, minute = 0) => ({ devices: [], detectors: atHome, now: new Date(2026, 9, 22, hour, minute) })
    expect(whereIs(ruth, context(6, 59))).toBe('bedroom')
    expect(whereIs(ruth, context(7))).toBe('kitchen')
    expect(whereIs(ruth, context(20, 59))).toBe('kitchen')
    expect(whereIs(ruth, context(21))).toBe('bedroom')
  })

  it('follows Ellie by her own phone, with no desk, to Bedroom 3 at night', () => {
    const ellie = HOUSEHOLD_RULES['person-ellie']
    const home = new Map([['ellie-phone-home', true]])
    expect(whereIs(ellie, { devices: [], detectors: new Map([['ruth-phone-home', true]]), now: noon })).toBe('hidden')
    // After her weekday hours at the table, Ruth's desk being on doesn't bring Ellie to it.
    expect(whereIs(ellie, { devices: [plug('Dining Room - Desk - Plug', true)], detectors: home, now: new Date(2026, 9, 22, 16, 0) })).toBe('kitchen')
    expect(whereIs(ellie, { devices: [], detectors: home, now: new Date(2026, 9, 22, 23, 0) })).toBe('bedroom')
    expect(ellie.bedroom.floor).toBe('first')
  })

  it('puts Ellie at the dining table on weekdays from 08:00 to 15:00, even with a TV on, if she is home', () => {
    const ellie = HOUSEHOLD_RULES['person-ellie']
    const home = new Map([['ellie-phone-home', true]])
    const tv = [plug('Den - Plug - TV', true, 80)]
    // 22 October 2026 is a Thursday; the 24th a Saturday.
    const at = (day, hour, minute = 0) => ({ devices: tv, detectors: home, now: new Date(2026, 9, day, hour, minute) })
    expect(whereIs(ellie, at(22, 8))).toBe('desk')
    expect(whereIs(ellie, at(22, 14, 59))).toBe('desk')
    expect(whereIs(ellie, at(22, 7, 59))).toBe('den')
    expect(whereIs(ellie, at(22, 15))).toBe('den')
    expect(whereIs(ellie, at(24, 10))).toBe('den')
    expect(whereIs(ellie, { ...at(22, 10), detectors: new Map() })).toBe('hidden')
  })

  it('reads phone presence from the room states the house app publishes', () => {
    const states = detectorStates([{ occupancy: { available: [{ id: 'ruth-phone-home', active: true }, { id: 'ste-phone-home', active: false }] } }])
    expect([states.get('ruth-phone-home'), states.get('ste-phone-home')]).toEqual([true, false])
  })

  it('moves Ruth and Ste, sat or standing, leaves everyone else, and drops whoever is away', () => {
    const furniture = [
      { id: 'person-ruth', floor: 'ground', x: 0, y: 0, w: 3.9, h: 3.9, facing: 'left', person: { name: 'Ruth', pose: 'sit' } },
      { id: 'person-ste', floor: 'ground', x: 0, y: 0, w: 3.9, h: 3.9, facing: 'back', rotation: 45, person: { name: 'Ste', pose: 'sit' } },
      { id: 'person-ellie', floor: 'ground', x: 1, y: 1, w: 3.9, h: 3.9, person: { name: 'Ellie' } },
    ]
    const joseph = { id: 'person-joseph', floor: 'first', x: 1, y: 1, w: 3.9, h: 3.9, person: { name: 'Joseph' } }
    const placed = placeHousehold([...furniture, joseph], { devices: [], detectors: new Map([['ruth-phone-home', true]]), now: new Date(2026, 9, 22, 23, 0) })
    expect(placed.map((p) => p.id)).toEqual(['person-ruth', 'person-joseph'])
    expect(placed[0]).toMatchObject({ floor: 'first', facing: 'front', rotation: 0, person: { name: 'Ruth', pose: 'stand' } })
    expect(placed[1]).toBe(joseph)
    expect(plugOn([plug('Dining Room - Desk - Plug', true)], 'dining room - desk - plug')).toBe(true)
  })
})

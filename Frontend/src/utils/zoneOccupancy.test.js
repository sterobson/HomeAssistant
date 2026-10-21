import { describe, expect, it } from 'vitest'
import { effectiveDetectorIds, occupancyFirstSaveMap, roomConditionMet, summariseOccupancy, zoneDetectors } from './zoneOccupancy.js'

const available = [
  { id: 'den-tv-on', label: 'Den TV on' },
  { id: 'living-room-tv-on', label: 'Living room TV on' },
  { id: 'living-room-lights-on', label: 'Living room stick lights on' }
]
const zone = (name, detectorIds) => ({ name, occupancy: { detectorIds, isDefault: true, available } })

describe('effectiveDetectorIds', () => {
  it('uses what the app reports while nothing has been saved', () => {
    expect(effectiveDetectorIds(zone('den', ['den-tv-on']), null)).toEqual(['den-tv-on'])
    expect(effectiveDetectorIds(zone('den', ['den-tv-on']), undefined)).toEqual(['den-tv-on'])
  })

  it('uses the saved choice once there is one, matching the zone name loosely', () => {
    expect(effectiveDetectorIds(zone('Living room', ['den-tv-on']), { 'living room': ['living-room-tv-on'] }))
      .toEqual(['living-room-tv-on'])
  })

  it('gives a zone the saved choice leaves out no presence', () => {
    expect(effectiveDetectorIds(zone('den', ['den-tv-on']), { kitchen: ['den-tv-on'] })).toEqual([])
  })

  it('copes with an app too old to report occupancy', () => {
    expect(effectiveDetectorIds({ name: 'den' }, null)).toEqual([])
  })
})

describe('occupancyFirstSaveMap', () => {
  it('keeps every zone as it is and applies the edit', () => {
    const zones = [zone('den', ['den-tv-on']), zone('Living room', ['living-room-tv-on', 'living-room-lights-on']), zone('kitchen', [])]

    expect(occupancyFirstSaveMap(zones, 'kitchen', ['living-room-lights-on'])).toEqual({
      den: ['den-tv-on'],
      'Living room': ['living-room-tv-on', 'living-room-lights-on'],
      kitchen: ['living-room-lights-on']
    })
  })

  it('lets the edit take detectors away from a zone', () => {
    expect(occupancyFirstSaveMap([zone('den', ['den-tv-on'])], 'den', [])).toEqual({ den: [] })
  })
})

describe('summariseOccupancy', () => {
  it('lists the detectors by label', () => {
    expect(summariseOccupancy(['living-room-tv-on', 'living-room-lights-on'], available))
      .toBe('In use when: Living room TV on or Living room stick lights on')
  })

  it('says when a zone has none', () => {
    expect(summariseOccupancy([], available)).toBe('No presence: never counts as in use')
  })
})

describe('zoneDetectors', () => {
  const live = [
    { id: 'den-tv-on', label: 'Den TV on', meansSomeoneHome: false, active: true },
    { id: 'ellie-phone-home', label: "Ellie's phone on the home Wi-Fi", meansSomeoneHome: false, active: false }
  ]
  const liveZone = (name, detectorIds) => ({ name, occupancy: { detectorIds, isDefault: true, available: live } })

  it("lists the zone's detectors with whether each is on now", () => {
    expect(zoneDetectors(liveZone('den', ['den-tv-on', 'ellie-phone-home']), null)).toEqual([
      { id: 'den-tv-on', label: 'Den TV on', active: true },
      { id: 'ellie-phone-home', label: "Ellie's phone on the home Wi-Fi", active: false }
    ])
  })

  it('follows the saved choice over what the app last reported', () => {
    expect(zoneDetectors(liveZone('den', ['den-tv-on']), { den: ['ellie-phone-home'] }))
      .toEqual([{ id: 'ellie-phone-home', label: "Ellie's phone on the home Wi-Fi", active: false }])
  })

  it('keeps a detector the app no longer offers, as off', () => {
    expect(zoneDetectors(liveZone('den', ['gone']), null)).toEqual([{ id: 'gone', label: 'gone', active: false }])
  })
})

describe('roomConditionMet', () => {
  const on = [{ active: true }, { active: false }]
  const off = [{ active: false }]

  it('holds an "in use" track to any detector being on', () => {
    expect(roomConditionMet(4, on)).toBe(true)
    expect(roomConditionMet(4, off)).toBe(false)
  })

  it('holds a "not in use" track to none being on', () => {
    expect(roomConditionMet(8, off)).toBe(true)
    expect(roomConditionMet(8, on)).toBe(false)
  })

  it('never counts a zone with no detectors as in use', () => {
    expect(roomConditionMet(4, [])).toBe(false)
    expect(roomConditionMet(8, [])).toBe(true)
  })

  it('has nothing to say about a track with no room condition, whatever its Home/Away flags', () => {
    expect(roomConditionMet(0, on)).toBeNull()
    expect(roomConditionMet(1, off)).toBeNull()
    expect(roomConditionMet(5, off)).toBe(false)
  })
})

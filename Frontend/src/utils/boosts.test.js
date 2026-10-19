import { describe, it, expect } from 'vitest'
import { fromLayoutDto, findNode, groupKey } from './roomLayout.js'
import { isBoostActive, groupsAround, groupBoostActive, roomLockedBy, groupLockedBy, groupBoost } from './boosts.js'

const NOW = Date.parse('2026-10-19T12:00:00Z')
const LATER = '2026-10-19T14:00:00Z'
const EARLIER = '2026-10-19T11:00:00Z'

// Downstairs { Dining room { dining desk (9), dining room (4) }, kitchen (1) }, bedroom (3) at the top level.
const rooms = [{ id: 1 }, { id: 3 }, { id: 4 }, { id: 9 }]
const layout = [
  {
    id: 'down',
    name: 'Downstairs',
    children: [
      { id: 'dining', name: 'Dining room', children: [{ roomId: 9 }, { roomId: 4 }] },
      { roomId: 1 }
    ]
  },
  { roomId: 3 }
]
const tree = fromLayoutDto(layout, rooms)
const downstairs = findNode(tree, groupKey('down'))
const dining = findNode(tree, groupKey('dining'))

const withBoosts = (boosts) => new Map(rooms.map((r) => [r.id, { id: r.id, boost: boosts[r.id] ?? null }]))
const boost = (groupId, endTime = LATER) => ({ startTime: EARLIER, endTime, temperature: 20.5, groupId })

describe('isBoostActive', () => {
  it('is running until its end', () => {
    expect(isBoostActive(boost(undefined), NOW)).toBe(true)
    expect(isBoostActive(boost(undefined, EARLIER), NOW)).toBe(false)
    expect(isBoostActive(null, NOW)).toBe(false)
  })
})

describe('groupsAround', () => {
  it('lists the groups a node sits in, outermost first', () => {
    expect(groupsAround(tree, 'room-9').map((g) => g.id)).toEqual(['down', 'dining'])
    expect(groupsAround(tree, groupKey('dining')).map((g) => g.id)).toEqual(['down'])
    expect(groupsAround(tree, 'room-3')).toEqual([])
  })
})

describe('a group boost locks everything inside it', () => {
  const roomsById = withBoosts({ 1: boost('down'), 4: boost('down'), 9: boost('down') })

  it('runs for the group that set it', () => {
    expect(groupBoostActive(downstairs, roomsById, NOW)).toBe(true)
    expect(groupBoostActive(dining, roomsById, NOW)).toBe(false)
  })

  it('locks each zone inside, however deep', () => {
    expect(roomLockedBy(tree, roomsById.get(9), NOW)?.id).toBe('down')
    expect(roomLockedBy(tree, roomsById.get(1), NOW)?.id).toBe('down')
  })

  it('locks the groups inside, but not the group itself', () => {
    expect(groupLockedBy(tree, dining, roomsById, NOW)?.id).toBe('down')
    expect(groupLockedBy(tree, downstairs, roomsById, NOW)).toBeNull()
  })

  it('locks nothing outside the group', () => {
    expect(roomLockedBy(tree, roomsById.get(3), NOW)).toBeNull()
  })

  it('locks nothing once it has ended', () => {
    const ended = withBoosts({ 1: boost('down', EARLIER), 4: boost('down', EARLIER), 9: boost('down', EARLIER) })
    expect(roomLockedBy(tree, ended.get(9), NOW)).toBeNull()
    expect(groupLockedBy(tree, dining, ended, NOW)).toBeNull()
  })
})

describe('boosts that do not lock', () => {
  it('a zone boost leaves the zone in control', () => {
    const roomsById = withBoosts({ 4: boost(undefined) })
    expect(roomLockedBy(tree, roomsById.get(4), NOW)).toBeNull()
    expect(groupLockedBy(tree, dining, roomsById, NOW)).toBeNull()
  })

  it('an inner group boost does not lock the group around it, which can supersede it', () => {
    const roomsById = withBoosts({ 4: boost('dining'), 9: boost('dining') })
    expect(roomLockedBy(tree, roomsById.get(4), NOW)?.id).toBe('dining')
    expect(groupLockedBy(tree, downstairs, roomsById, NOW)).toBeNull()
    expect(groupLockedBy(tree, dining, roomsById, NOW)).toBeNull()
  })

  it('a tag naming a group the zone is no longer in locks nothing', () => {
    const roomsById = withBoosts({ 3: boost('down'), 4: boost('deleted-group') })
    expect(roomLockedBy(tree, roomsById.get(3), NOW)).toBeNull()
    expect(roomLockedBy(tree, roomsById.get(4), NOW)).toBeNull()
  })
})

describe('groupBoost', () => {
  it('is a zone boost tagged with the group', () => {
    expect(groupBoost(dining, new Date(EARLIER), new Date(LATER), 21)).toEqual({
      startTime: new Date(EARLIER).toISOString(),
      endTime: new Date(LATER).toISOString(),
      temperature: 21,
      groupId: 'dining'
    })
  })
})

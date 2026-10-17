import { describe, it, expect } from 'vitest'
import {
  fromLayoutDto, toLayoutDto, findNode, parentOf, roomIdsIn, orderedRoomIds, groupIds, newGroupId,
  canMove, move, moveTargets, moveTo, addGroup, updateGroup, deleteGroup,
  temperatureSummary, anyHeating, normaliseIcon, roomKey, groupKey
} from './roomLayout.js'

const rooms = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10].map((id) => ({ id, name: `room ${id}` }))

// Downstairs[ room 1, Dining[ room 4, room 9 ], room 5 ], Upstairs[ Bedrooms[ Kids[ room 6, room 7 ], room 3 ], room 8 ], room 2, room 10
const saved = () => [
  { id: 'down', name: 'Downstairs', icon: '⬇️', children: [
    { roomId: 1 },
    { id: 'dining', name: 'Dining room', icon: '🍽️', children: [{ roomId: 4 }, { roomId: 9 }] },
    { roomId: 5 }
  ] },
  { id: 'up', name: 'Upstairs', children: [
    { id: 'beds', name: 'Bedrooms', icon: '🛏️', children: [
      { id: 'kids', name: 'Kids', children: [{ roomId: 6 }, { roomId: 7 }] },
      { roomId: 3 }
    ] },
    { roomId: 8 }
  ] },
  { roomId: 2 },
  { roomId: 10 }
]
const tree = () => fromLayoutDto(saved(), rooms)
const shape = (nodes) => nodes.map((n) => (n.type === 'room' ? n.roomId : { [n.id]: shape(n.children) }))

describe('fromLayoutDto / toLayoutDto', () => {
  it('builds the nested tree in the saved order', () => {
    expect(shape(tree())).toEqual([
      { down: [1, { dining: [4, 9] }, 5] },
      { up: [{ beds: [{ kids: [6, 7] }, 3] }, 8] },
      2,
      10
    ])
  })

  it('round-trips through the saved form, including icons and any depth', () => {
    const again = fromLayoutDto(toLayoutDto(tree()), rooms)
    expect(shape(again)).toEqual(shape(tree()))
    expect(findNode(again, groupKey('beds')).icon).toBe('🛏️')
    expect(findNode(again, groupKey('kids')).icon).toBeNull()
  })

  it('with no saved layout, simply lists the rooms in order', () => {
    expect(shape(fromLayoutDto([], rooms))).toEqual([1, 2, 3, 4, 5, 6, 7, 8, 9, 10])
    expect(shape(fromLayoutDto(undefined, rooms))).toEqual([1, 2, 3, 4, 5, 6, 7, 8, 9, 10])
  })

  it('appends a room the layout never mentions to the top level', () => {
    const partial = [{ id: 'g', name: 'G', children: [{ roomId: 1 }] }]
    expect(shape(fromLayoutDto(partial, rooms.slice(0, 3)))).toEqual([{ g: [1] }, 2, 3])
  })

  it('skips a node naming a room that no longer exists', () => {
    expect(shape(fromLayoutDto([{ roomId: 99 }, { roomId: 1 }], rooms.slice(0, 2)))).toEqual([1, 2])
  })

  it('shows a room named twice only once, where it first appears', () => {
    const dup = [{ id: 'a', name: 'A', children: [{ roomId: 1 }] }, { roomId: 1 }, { roomId: 2 }]
    expect(shape(fromLayoutDto(dup, rooms.slice(0, 2)))).toEqual([{ a: [1] }, 2])
  })

  it('gives a group with a missing or repeated id a fresh one, so keys stay unique', () => {
    const messy = [{ name: 'No id', children: [] }, { id: 'x', name: 'One', children: [] }, { id: 'x', name: 'Two', children: [] }]
    const ids = groupIds(fromLayoutDto(messy, []))
    expect(new Set(ids).size).toBe(3)
  })

  it('tolerates a group with no children list', () => {
    expect(shape(fromLayoutDto([{ id: 'g', name: 'G' }], []))).toEqual([{ g: [] }])
  })
})

describe('finding', () => {
  it('finds the parent of a node at any depth, and null at the top', () => {
    expect(parentOf(tree(), roomKey(6)).id).toBe('kids')
    expect(parentOf(tree(), groupKey('kids')).id).toBe('beds')
    expect(parentOf(tree(), groupKey('down'))).toBeNull()
  })

  it('lists rooms in display order across all groups', () => {
    expect(orderedRoomIds(tree())).toEqual([1, 4, 9, 5, 6, 7, 3, 8, 2, 10])
    expect(roomIdsIn(findNode(tree(), groupKey('beds')))).toEqual([6, 7, 3])
  })
})

describe('move among siblings', () => {
  it('moves a room within its group and nowhere else', () => {
    expect(shape(move(tree(), roomKey(5), -1))).toEqual([
      { down: [1, 5, { dining: [4, 9] }] }, { up: [{ beds: [{ kids: [6, 7] }, 3] }, 8] }, 2, 10
    ])
  })

  it('moves a whole group, with everything inside it, among its siblings', () => {
    expect(shape(move(tree(), groupKey('kids'), +1))).toEqual([
      { down: [1, { dining: [4, 9] }, 5] }, { up: [{ beds: [3, { kids: [6, 7] }] }, 8] }, 2, 10
    ])
  })

  it('moves a top-level group past a top-level room', () => {
    expect(shape(move(tree(), groupKey('up'), +1))).toEqual([
      { down: [1, { dining: [4, 9] }, 5] }, 2, { up: [{ beds: [{ kids: [6, 7] }, 3] }, 8] }, 10
    ])
  })

  it('does nothing past either end of a sibling list', () => {
    expect(canMove(tree(), roomKey(1), -1)).toBe(false)
    expect(canMove(tree(), roomKey(10), +1)).toBe(false)
    expect(canMove(tree(), roomKey(9), +1)).toBe(false)
    expect(canMove(tree(), roomKey(4), -1)).toBe(false)
    expect(shape(move(tree(), roomKey(1), -1))).toEqual(shape(tree()))
  })

  it('never mutates its input', () => {
    const t = tree()
    const before = JSON.stringify(t)
    move(t, groupKey('up'), +1)
    expect(JSON.stringify(t)).toBe(before)
  })
})

describe('moveTo', () => {
  it('moves a room into a group, at the end', () => {
    expect(shape(moveTo(tree(), roomKey(2), 'down'))).toEqual([
      { down: [1, { dining: [4, 9] }, 5, 2] }, { up: [{ beds: [{ kids: [6, 7] }, 3] }, 8] }, 10
    ])
  })

  it('moves a room into a deeply nested group', () => {
    expect(shape(moveTo(tree(), roomKey(10), 'kids'))).toEqual([
      { down: [1, { dining: [4, 9] }, 5] }, { up: [{ beds: [{ kids: [6, 7, 10] }, 3] }, 8] }, 2
    ])
  })

  it('moves a room out to the top level', () => {
    expect(shape(moveTo(tree(), roomKey(6), null))).toEqual([
      { down: [1, { dining: [4, 9] }, 5] }, { up: [{ beds: [{ kids: [7] }, 3] }, 8] }, 2, 10, 6
    ])
  })

  it('moves a group, with its contents, into another group', () => {
    expect(shape(moveTo(tree(), groupKey('dining'), 'up'))).toEqual([
      { down: [1, 5] }, { up: [{ beds: [{ kids: [6, 7] }, 3] }, 8, { dining: [4, 9] }] }, 2, 10
    ])
  })

  it('refuses to move a group into itself or into something inside it', () => {
    expect(shape(moveTo(tree(), groupKey('up'), 'up'))).toEqual(shape(tree()))
    expect(shape(moveTo(tree(), groupKey('up'), 'kids'))).toEqual(shape(tree()))
    expect(shape(moveTo(tree(), groupKey('beds'), 'kids'))).toEqual(shape(tree()))
  })

  it('does nothing when the node is already there, or the target does not exist', () => {
    expect(shape(moveTo(tree(), roomKey(1), 'down'))).toEqual(shape(tree()))
    expect(shape(moveTo(tree(), roomKey(2), null))).toEqual(shape(tree()))
    expect(shape(moveTo(tree(), roomKey(2), 'nope'))).toEqual(shape(tree()))
    expect(shape(moveTo(tree(), 'room-999', 'down'))).toEqual(shape(tree()))
  })

  it('never loses or duplicates a room, whatever it is moved to', () => {
    const everyMove = [
      moveTo(tree(), groupKey('down'), 'kids'),
      moveTo(tree(), groupKey('kids'), 'down'),
      moveTo(tree(), roomKey(4), 'up'),
      moveTo(tree(), groupKey('beds'), null)
    ]
    for (const result of everyMove) {
      expect(orderedRoomIds(result).sort((a, b) => a - b)).toEqual([1, 2, 3, 4, 5, 6, 7, 8, 9, 10])
    }
  })
})

describe('moveTargets', () => {
  it('offers the top level and every group, indented by depth, flagging where the node already is', () => {
    const targets = moveTargets(tree(), roomKey(9))
    expect(targets.map((t) => `${t.depth}:${t.id ?? 'top'}${t.current ? '*' : ''}`)).toEqual([
      '0:top', '1:down', '2:dining*', '1:up', '2:beds', '3:kids'
    ])
  })

  it('leaves out a group and everything inside it, so it cannot be moved into itself', () => {
    const ids = moveTargets(tree(), groupKey('up')).map((t) => t.id)
    expect(ids).toEqual([null, 'down', 'dining'])
  })

  it('flags the top level as current for a top-level node', () => {
    expect(moveTargets(tree(), roomKey(2))[0].current).toBe(true)
  })
})

describe('addGroup, updateGroup, deleteGroup', () => {
  it('adds an empty group at the end of the top level or of another group', () => {
    expect(shape(addGroup(tree(), { id: 'new', name: 'Garden' }))).toEqual([
      { down: [1, { dining: [4, 9] }, 5] }, { up: [{ beds: [{ kids: [6, 7] }, 3] }, 8] }, 2, 10, { new: [] }
    ])
    expect(shape(addGroup(tree(), { id: 'new', name: 'Garden' }, 'kids'))[1]).toEqual({ up: [{ beds: [{ kids: [6, 7, { new: [] }] }, 3] }, 8] })
  })

  it('renames and re-icons a group at any depth, keeping everything else', () => {
    const t = updateGroup(tree(), 'kids', { name: '  Children  ', icon: '🧸' })
    expect(findNode(t, groupKey('kids')).name).toBe('Children')
    expect(findNode(t, groupKey('kids')).icon).toBe('🧸')
    expect(shape(t)).toEqual(shape(tree()))
  })

  it('ignores a blank name but still applies the icon, and a null icon removes it', () => {
    const blank = updateGroup(tree(), 'down', { name: '   ', icon: '🏠' })
    expect(findNode(blank, groupKey('down')).name).toBe('Downstairs')
    expect(findNode(blank, groupKey('down')).icon).toBe('🏠')
    expect(findNode(updateGroup(tree(), 'down', { icon: null }), groupKey('down')).icon).toBeNull()
  })

  it('deleting a group keeps its contents, moving them up into its parent where it was', () => {
    expect(shape(deleteGroup(tree(), 'beds'))).toEqual([
      { down: [1, { dining: [4, 9] }, 5] }, { up: [{ kids: [6, 7] }, 3, 8] }, 2, 10
    ])
    expect(shape(deleteGroup(tree(), 'down'))).toEqual([
      1, { dining: [4, 9] }, 5, { up: [{ beds: [{ kids: [6, 7] }, 3] }, 8] }, 2, 10
    ])
  })

  it('deleting a group never deletes a room', () => {
    expect(orderedRoomIds(deleteGroup(tree(), 'up')).sort((a, b) => a - b)).toEqual([1, 2, 3, 4, 5, 6, 7, 8, 9, 10])
  })

  it('makes up group ids that are not already taken', () => {
    expect(groupIds(tree())).not.toContain(newGroupId(tree()))
    const crowded = addGroup(addGroup(tree(), { id: 'g6', name: 'x' }), { id: 'g7', name: 'y' })
    expect(groupIds(crowded)).not.toContain(newGroupId(crowded))
  })
})

describe('presentation', () => {
  const byId = new Map([
    [4, { id: 4, currentTemperature: 19.1, heatingActive: false }],
    [9, { id: 9, currentTemperature: 19.1, heatingActive: true }],
    [1, { id: 1, currentTemperature: 21, heatingActive: false }],
    [5, { id: 5, currentTemperature: null, heatingActive: false }]
  ])

  it('reports one shared value when every room reads the same', () => {
    const summary = temperatureSummary(findNode(tree(), groupKey('dining')), byId)
    expect(summary).toEqual({ low: 19.1, high: 19.1, shared: true })
  })

  it('reports a range across rooms with different readings, ignoring rooms with none yet', () => {
    const summary = temperatureSummary(findNode(tree(), groupKey('down')), byId)
    expect(summary).toEqual({ low: 19.1, high: 21, shared: false })
  })

  it('reports nothing when no room in the group has a reading', () => {
    expect(temperatureSummary(findNode(tree(), groupKey('kids')), byId)).toBeNull()
  })

  it('lights a group when any room anywhere inside it is heating', () => {
    expect(anyHeating(findNode(tree(), groupKey('down')), byId)).toBe(true)
    expect(anyHeating(findNode(tree(), groupKey('up')), byId)).toBe(false)
  })
})

describe('normaliseIcon', () => {
  it.each([
    ['🍳', '🍳'],
    ['  🛏️  ', '🛏️'],
    ['🍽️ dining', '🍽️'],
    ['👨‍👩‍👧', '👨‍👩‍👧'],
    ['🇬🇧', '🇬🇧']
  ])('keeps the first whole emoji of %j', (input, expected) => {
    expect(normaliseIcon(input)).toBe(expected)
  })

  it.each([null, undefined, '', '   '])('gives null for blank input %j', (input) => {
    expect(normaliseIcon(input)).toBeNull()
  })
})

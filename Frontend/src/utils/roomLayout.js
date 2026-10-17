// How rooms are arranged on the heating page: an ordered tree of rooms and groups, to any depth.
// Pure functions, so the rules are unit-tested rather than click-tested.
//
// The saved `layout` is a tree. A node is a room (`{ roomId }`) or a group (`{ id, name, icon,
// children }`), and a group's children are more nodes. The list of rooms itself stays flat: the
// heating code never sees any of this, so a room is scheduled and controlled the same wherever it sits.
//
// Here the tree uses a slightly richer shape so every node has a unique `key`:
//   { type: 'room',  key: 'room-7',     roomId }
//   { type: 'group', key: 'group-g-ab1', id, name, icon, children: [...] }
//
// Every function returns NEW data and never mutates its input.

export const roomKey = (roomId) => `room-${roomId}`
export const groupKey = (id) => `group-${id}`

// ---- Building the tree from what was saved --------------------------------------------------------

/**
 * Turns the saved layout plus the flat rooms list into the tree the page shows.
 *
 * The two can drift apart, and this reconciles them rather than failing: a node naming a room that no
 * longer exists is skipped, a room named twice is shown once (the first time), and any room the layout
 * does not mention is appended to the top level, in the rooms list's order. An empty layout therefore
 * simply lists the rooms. Groups with no usable id get a fresh one, so every node has a unique key.
 */
export const fromLayoutDto = (layout, rooms) => {
  const known = new Set(rooms.map((r) => r.id))
  const placed = new Set()
  const usedGroupIds = new Set()

  const freshId = () => {
    let n = usedGroupIds.size + 1
    while (usedGroupIds.has(`g${n}`)) n += 1
    return `g${n}`
  }

  const convert = (nodes) => {
    const out = []
    for (const node of nodes ?? []) {
      if (node.roomId !== null && node.roomId !== undefined) {
        if (known.has(node.roomId) && !placed.has(node.roomId)) {
          placed.add(node.roomId)
          out.push({ type: 'room', key: roomKey(node.roomId), roomId: node.roomId })
        }
        continue
      }

      let id = typeof node.id === 'string' && node.id.trim() ? node.id.trim() : null
      if (id === null || usedGroupIds.has(id)) id = freshId()
      usedGroupIds.add(id)

      out.push({
        type: 'group',
        key: groupKey(id),
        id,
        name: (node.name ?? '').trim() || 'Group',
        icon: node.icon || null,
        children: convert(node.children)
      })
    }
    return out
  }

  const tree = convert(layout)

  for (const room of rooms) {
    if (!placed.has(room.id)) {
      placed.add(room.id)
      tree.push({ type: 'room', key: roomKey(room.id), roomId: room.id })
    }
  }

  return tree
}

/** The inverse: the tree as the plain structure that is saved. */
export const toLayoutDto = (tree) =>
  tree.map((node) =>
    node.type === 'room'
      ? { roomId: node.roomId }
      : { id: node.id, name: node.name, icon: node.icon ?? null, children: toLayoutDto(node.children) }
  )

// ---- Finding things --------------------------------------------------------------------------------

/** Every node in pre-order (a group, then what is inside it). */
export const walk = (tree, visit, depth = 0, parent = null) => {
  for (const node of tree) {
    visit(node, depth, parent)
    if (node.type === 'group') walk(node.children, visit, depth + 1, node)
  }
}

export const findNode = (tree, key) => {
  let found = null
  walk(tree, (node) => {
    if (!found && node.key === key) found = node
  })
  return found
}

/** The group a node sits directly inside, or null when it is at the top level (or not found). */
export const parentOf = (tree, key) => {
  let parent = null
  walk(tree, (node, _depth, p) => {
    if (node.key === key) parent = p
  })
  return parent
}

/** The ids of every room at or below a node, in display order. */
export const roomIdsIn = (node) =>
  node.type === 'room' ? [node.roomId] : node.children.flatMap(roomIdsIn)

/** Room ids in display order across the whole tree. */
export const orderedRoomIds = (tree) => tree.flatMap(roomIdsIn)

/** Every group id in use. */
export const groupIds = (tree) => {
  const ids = []
  walk(tree, (node) => node.type === 'group' && ids.push(node.id))
  return ids
}

/** An id no existing group has. Short and readable, since it is stored and never shown. */
export const newGroupId = (tree) => {
  const used = new Set(groupIds(tree))
  let n = used.size + 1
  while (used.has(`g${n}`)) n += 1
  return `g${n}`
}

// ---- Changing the tree -----------------------------------------------------------------------------

// Rebuilds the tree, replacing the child list of the group `parentId` (null = the top level).
const withChildren = (tree, parentId, change) => {
  if (parentId === null) return change(tree)
  const visit = (nodes) =>
    nodes.map((node) => {
      if (node.type !== 'group') return node
      if (node.id === parentId) return { ...node, children: change(node.children) }
      return { ...node, children: visit(node.children) }
    })
  return visit(tree)
}

const swap = (items, from, to) => {
  const copy = [...items]
  const [moved] = copy.splice(from, 1)
  copy.splice(to, 0, moved)
  return copy
}

const siblingsAndIndex = (tree, key) => {
  const parent = parentOf(tree, key)
  const siblings = parent ? parent.children : tree
  return { parentId: parent ? parent.id : null, siblings, index: siblings.findIndex((n) => n.key === key) }
}

/** True when the node can move by `delta` (-1 up, +1 down) among its siblings. */
export const canMove = (tree, key, delta) => {
  const { siblings, index } = siblingsAndIndex(tree, key)
  return index > -1 && index + delta >= 0 && index + delta < siblings.length
}

/** Moves a room or a whole group one place up or down among its siblings. */
export const move = (tree, key, delta) => {
  if (!canMove(tree, key, delta)) return tree
  const { parentId, index } = siblingsAndIndex(tree, key)
  return withChildren(tree, parentId, (children) => swap(children, index, index + delta))
}

/**
 * Where a node could be moved to: the top level and every group, indented by depth, excluding the
 * node itself and anything inside it (a group cannot go into itself). The node's current parent is
 * flagged so the picker can show where it already is.
 */
export const moveTargets = (tree, key) => {
  const node = findNode(tree, key)
  const parent = parentOf(tree, key)
  const targets = [{ id: null, name: 'Top level', icon: null, depth: 0, current: parent === null }]
  if (!node) return targets

  const inside = new Set(node.type === 'group' ? groupIds([node]) : [])
  walk(tree, (n, depth) => {
    if (n.type === 'group' && !inside.has(n.id)) {
      targets.push({ id: n.id, name: n.name, icon: n.icon, depth: depth + 1, current: parent?.id === n.id })
    }
  })
  return targets
}

/**
 * Moves a node to the end of another group (`targetId`), or to the end of the top level (null).
 * Refuses, returning the tree unchanged, to move a group into itself or one of its own descendants,
 * and does nothing when it is already there.
 */
export const moveTo = (tree, key, targetId) => {
  const node = findNode(tree, key)
  if (!node) return tree

  const parent = parentOf(tree, key)
  if ((parent ? parent.id : null) === targetId) return tree

  if (targetId !== null) {
    const target = findNode(tree, groupKey(targetId))
    if (!target || target.type !== 'group') return tree
    if (node.type === 'group' && groupIds([node]).includes(targetId)) return tree
  }

  const without = removeNode(tree, key)
  return withChildren(without, targetId, (children) => [...children, node])
}

const removeNode = (tree, key) =>
  tree
    .filter((n) => n.key !== key)
    .map((n) => (n.type === 'group' ? { ...n, children: removeNode(n.children, key) } : n))

/** Adds an empty group at the end of `parentId` (null = top level). */
export const addGroup = (tree, group, parentId = null) =>
  withChildren(tree, parentId, (children) => [
    ...children,
    { type: 'group', key: groupKey(group.id), id: group.id, name: group.name, icon: group.icon ?? null, children: [] }
  ])

/** Changes a group's name and/or icon. A blank name is ignored; a null icon removes it. */
export const updateGroup = (tree, id, changes) => {
  const visit = (nodes) =>
    nodes.map((node) => {
      if (node.type !== 'group') return node
      if (node.id !== id) return { ...node, children: visit(node.children) }
      const name = changes.name !== undefined && changes.name.trim() ? changes.name.trim() : node.name
      const icon = changes.icon !== undefined ? changes.icon || null : node.icon
      return { ...node, name, icon }
    })
  return visit(tree)
}

/**
 * Deletes a group but keeps what is in it: its children move up into its parent, at the position the
 * group had, in order. Deleting a group never deletes a room.
 */
export const deleteGroup = (tree, id) => {
  const spliceOut = (nodes) =>
    nodes.flatMap((node) => {
      if (node.type !== 'group') return [node]
      if (node.id === id) return node.children
      return [{ ...node, children: spliceOut(node.children) }]
    })
  return spliceOut(tree)
}

// ---- Presentation helpers --------------------------------------------------------------------------

/**
 * What a group should say about the temperature of the rooms inside it. Rooms that share a thermometer
 * report one value, so that is shown plainly; otherwise the range is more honest than any single number.
 * Returns null when no room in the group has a reading yet.
 */
export const temperatureSummary = (node, roomsById) => {
  const readings = roomIdsIn(node)
    .map((id) => roomsById.get(id)?.currentTemperature)
    .filter((t) => t !== null && t !== undefined)

  if (readings.length === 0) return null
  const low = Math.min(...readings)
  const high = Math.max(...readings)
  return { low, high, shared: Math.abs(high - low) < 0.05 }
}

/** True when any room at or below the node is heating. */
export const anyHeating = (node, roomsById) =>
  roomIdsIn(node).some((id) => roomsById.get(id)?.heatingActive)

// ---- Icons -----------------------------------------------------------------------------------------

/**
 * Reduces whatever was typed or pasted to a single emoji. An icon is one "character" as a person sees
 * it, which can be several code points (a flag, a family, an emoji with a variation selector), so this
 * keeps the first grapheme rather than slicing the string. Blank input means "no icon" and gives null.
 */
export const normaliseIcon = (text) => {
  const trimmed = (text ?? '').trim()
  if (!trimmed) return null

  const segmenter = new Intl.Segmenter(undefined, { granularity: 'grapheme' })
  const first = segmenter.segment(trimmed)[Symbol.iterator]().next().value
  return first ? first.segment : null
}

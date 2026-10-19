// Who is in charge of a boost. A group boost is stored as the same boost on every zone inside the group,
// each tagged with the group's id (`boost.groupId`); a zone's own boost has no tag. While a group's boost
// runs, nothing inside it can be boosted on its own: its zones and smaller groups are locked, and only that
// group (or a group around it, whose boost supersedes it) can change or cancel it (owner, 2026-10-19).
//
// Works on the layout tree from roomLayout.js. Pure functions, so the rules are unit-tested.

import { roomKey, roomIdsIn } from './roomLayout.js'

/** Whether a boost is running at `now` (milliseconds). */
export const isBoostActive = (boost, now = Date.now()) =>
  !!boost?.endTime && Date.parse(boost.endTime) > now

/** The groups a node sits inside, outermost first. Empty for a top-level node or one not in the tree. */
export const groupsAround = (tree, key) => {
  const search = (nodes, path) => {
    for (const node of nodes) {
      if (node.key === key) return path
      if (node.type === 'group') {
        const found = search(node.children, [...path, node])
        if (found) return found
      }
    }
    return null
  }
  return search(tree, []) ?? []
}

/** Whether this group's own boost is running: a zone inside carries a running boost tagged with it. */
export const groupBoostActive = (group, roomsById, now = Date.now()) =>
  roomIdsIn(group).some((id) => {
    const boost = roomsById.get(id)?.boost
    return isBoostActive(boost, now) && boost.groupId === group.id
  })

/**
 * The group whose running boost locks a zone's boost controls, or null. Only a group the zone still sits in
 * counts: a tag naming a group that has since been deleted, or that the zone was moved out of, locks nothing.
 */
export const roomLockedBy = (tree, room, now = Date.now()) => {
  const groupId = isBoostActive(room?.boost, now) ? room.boost.groupId : null
  if (!groupId) return null
  return groupsAround(tree, roomKey(room.id)).find((group) => group.id === groupId) ?? null
}

/** The group around this one whose running boost locks its boost controls, or null. Outermost first. */
export const groupLockedBy = (tree, group, roomsById, now = Date.now()) =>
  groupsAround(tree, group.key).find((outer) => groupBoostActive(outer, roomsById, now)) ?? null

/** The boost a group sets on each zone inside it: the zone boost, tagged with the group. */
export const groupBoost = (group, startTime, endTime, temperature) => ({
  startTime: startTime.toISOString(),
  endTime: endTime.toISOString(),
  temperature,
  groupId: group.id
})

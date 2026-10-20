// Low-battery warnings on the heating page (owner, 2026-10-20). The NetDaemon app reports every replaceable battery
// in the house with its room states (`batteries`: { entityId, name, percent, area, deviceEntityIds }). Each is shown
// on the zone or layout group it belongs to:
//   1. a zone whose heating uses the device (its TRV or thermometer: any of the device's entities is one of the
//      zone's devices). A device two zones share, like the dining room thermometer, belongs to both;
//   2. otherwise the zone or group its Home Assistant area is mapped to (`heating.areaPlacement`);
//   3. otherwise nowhere in particular: the top of the page.
// A place shows the lowest battery at or below LOW_BATTERY_PERCENT. A collapsed group also shows the lowest of
// everything inside it, so a warning cannot be hidden by folding the group away.

import { roomLabel } from './rooms.js'

export const LOW_BATTERY_PERCENT = 20

/** A reading at or below the threshold. An unavailable sensor (no percent) is not a flat battery. */
export const isLow = (battery) => typeof battery?.percent === 'number' && battery.percent <= LOW_BATTERY_PERCENT

/** The lowest low battery in the list, or null when none is low. */
export const lowestLow = (batteries) =>
  (batteries ?? []).filter(isLow).reduce((low, battery) => (low === null || battery.percent < low.percent ? battery : low), null)

/** 'room:5' -> { kind: 'room', id: '5' }; 'group:g1' -> { kind: 'group', id: 'g1' }; anything else -> null. */
export const parsePlace = (place) => {
  const match = /^(room|group):(.+)$/.exec(place ?? '')
  return match ? { kind: match[1], id: match[2] } : null
}

export const roomPlace = (roomId) => `room:${roomId}`
export const groupPlace = (groupId) => `group:${groupId}`

const groupsIn = (nodes) =>
  (nodes ?? []).flatMap(node => (node.type === 'group' ? [node, ...groupsIn(node.children)] : []))

/** The zones whose heating uses this battery's device. */
export const zonesUsing = (battery, rooms) => {
  const ids = new Set(battery.deviceEntityIds?.length ? battery.deviceEntityIds : [battery.entityId])
  return (rooms ?? []).filter(room => (room.devices ?? []).some(device => ids.has(device.entityId)))
}

/**
 * Where every battery shows. Room ids are compared as strings, so a place saved as text still matches.
 * @returns {{ byRoom: Map<string, object[]>, byGroup: Map<string, object[]>, unplaced: object[] }}
 */
export const placeBatteries = (batteries, rooms, layoutTree, areaPlacement) => {
  const byRoom = new Map()
  const byGroup = new Map()
  const unplaced = []
  const roomIds = new Set((rooms ?? []).map(room => String(room.id)))
  const groupIds = new Set(groupsIn(layoutTree).map(group => String(group.id)))
  const add = (map, key, battery) => map.set(key, [...(map.get(key) ?? []), battery])

  for (const battery of batteries ?? []) {
    const zones = zonesUsing(battery, rooms)
    if (zones.length > 0) {
      zones.forEach(room => add(byRoom, String(room.id), battery))
      continue
    }

    const place = parsePlace(battery.area ? areaPlacement?.[battery.area] : null)
    if (place?.kind === 'room' && roomIds.has(place.id)) add(byRoom, place.id, battery)
    else if (place?.kind === 'group' && groupIds.has(place.id)) add(byGroup, place.id, battery)
    else unplaced.push(battery)
  }

  return { byRoom, byGroup, unplaced }
}

/** The batteries shown on a zone. */
export const batteriesForRoom = (placement, roomId) => placement.byRoom.get(String(roomId)) ?? []

/**
 * The batteries placed on a group itself, and, with `inside`, on everything within it at any depth. A battery two
 * zones share is counted once.
 */
export const batteriesForGroup = (placement, group, inside) => {
  const found = [...(placement.byGroup.get(String(group.id)) ?? [])]
  if (inside) {
    const walk = (nodes) => {
      for (const node of nodes ?? []) {
        if (node.type === 'group') {
          found.push(...(placement.byGroup.get(String(node.id)) ?? []))
          walk(node.children)
        } else {
          found.push(...batteriesForRoom(placement, node.roomId))
        }
      }
    }
    walk(group.children)
  }
  return [...new Map(found.map(battery => [battery.entityId, battery])).values()]
}

/**
 * Every place an area can be shown on, in layout order, groups and zones alike, indented by depth:
 * [{ value: 'group:g1' | 'room:5', label, depth }].
 */
export const placeTargets = (layoutTree, roomsById) => {
  const targets = []
  const walk = (nodes, depth) => {
    for (const node of nodes ?? []) {
      if (node.type === 'group') {
        targets.push({ value: groupPlace(node.id), label: node.name, depth })
        walk(node.children, depth + 1)
      } else {
        const room = roomsById.get(node.roomId)
        if (room) targets.push({ value: roomPlace(room.id), label: roomLabel(room), depth })
      }
    }
  }
  walk(layoutTree, 0)
  return targets
}

const byName = (a, b) => a.localeCompare(b, undefined, { numeric: true, sensitivity: 'base' })

/**
 * The batteries grouped by Home Assistant area for the batteries dialog, areas in natural order with "no area"
 * last, each battery saying which zones' heating uses it (those ignore their area's placement).
 * @returns {{ area: string|null, batteries: { battery: object, zones: object[] }[] }[]}
 */
export const batteriesByArea = (batteries, rooms) => {
  const areas = new Map()
  for (const battery of batteries ?? []) {
    const key = battery.area ?? null
    areas.set(key, [...(areas.get(key) ?? []), { battery, zones: zonesUsing(battery, rooms) }])
  }
  return [...areas.entries()]
    .sort(([a], [b]) => (a === null ? 1 : b === null ? -1 : byName(a, b)))
    .map(([area, items]) => ({ area, batteries: items.sort((a, b) => byName(a.battery.name, b.battery.name)) }))
}

// The hardware a heating zone is bound to, as published by the NetDaemon app on each room state
// (`devices`). The app builds the list from the same switch statements that bind the zone, so this
// is what the heating code actually drives and reads, not a second copy of the wiring. Display only.
//
// Each device: { entityId, name, role, state, unit, currentTemperature, targetTemperature }.

import { roomLabel } from './rooms.js'

// Labels sit under a group heading (see DEVICE_GROUPS), so they don't repeat what the group says.
const ROLE_LABELS = {
  thermometer: 'Thermometer',
  radiatorValve: 'Radiator valve',
  heaterPlug: 'Heater plug',
  // No valve or switch: hot whenever the boiler runs, so its zone is heated by running the boiler.
  towelRail: 'Towel rail on the boiler',
  deskPlug: 'Desk plug',
  deskPower: 'Desk power',
  tvPower: 'TV power',
  lightsPlug: 'Lights plug',
  // The router's list of devices online, which phone presence was read from. Only from apps before 2026-10-21.
  routerDevices: 'Router',
  // A phone on the home Wi-Fi (not a Home Assistant entity): "home" or "away".
  phone: 'Phone'
}

// How a zone's devices are grouped in the UI: what it reads, what heats, and how it detects presence.
export const DEVICE_GROUPS = [
  { key: 'readers', title: 'Readers', roles: ['thermometer'] },
  { key: 'heaters', title: 'Heaters', roles: ['radiatorValve', 'heaterPlug', 'towelRail'] },
  { key: 'presence', title: 'Presence', roles: ['deskPlug', 'deskPower', 'tvPower', 'lightsPlug', 'phone', 'routerDevices'] }
]

// Display order within a zone, which is also the order within each group.
const ROLE_ORDER = DEVICE_GROUPS.flatMap(group => group.roles)

export const roleLabel = (role) => ROLE_LABELS[role] ?? 'Device'

/** The device's Home Assistant friendly name, falling back to its entity ID. */
export const deviceName = (device) => device?.name?.trim() || device?.entityId || ''

/**
 * Home Assistant reports a device it cannot reach as `unavailable` (or `unknown` before it has
 * reported). A missing state means the entity was not found at all. None of those are readings.
 * A towel rail is not in Home Assistant at all, so it is never unavailable.
 */
export const isUnavailable = (device) =>
  device?.role !== 'towelRail' &&
  (device?.state == null || device.state === 'unavailable' || device.state === 'unknown')

/**
 * A short description of what the device says now.
 * @param {object} device
 * @param {(celsius: number) => string} formatTempWithUnit from useFormatting, so the user's unit applies
 */
export const describeState = (device, formatTempWithUnit) => {
  // Not in Home Assistant: its state is the boiler's, sent only while the app runs the boiler.
  if (device?.role === 'towelRail') {
    if (device.state === 'on') return 'Hot, the boiler is on'
    if (device.state === 'off') return 'Cold, the boiler is off'
    return 'Hot whenever the boiler runs'
  }
  if (device?.state == null) return 'Not found in Home Assistant'
  if (device.state === 'unavailable') return 'Unavailable'
  if (device.state === 'unknown') return 'Unknown'

  switch (device.role) {
    case 'thermometer': {
      const value = Number(device.state)
      return Number.isFinite(value) ? formatTempWithUnit(value) : device.state
    }
    case 'radiatorValve': {
      if (device.state === 'off') return 'Off'
      const parts = []
      if (device.targetTemperature != null) parts.push(`Set to ${formatTempWithUnit(device.targetTemperature)}`)
      if (device.currentTemperature != null) parts.push(`reads ${formatTempWithUnit(device.currentTemperature)}`)
      return parts.length ? parts.join(', ') : device.state
    }
    case 'phone':
      if (device.state === 'home') return 'On the home Wi-Fi'
      if (device.state === 'away') return 'Not on the home Wi-Fi'
      return device.state
    case 'heaterPlug':
    case 'deskPlug':
    case 'lightsPlug':
      if (device.state === 'on') return 'On'
      if (device.state === 'off') return 'Off'
      return device.state
    default:
      return device.unit ? `${device.state} ${device.unit}` : device.state
  }
}

/** A zone's devices in display order: by role, then by name. Does not modify the input. */
export const sortDevices = (devices) =>
  [...(devices ?? [])].sort((a, b) => {
    const byRole = rank(a.role) - rank(b.role)
    return byRole !== 0
      ? byRole
      : deviceName(a).localeCompare(deviceName(b), undefined, { numeric: true, sensitivity: 'base' })
  })

/**
 * A zone's devices split into Readers, Heaters and Presence, each in display order, leaving out empty
 * groups. A role this file doesn't know goes in a trailing "Other" group rather than disappearing.
 * @returns {{ key: string, title: string, devices: object[] }[]}
 */
export const groupDevices = (devices) => {
  const sorted = sortDevices(devices)
  const groups = DEVICE_GROUPS.map(group => ({
    key: group.key,
    title: group.title,
    devices: sorted.filter(d => group.roles.includes(d.role))
  }))
  groups.push({ key: 'other', title: 'Other', devices: sorted.filter(d => !ROLE_ORDER.includes(d.role)) })
  return groups.filter(group => group.devices.length > 0)
}

const rank = (role) => {
  const index = ROLE_ORDER.indexOf(role)
  return index === -1 ? ROLE_ORDER.length : index
}

/**
 * One section per zone, in the order given (layout order for a group). Zones are kept apart
 * rather than merged, because which zone owns a device is the useful part: a thermometer two
 * zones share appears under both.
 * @param {object[]} rooms
 * @returns {{ roomId: number, title: string, icon: string|undefined, devices: object[] }[]}
 */
export const deviceSections = (rooms) =>
  rooms
    .filter(Boolean)
    .map(room => ({
      roomId: room.id,
      // The canonical zone name, which the occupancy setting is keyed by; title is only for display.
      name: room.name,
      title: roomLabel(room),
      icon: room.icon,
      devices: sortDevices(room.devices),
      // Which presence detectors count for this zone (from the app); null from an app too old to send it.
      occupancy: room.occupancy ?? null
    }))

// The filters the Devices dialog offers (owner, 2026-10-21), for a zone, a group or the whole house. Each says which
// device roles it keeps, and whether the zone's batteries list shows. "Battery" is every device with a replaceable
// battery, which is exactly that list (each entry is a device, named as in Home Assistant), so it shows only that.
export const DEVICE_FILTERS = [
  { key: 'all', label: 'All', roles: null, batteries: true },
  { key: 'trv', label: 'TRVs', roles: ['radiatorValve'], batteries: false },
  { key: 'plug', label: 'Plugs', roles: ['heaterPlug', 'deskPlug', 'lightsPlug', 'deskPower', 'tvPower'], batteries: false },
  { key: 'heating', label: 'Heating', roles: ['radiatorValve', 'heaterPlug'], batteries: false },
  { key: 'thermometer', label: 'Thermometers', roles: ['thermometer'], batteries: false },
  { key: 'battery', label: 'Battery', roles: [], batteries: true }
]

/**
 * A section as one filter shows it: the devices and batteries it keeps, and whether the zone-wide lines (occupancy,
 * warm-up) still make sense. Null when the filter leaves nothing in the section, so it can be hidden.
 * @param {{ devices: object[], batteries?: object[], occupancy?: object|null }} section
 * @param {string} filterKey one of DEVICE_FILTERS' keys; an unknown key shows everything
 */
export const filterSection = (section, filterKey) => {
  const filter = DEVICE_FILTERS.find(f => f.key === filterKey) ?? DEVICE_FILTERS[0]
  const devices = filter.roles === null
    ? [...(section.devices ?? [])]
    : (section.devices ?? []).filter(device => filter.roles.includes(device.role))
  const batteries = filter.batteries ? [...(section.batteries ?? [])] : []
  if (devices.length === 0 && batteries.length === 0 && filter.key !== 'all') return null
  return {
    ...section,
    devices,
    batteries,
    showOccupancy: filter.key === 'all',
    showWarmUp: filter.key === 'all' || filter.key === 'heating' || filter.key === 'trv'
  }
}

/**
 * Every zone's devices for the whole house, in layout order: each group's own batteries (those placed on the group
 * rather than a zone) before what is inside it, each zone's devices with its batteries, then the batteries placed
 * nowhere. Mirrors what a group's Devices list shows, for the top of the tree.
 * @param {object[]} layoutTree utils/roomLayout.js nodes: { type: 'group', id, name, icon, children } or { type: 'room', roomId }
 * @param {Map<number, object>} roomsById rooms merged with their states
 * @param {{ byRoom: Map, byGroup: Map, unplaced: object[] }|null} placement from utils/batteries.js placeBatteries
 */
export const houseDeviceSections = (layoutTree, roomsById, placement) => {
  const sections = []
  const walk = (nodes) => {
    for (const node of nodes ?? []) {
      if (node.type === 'group') {
        const own = placement?.byGroup?.get(String(node.id)) ?? []
        if (own.length > 0) {
          sections.push({ roomId: `group:${node.id}`, title: node.name, icon: node.icon, devices: [], occupancy: null, batteries: own })
        }
        walk(node.children)
      } else {
        const room = roomsById.get(node.roomId)
        if (!room) continue
        const [section] = deviceSections([room])
        sections.push({ ...section, batteries: placement?.byRoom?.get(String(room.id)) ?? [] })
      }
    }
  }
  walk(layoutTree)
  const unplaced = placement?.unplaced ?? []
  if (unplaced.length > 0) {
    sections.push({ roomId: 'unplaced', title: 'Not in any room or group', devices: [], occupancy: null, batteries: unplaced })
  }
  return sections
}

/** How many distinct entities the rooms use between them, counting a shared one once. */
export const distinctDeviceCount = (rooms) =>
  new Set(rooms.filter(Boolean).flatMap(room => (room.devices ?? []).map(d => d.entityId))).size

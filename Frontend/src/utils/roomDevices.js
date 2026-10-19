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
  deskPlug: 'Desk plug',
  deskPower: 'Desk power',
  tvPower: 'TV power',
  lightsPlug: 'Lights plug'
}

// How a zone's devices are grouped in the UI: what it reads, what heats, and how it detects presence.
export const DEVICE_GROUPS = [
  { key: 'readers', title: 'Readers', roles: ['thermometer'] },
  { key: 'heaters', title: 'Heaters', roles: ['radiatorValve', 'heaterPlug'] },
  { key: 'presence', title: 'Presence', roles: ['deskPlug', 'deskPower', 'tvPower', 'lightsPlug'] }
]

// Display order within a zone, which is also the order within each group.
const ROLE_ORDER = DEVICE_GROUPS.flatMap(group => group.roles)

export const roleLabel = (role) => ROLE_LABELS[role] ?? 'Device'

/** The device's Home Assistant friendly name, falling back to its entity ID. */
export const deviceName = (device) => device?.name?.trim() || device?.entityId || ''

/**
 * Home Assistant reports a device it cannot reach as `unavailable` (or `unknown` before it has
 * reported). A missing state means the entity was not found at all. None of those are readings.
 */
export const isUnavailable = (device) =>
  device?.state == null || device.state === 'unavailable' || device.state === 'unknown'

/**
 * A short description of what the device says now.
 * @param {object} device
 * @param {(celsius: number) => string} formatTempWithUnit from useFormatting, so the user's unit applies
 */
export const describeState = (device, formatTempWithUnit) => {
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

/** How many distinct entities the rooms use between them, counting a shared one once. */
export const distinctDeviceCount = (rooms) =>
  new Set(rooms.filter(Boolean).flatMap(room => (room.devices ?? []).map(d => d.entityId))).size

// Which presence detectors count for each zone (owner, 2026-10-19).
//
// The detectors themselves (what counts as in use, with their wattages and hold times) are defined in the
// NetDaemon app (PresenceService) and arrive on each room state as `occupancy`:
//
//   { detectorIds: ['den-tv-on'], isDefault: true, available: [{ id, label, meansSomeoneHome, active }] }
//
// The choice is saved in the device-settings blob as `heating.zoneOccupancy`: null until someone first saves
// (the app then uses its built-in defaults), then zone canonical name -> detector ids. Once saved it is the
// whole truth, so a zone it leaves out has no presence; the first save therefore writes every zone's current
// detectors alongside the edit, like the device rules' first save.

/**
 * The detector ids that count for a zone: the saved choice once there is one, otherwise what the app reports.
 * @param {{ name: string, occupancy?: object|null }} zone
 * @param {Record<string, string[]>|null|undefined} savedMap `heating.zoneOccupancy`
 */
export const effectiveDetectorIds = (zone, savedMap) => {
  if (savedMap != null) {
    const key = Object.keys(savedMap).find(k => k.trim().toLowerCase() === zone?.name?.trim().toLowerCase())
    return key === undefined ? [] : [...savedMap[key]]
  }
  return [...(zone?.occupancy?.detectorIds ?? [])]
}

/**
 * The map to save when nothing has been saved yet: every zone keeps the detectors it has now, and this zone
 * gets the new choice. Zones with no detectors are left out, which means the same thing.
 * @param {{ name: string, occupancy?: object|null }[]} zones every zone in the house
 * @param {string} zoneName
 * @param {string[]} detectorIds
 */
export const occupancyFirstSaveMap = (zones, zoneName, detectorIds) => {
  const map = {}
  for (const zone of zones ?? []) {
    const ids = zone?.occupancy?.detectorIds ?? []
    if (zone?.name && ids.length > 0 && !(zone.name in map)) map[zone.name] = [...ids]
  }
  map[zoneName] = [...detectorIds]
  return map
}

/**
 * A short sentence for the Devices list, e.g. "In use when: Den TV on (…) or Living room stick lights on".
 * @param {string[]} detectorIds
 * @param {{ id: string, label: string }[]} available
 */
export const summariseOccupancy = (detectorIds, available) => {
  const labels = (detectorIds ?? []).map(id => available?.find(d => d.id === id)?.label ?? id)
  if (labels.length === 0) return 'No presence: never counts as in use'
  return `In use when: ${labels.join(' or ')}`
}

/**
 * What makes a zone count as occupied, for the schedule editor: each of its detectors with whether it is on now.
 * A detector the app no longer offers keeps its id as its label and reads as off.
 * @param {{ name: string, occupancy?: object|null }} zone
 * @param {Record<string, string[]>|null|undefined} savedMap `heating.zoneOccupancy`
 * @returns {{ id: string, label: string, active: boolean }[]}
 */
export const zoneDetectors = (zone, savedMap) => {
  const available = zone?.occupancy?.available ?? []
  return effectiveDetectorIds(zone, savedMap).map(id => {
    const detector = available.find(d => d.id === id)
    return { id, label: detector?.label ?? id, active: detector?.active === true }
  })
}

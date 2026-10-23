// The Zigbee mesh for the 3D view's signal lines (owner, 2026-10-22): how well devices hear each other, and the route
// a device's messages most likely take to the hub. ZHA reports neighbour tables (who hears whom, at what LQI), not
// the routes messages actually take, so the route here is worked out, not read: the path whose weakest link is the
// strongest, then the one with fewest hops. LQI is 0–255.

/** 'strong' (150+), 'fair' (100–149) or 'weak' (under 100), as tools/ha/zigbee-mesh.cjs bands them. */
export function lqiBand(lqi) {
  return lqi >= 150 ? 'strong' : lqi >= 100 ? 'fair' : 'weak'
}

/**
 * The mesh indexed by device: for each device, every device it has a link with, with the LQI each way (`heard`: how
 * well this device hears the other; `heardBy`: how well the other hears this one), what the other is to it, and
 * `lqi`: the link's strength, the weaker direction where both are known.
 */
export function meshIndex(zigbee) {
  const roles = new Map((zigbee?.nodes ?? []).map((n) => [n.deviceId, n.role]))
  const links = new Map()
  const entry = (a, b) => {
    if (!links.has(a)) links.set(a, new Map())
    const map = links.get(a)
    if (!map.has(b)) map.set(b, { id: b, heard: null, heardBy: null, relationship: null })
    return map.get(b)
  }
  const flip = { parent: 'child', child: 'parent', sibling: 'sibling', other: 'other' }
  for (const link of zigbee?.links ?? []) {
    if (!link.lqi || link.from === link.to) continue
    const mine = entry(link.from, link.to)
    mine.heard = Math.max(mine.heard ?? 0, link.lqi)
    mine.relationship ??= link.relationship
    const theirs = entry(link.to, link.from)
    theirs.heardBy = Math.max(theirs.heardBy ?? 0, link.lqi)
    theirs.relationship ??= flip[link.relationship] ?? 'other'
  }
  for (const map of links.values()) {
    for (const link of map.values()) {
      link.lqi = link.heard && link.heardBy ? Math.min(link.heard, link.heardBy) : (link.heard ?? link.heardBy)
    }
  }
  return { roles, links }
}

/** Every device a device has a link with, strongest first. */
export function linksOf(zigbee, deviceId, index = meshIndex(zigbee)) {
  return [...(index.links.get(deviceId)?.values() ?? [])].sort((a, b) => b.lqi - a.lqi)
}

/** Each pair of devices with a link once: [{ a, b, lqi, relationship }]. */
export function uniqueLinks(zigbee, index = meshIndex(zigbee)) {
  const seen = new Set()
  const pairs = []
  for (const [a, map] of index.links) {
    for (const link of map.values()) {
      const key = a < link.id ? `${a}|${link.id}` : `${link.id}|${a}`
      if (seen.has(key)) continue
      seen.add(key)
      pairs.push({ a, b: link.id, lqi: link.lqi, relationship: link.relationship })
    }
  }
  return pairs
}

/**
 * A device's likely route to the hub: [{ id, lqi }], from the device to the hub, each step's `lqi` being the link
 * it was reached over (null for the first). A battery device goes through its parent; routers then take the path
 * whose weakest link is strongest. Null when the hub can't be reached from the links known.
 */
export function routeToHub(zigbee, deviceId, index = meshIndex(zigbee)) {
  const hub = [...index.roles].find(([, role]) => role === 'coordinator')?.[0]
  if (!hub || !index.roles.has(deviceId)) return null
  if (deviceId === hub) return [{ id: hub, lqi: null }]

  const route = [{ id: deviceId, lqi: null }]
  let start = deviceId
  if (index.roles.get(deviceId) === 'end') {
    const parent = linksOf(zigbee, deviceId, index).find((l) => l.relationship === 'parent' && index.roles.get(l.id) !== 'end')
    if (!parent) return null
    route.push({ id: parent.id, lqi: parent.lqi })
    start = parent.id
    if (start === hub) return route
  }

  // Widest path over routers and the hub: maximise the weakest link, then the fewest hops.
  const best = new Map([[start, { width: Infinity, hops: 0, previous: null, lqi: null }]])
  const done = new Set()
  for (;;) {
    let current = null
    for (const [id, state] of best) {
      if (done.has(id)) continue
      if (!current || state.width > best.get(current).width || (state.width === best.get(current).width && state.hops < best.get(current).hops)) current = id
    }
    if (!current || current === hub) break
    done.add(current)
    const here = best.get(current)
    for (const link of index.links.get(current)?.values() ?? []) {
      if (index.roles.get(link.id) === 'end' || done.has(link.id)) continue
      const width = Math.min(here.width, link.lqi)
      const known = best.get(link.id)
      if (!known || width > known.width || (width === known.width && here.hops + 1 < known.hops)) {
        best.set(link.id, { width, hops: here.hops + 1, previous: current, lqi: link.lqi })
      }
    }
  }
  if (!best.has(hub)) return null
  const tail = []
  for (let id = hub; id !== start; id = best.get(id).previous) tail.unshift({ id, lqi: best.get(id).lqi })
  return [...route, ...tail]
}

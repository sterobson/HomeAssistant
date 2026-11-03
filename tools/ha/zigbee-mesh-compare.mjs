// Read-only: compares two zigbee-mesh.cjs --json dumps. For each device, the weakest link on its best path to the hub
// (the same route calculation the 3D view uses), before and after.
// Usage: node mesh-compare.mjs <old.json> <new.json>
import { readFileSync } from 'node:fs'
import { meshIndex, routeToHub } from '../../Frontend/src/utils/zigbee.js'

const load = (path) => {
  const raw = readFileSync(path, 'utf8').replace(/^\uFEFF/, '')
  const devices = JSON.parse(raw)
  const zigbee = {
    nodes: devices.map((d) => ({ deviceId: d.ieee, role: d.type === 'Coordinator' ? 'coordinator' : d.type === 'Router' ? 'router' : 'end', available: d.available })),
    links: devices.flatMap((d) => (d.neighbors ?? []).map((n) => ({ from: d.ieee, to: n.ieee, lqi: n.lqi, relationship: (n.relationship ?? '').toLowerCase() }))),
  }
  return { devices, zigbee, index: meshIndex(zigbee), names: new Map(devices.map((d) => [d.ieee, d.name])) }
}
const [oldMesh, newMesh] = process.argv.slice(2).map(load)

const summary = (mesh, device) => {
  const route = routeToHub(mesh.zigbee, device.ieee, mesh.index)
  if (!route) return { weakest: null, hops: null, path: 'no route' }
  const links = route.slice(1).map((s) => s.lqi)
  return { weakest: links.length ? Math.min(...links) : null, hops: links.length, path: route.slice(1).map((s) => `${mesh.names.get(s.id)} ${s.lqi}`).join(' > ') }
}

const byName = new Map(oldMesh.devices.map((d) => [d.ieee, d]))
const rows = newMesh.devices.filter((d) => d.type !== 'Coordinator').map((d) => {
  const now = summary(newMesh, d)
  const was = byName.has(d.ieee) ? summary(oldMesh, byName.get(d.ieee)) : null
  return { area: d.area ?? '(none)', name: d.name, type: d.type, available: d.available, lastSeen: d.lastSeen, was: was?.weakest ?? null, isNew: !was, now: now.weakest, hops: now.hops, path: now.path }
}).sort((a, b) => a.area.localeCompare(b.area) || a.name.localeCompare(b.name))

for (const r of rows) {
  const change = r.isNew ? 'new' : r.was == null || r.now == null ? '?' : (r.now - r.was >= 0 ? '+' : '') + (r.now - r.was)
  console.log(`${r.area.padEnd(13)} ${r.name.slice(0, 38).padEnd(38)} ${String(r.was ?? '-').padStart(4)} -> ${String(r.now ?? '-').padStart(4)} ${change.padStart(5)}  ${r.available ? '' : `[OFFLINE since ${r.lastSeen}] `}${r.path}`)
}
const both = rows.filter((r) => r.was != null && r.now != null)
const avg = (key) => Math.round(both.reduce((s, r) => s + r[key], 0) / both.length)
console.log(`\ndevices in both: ${both.length}, mean weakest link ${avg('was')} -> ${avg('now')}; better ${both.filter((r) => r.now > r.was).length}, worse ${both.filter((r) => r.now < r.was).length}`)
const den = both.filter((r) => r.area === 'Games Room')
console.log(`den devices in both: ${den.length}, mean weakest link ${Math.round(den.reduce((s, r) => s + r.was, 0) / den.length)} -> ${Math.round(den.reduce((s, r) => s + r.now, 0) / den.length)}`)
const missing = oldMesh.devices.filter((d) => !newMesh.devices.some((n) => n.ieee === d.ieee)).map((d) => d.name)
console.log('gone since:', missing.join(', ') || 'none')

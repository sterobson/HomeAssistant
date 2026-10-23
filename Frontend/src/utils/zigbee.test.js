import { describe, it, expect } from 'vitest'
import { lqiBand, linksOf, uniqueLinks, routeToHub } from './zigbee.js'

// A small mesh shaped like the house's: the den's plugs hear each other well but the hub badly, and get out through
// the kitchen heater plug; the den thermometer hangs off the games room heater plug.
const mesh = {
  nodes: [
    { deviceId: 'hub', role: 'coordinator' },
    { deviceId: 'kitchen', role: 'router' },
    { deviceId: 'denHeater', role: 'router' },
    { deviceId: 'denTv', role: 'router' },
    { deviceId: 'denClimate', role: 'end' },
  ],
  links: [
    { from: 'hub', to: 'kitchen', lqi: 98, relationship: 'child' },
    { from: 'kitchen', to: 'hub', lqi: 120, relationship: 'parent' },
    { from: 'hub', to: 'denHeater', lqi: 46, relationship: 'sibling' },
    { from: 'kitchen', to: 'denHeater', lqi: 104, relationship: 'sibling' },
    { from: 'denHeater', to: 'kitchen', lqi: 110, relationship: 'sibling' },
    { from: 'denHeater', to: 'denTv', lqi: 140, relationship: 'sibling' },
    { from: 'denTv', to: 'denHeater', lqi: 138, relationship: 'sibling' },
    { from: 'denHeater', to: 'denClimate', lqi: 129, relationship: 'child' },
  ],
}

describe('the Zigbee mesh', () => {
  it('bands LQI like the mesh script does', () => {
    expect([lqiBand(200), lqiBand(150), lqiBand(149), lqiBand(100), lqiBand(99)]).toEqual(['strong', 'strong', 'fair', 'fair', 'weak'])
  })

  it('takes a link as strong as its weaker direction, strongest links first', () => {
    const links = linksOf(mesh, 'denHeater')
    expect(links.map((l) => [l.id, l.lqi])).toEqual([['denTv', 138], ['denClimate', 129], ['kitchen', 104], ['hub', 46]])
    expect(links.find((l) => l.id === 'denClimate').relationship).toBe('child')
  })

  it('lists each pair once', () => {
    expect(uniqueLinks(mesh)).toHaveLength(5)
  })

  it('routes a den plug through the kitchen rather than straight to a hub it hears badly', () => {
    expect(routeToHub(mesh, 'denTv')).toEqual([
      { id: 'denTv', lqi: null },
      { id: 'denHeater', lqi: 138 },
      { id: 'kitchen', lqi: 104 },
      { id: 'hub', lqi: 98 },
    ])
  })

  it('routes a battery device through its parent first', () => {
    expect(routeToHub(mesh, 'denClimate').map((s) => s.id)).toEqual(['denClimate', 'denHeater', 'kitchen', 'hub'])
  })

  it('has no route for a device the mesh does not know, and the hub is its own route', () => {
    expect(routeToHub(mesh, 'unknown')).toBeNull()
    expect(routeToHub(mesh, 'hub')).toEqual([{ id: 'hub', lqi: null }])
    expect(routeToHub({ nodes: [], links: [] }, 'denTv')).toBeNull()
  })
})

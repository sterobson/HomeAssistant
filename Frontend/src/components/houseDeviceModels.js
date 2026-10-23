// What each device looks like in the 3D house (owner, 2026-10-22): smart plugs as white UK sockets, buttons as white
// push buttons, thermometers white with an LCD showing their reading. Their state shows on a small LED; kinds with no
// model of their own stay simple shapes coloured by state. Sizes are real (metres), facing +z; HouseScene scales and
// turns them.
import * as THREE from 'three'

const WHITE = '#f6f6f3'

const mat = (colour, extra = {}) => new THREE.MeshStandardMaterial({ color: colour, roughness: 0.45, ...extra })

function box(w, h, d, material, x = 0, y = 0, z = 0) {
  const mesh = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), material)
  mesh.position.set(x, y, z)
  mesh.castShadow = true
  return mesh
}

function led(x, y, z) {
  const material = mat('#555555', { emissive: '#000000', roughness: 0.2 })
  const mesh = new THREE.Mesh(new THREE.SphereGeometry(0.0045, 12, 8), material)
  mesh.position.set(x, y, z)
  return { mesh, material }
}

/** A UK double-pole socket face: earth slot above, live and neutral below, a rocker switch, and the plug's LED. */
function socket() {
  const group = new THREE.Group()
  group.add(box(0.086, 0.086, 0.012, mat(WHITE, { roughness: 0.35 }), 0, 0, 0.006))
  const slot = mat('#1d1f22', { roughness: 0.8 })
  group.add(box(0.005, 0.009, 0.002, slot, -0.01, 0.017, 0.0125)) // earth
  group.add(box(0.009, 0.004, 0.002, slot, -0.024, -0.008, 0.0125)) // live
  group.add(box(0.009, 0.004, 0.002, slot, 0.004, -0.008, 0.0125)) // neutral
  group.add(box(0.014, 0.022, 0.006, mat('#ececea', { roughness: 0.4 }), 0.027, 0.004, 0.014)) // switch
  const light = led(0.027, -0.024, 0.0135)
  group.add(light.mesh)
  return { object: group, tint: light.material, led: true }
}

/** A round push button lying flat, as the SNZB-01P sits on a desk or shelf. */
function pushButton() {
  const group = new THREE.Group()
  const shell = new THREE.Mesh(new THREE.CylinderGeometry(0.024, 0.026, 0.012, 32), mat(WHITE, { roughness: 0.3 }))
  shell.castShadow = true
  group.add(shell)
  const cap = new THREE.Mesh(new THREE.CylinderGeometry(0.017, 0.017, 0.004, 32), mat('#ffffff', { roughness: 0.25 }))
  cap.position.y = 0.008
  group.add(cap)
  const light = led(0, 0.007, 0.021)
  group.add(light.mesh)
  return { object: group, tint: light.material, led: true }
}

function drawLcd(canvas, temperature, humidity) {
  const g = canvas.getContext('2d')
  g.fillStyle = '#c7d1c1'
  g.fillRect(0, 0, canvas.width, canvas.height)
  g.fillStyle = '#1e2620'
  g.textAlign = 'center'
  g.textBaseline = 'middle'
  g.font = 'bold 54px "Segoe UI", system-ui, sans-serif'
  g.fillText(temperature ?? '--.-', canvas.width / 2 - 6, canvas.height * 0.4)
  g.font = '26px "Segoe UI", system-ui, sans-serif'
  g.fillText('°C', canvas.width - 22, canvas.height * 0.28)
  g.fillText(humidity == null ? '--%' : `${humidity}%`, canvas.width / 2, canvas.height * 0.8)
}

/** A square thermometer with an LCD face, as the SNZB-02D, showing the device's own temperature and humidity. */
function thermometer() {
  const group = new THREE.Group()
  group.add(box(0.062, 0.062, 0.02, mat(WHITE, { roughness: 0.35 }), 0, 0, 0))
  const canvas = document.createElement('canvas')
  canvas.width = 160
  canvas.height = 128
  drawLcd(canvas)
  const texture = new THREE.CanvasTexture(canvas)
  texture.colorSpace = THREE.SRGBColorSpace
  const screen = new THREE.Mesh(new THREE.PlaneGeometry(0.05, 0.04), new THREE.MeshBasicMaterial({ map: texture }))
  screen.position.set(0, 0.003, 0.0102)
  group.add(screen)
  const light = led(0.022, -0.024, 0.0105)
  group.add(light.mesh)
  let shown = ''
  const update = (device) => {
    const reading = (deviceClass) => {
      const entity = (device?.entities ?? []).find((e) => e.deviceClass === deviceClass)
      const value = Number(entity?.state)
      return entity && Number.isFinite(value) ? value : null
    }
    const temperature = reading('temperature')
    const humidity = reading('humidity')
    const text = `${temperature?.toFixed(1)}|${humidity?.toFixed(0)}`
    if (text === shown) return
    shown = text
    drawLcd(canvas, temperature?.toFixed(1), humidity?.toFixed(0))
    texture.needsUpdate = true
  }
  return { object: group, tint: light.material, led: true, update }
}

function simple(geometry) {
  const material = mat('#42a5f5', { roughness: 0.45, metalness: 0.05 })
  const mesh = new THREE.Mesh(geometry, material)
  mesh.castShadow = true
  return { object: mesh, tint: material, led: false }
}

/**
 * The model for a device kind: { object, tint (the material its state colours), led (whether that is a small LED
 * rather than the whole thing), update(device) when it shows a reading }.
 */
export function buildDeviceModel(kind) {
  switch (kind) {
    case 'plug': return socket()
    case 'button': return pushButton()
    case 'thermometer': return thermometer()
    case 'light': return simple(new THREE.SphereGeometry(0.11, 24, 16))
    // A radiator valve: the radiator itself is drawn with the furniture.
    case 'radiator': return simple(new THREE.CylinderGeometry(0.035, 0.035, 0.11, 20))
    case 'flood': return simple(new THREE.CylinderGeometry(0.035, 0.035, 0.02, 24))
    case 'motion': return simple(new THREE.SphereGeometry(0.04, 20, 12, 0, Math.PI * 2, 0, Math.PI / 2))
    case 'hub': return simple(new THREE.BoxGeometry(0.11, 0.035, 0.11))
    case 'fingerbot': return simple(new THREE.BoxGeometry(0.035, 0.05, 0.035))
    case 'tv': return simple(new THREE.BoxGeometry(0.7, 0.4, 0.04))
    case 'speaker': return simple(new THREE.CylinderGeometry(0.05, 0.05, 0.1, 20))
    case 'car': return simple(new THREE.BoxGeometry(0.55, 0.22, 0.26))
    case 'phone': return simple(new THREE.BoxGeometry(0.035, 0.07, 0.008))
    default: return simple(new THREE.OctahedronGeometry(0.06))
  }
}

/** Kinds that hang on a wall, so HouseScene turns them to face into the room. */
export const WALL_KINDS = new Set(['plug', 'thermometer', 'motion'])

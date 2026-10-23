// Three.js builders for the 3D house's fittings: laminate floors, doors, windows, radiators and the furniture listed
// in utils/houseModel.js (FURNITURE). Simple boxes and cylinders in real-world colours, so the model reads as a house
// rather than a diagram; positions come in metres from HouseScene.
import * as THREE from 'three'
import { buildFigure } from './houseFigures.js'

const COLOURS = {
  wood: '#a77b52',
  woodDark: '#7a5537',
  // Internal doors are white (owner, 2026-10-22), with four panels.
  doorPaint: '#f5f4f0',
  doorPanel: '#e8e6e0',
  externalDoor: '#f1efe9',
  chair: '#56636f',
  carcass: '#f1f0ec',
  appliance: '#f2f2f0',
  steel: '#b8bcc2',
  black: '#1b1d20',
  cabinet: '#4a4f56',
  radiator: '#f6f6f3',
  pole: '#d8d4cc',
  glass: '#9ecbe6',
}

/** Upholstery colours FURNITURE can name. */
export const FABRICS = {
  // DFS "duck egg": a pale blue-green grey (owner's den sofa, a DFS Euphoria).
  duckEgg: '#a9c4c1',
  // The living room sofas, "a purply red" (owner).
  plum: '#8a2f52',
  default: '#6c7c8c',
}

const GLOW = { light: '#ffd27a', heater: '#ff7a2f' }

/** What arrange mode calls each kind of furniture (by type, or a tall unit's label). */
export const FURNITURE_NAMES = {
  desk: 'Desk', table: 'Table', chair: 'Chair', sofa: 'Sofa', counter: 'Worktop', sink: 'Sink', hob: 'Hob',
  oven: 'Oven', fridge: 'Fridge', tall: 'Tall unit', bar: 'Breakfast bar', heater: 'Heater', boiler: 'Boiler',
  toilet: 'Toilet', cabinet: 'TV cabinet', tv: 'TV', stickLight: 'Stick light', radiator: 'Radiator',
  monitor: 'Monitor', keyboard: 'Keyboard', mouse: 'Mouse', printer: 'Printer', person: 'Person',
}

const DESK_TOP = 0.76

let heatTexture = null

/**
 * The shimmer of heat from a heater that is on (owner, 2026-10-22): a cone opening away from its grille, warm at the
 * heater and fading to nothing, with bands HouseScene drifts outwards. Hidden until the heater's plug is on.
 */
function heatCone(direction, origin) {
  heatTexture ??= canvasTexture(128, (g, size) => {
    for (let y = 0; y < size; y++) {
      const fromHeater = y / size // 0 at the top of the canvas, the cone's tip
      const fade = Math.pow(1 - fromHeater, 1.6)
      const band = 0.65 + 0.35 * Math.sin(fromHeater * Math.PI * 10)
      g.fillStyle = `rgba(255, ${Math.round(120 + 80 * fromHeater)}, 40, ${(0.85 * fade * band).toFixed(3)})`
      g.fillRect(0, y, size, 1)
    }
  })
  const length = 1.1
  const material = new THREE.MeshBasicMaterial({
    map: heatTexture.clone(), transparent: true, opacity: 0.55, depthWrite: false, side: THREE.DoubleSide,
    blending: THREE.AdditiveBlending,
  })
  material.map.needsUpdate = true
  const cone = new THREE.Mesh(new THREE.ConeGeometry(0.45, length, 32, 1, true), material)
  // The cone's tip (+y) points back at the heater, its open end away from it.
  cone.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), direction.clone().negate())
  cone.position.copy(origin).addScaledVector(direction, length / 2)
  cone.visible = false
  cone.renderOrder = 5
  cone.userData.heat = true
  return cone
}

/**
 * The living room's stick lights (owner, 2026-10-22: smaller, like a tree with no leaves): a dark trunk splitting
 * into bare twigs, with a tiny warm bulb at each tip that lights when the plug is on. Seeded by the item's id so
 * each tree keeps its shape between redraws.
 */
function twigTree(id, glow) {
  const group = new THREE.Group()
  let seed = [...String(id)].reduce((sum, c) => sum * 31 + c.charCodeAt(0), 7) % 2147483647 || 1
  const random = () => (seed = (seed * 16807) % 2147483647) / 2147483647
  const bark = material('#3a2f29', { roughness: 0.9 })
  const bulb = material('#f1e6cc', { emissive: GLOW.light, emissiveIntensity: 0, roughness: 0.3 })
  glow.push({ material: bulb, colour: GLOW.light, intensity: 2.6 })
  group.add(cylinder(0.06, 0.02, material(COLOURS.black), 0, 0.01, 0))
  const up = new THREE.Vector3(0, 1, 0)
  const branch = (from, direction, length, radius, depth) => {
    const to = from.clone().addScaledVector(direction, length)
    const twig = new THREE.Mesh(new THREE.CylinderGeometry(radius * 0.7, radius, length, 6), bark)
    twig.position.copy(from).addScaledVector(direction, length / 2)
    twig.quaternion.setFromUnitVectors(up, direction)
    twig.castShadow = true
    group.add(twig)
    if (depth === 0) {
      const tip = new THREE.Mesh(new THREE.SphereGeometry(0.008, 8, 6), bulb)
      tip.position.copy(to)
      group.add(tip)
      return
    }
    const children = depth === 3 ? 3 : 2 + Math.round(random())
    for (let i = 0; i < children; i++) {
      const spin = (i / children) * Math.PI * 2 + random() * 0.8
      const lean = 0.35 + random() * 0.45
      const next = direction.clone().applyAxisAngle(new THREE.Vector3(Math.cos(spin), 0, Math.sin(spin)), lean).normalize()
      next.y = Math.max(next.y, 0.25)
      branch(to, next.normalize(), length * (0.62 + random() * 0.15), radius * 0.62, depth - 1)
    }
  }
  branch(new THREE.Vector3(0, 0.02, 0), up.clone(), 0.32, 0.012, 3)
  return group
}

/** A 5-star office chair: castors' legs, gas lift, seat and a backrest on the side away from the desk. */
function swivelChair(w, d, back) {
  const group = new THREE.Group()
  const frame = material(COLOURS.black, { roughness: 0.4, metalness: 0.3 })
  const fabric = material('#2e3238', { roughness: 0.9 })
  for (let i = 0; i < 5; i++) {
    const leg = box(0.3, 0.03, 0.04, frame, 0, 0.06, 0)
    leg.geometry.translate(0.15, 0, 0)
    leg.rotation.y = (i * 2 * Math.PI) / 5
    group.add(leg)
    const angle = (i * 2 * Math.PI) / 5
    group.add(cylinder(0.025, 0.04, frame, Math.cos(angle) * 0.29, 0.02, -Math.sin(angle) * 0.29))
  }
  group.add(cylinder(0.025, 0.36, frame, 0, 0.26, 0))
  group.add(box(0.5, 0.08, 0.48, fabric, 0, 0.48, 0))
  const towards = back.axis === 'z' ? [0, 0.22 * back.sign] : [0.22 * back.sign, 0]
  group.add(box(back.axis === 'z' ? 0.46 : 0.05, 0.55, back.axis === 'z' ? 0.05 : 0.46, fabric, towards[0], 0.88, towards[1]))
  group.add(box(back.axis === 'z' ? 0.06 : 0.04, 0.18, back.axis === 'z' ? 0.04 : 0.06, frame, towards[0] * 0.9, 0.58, towards[1] * 0.9))
  return group
}

/** A monitor on a stand on the desk: `inches` diagonal, `aspect` like "32:10" (16:9 unless said). */
function monitor(item, w, d, back) {
  const group = new THREE.Group()
  const [ax, ay] = (item.aspect ?? '16:9').split(':').map(Number)
  const diagonal = (item.inches ?? 24) * 0.0254
  const width = diagonal * Math.cos(Math.atan(ay / ax))
  const height = diagonal * Math.sin(Math.atan(ay / ax))
  const alongX = w >= d
  const shell = material(COLOURS.black, { roughness: 0.35, metalness: 0.2 })
  const screen = material('#0d1b2a', { roughness: 0.15, metalness: 0.1, emissive: '#16324f', emissiveIntensity: 0.35 })
  const bottom = DESK_TOP + 0.1
  group.add(box(alongX ? width : 0.025, height, alongX ? 0.025 : width, shell, 0, bottom + height / 2, 0))
  const [fx, fz] = back.axis === 'z' ? [0, -0.0135 * back.sign] : [-0.0135 * back.sign, 0]
  group.add(box(alongX ? width - 0.02 : 0.002, height - 0.02, alongX ? 0.002 : width - 0.02, screen, fx, bottom + height / 2, fz))
  const [sx, sz] = back.axis === 'z' ? [0, 0.06 * back.sign] : [0.06 * back.sign, 0]
  group.add(box(0.05, bottom - DESK_TOP + height / 3, 0.03, shell, sx, DESK_TOP + (bottom - DESK_TOP + height / 3) / 2, sz))
  group.add(box(alongX ? 0.24 : 0.18, 0.012, alongX ? 0.18 : 0.24, shell, sx, DESK_TOP + 0.006, sz))
  return group
}

// Textures are drawn once and shared; they live as long as the page.
let marble = null
let weave = null

const material = (colour, extra = {}) => new THREE.MeshStandardMaterial({ color: colour, roughness: 0.75, ...extra })

function shade(mesh) {
  mesh.castShadow = true
  mesh.receiveShadow = true
  return mesh
}

function box(w, h, d, mat, x, y, z) {
  const mesh = shade(new THREE.Mesh(new THREE.BoxGeometry(w, h, d), mat))
  mesh.position.set(x, y, z)
  return mesh
}

function cylinder(r, h, mat, x, y, z) {
  const mesh = shade(new THREE.Mesh(new THREE.CylinderGeometry(r, r, h, 20), mat))
  mesh.position.set(x, y, z)
  return mesh
}

function canvasTexture(size, draw) {
  const canvas = document.createElement('canvas')
  canvas.width = size
  canvas.height = size
  draw(canvas.getContext('2d'), size)
  const texture = new THREE.CanvasTexture(canvas)
  texture.wrapS = THREE.RepeatWrapping
  texture.wrapT = THREE.RepeatWrapping
  texture.colorSpace = THREE.SRGBColorSpace
  texture.anisotropy = 4
  return texture
}

function seeded(seed) {
  let s = seed
  return () => (s = (s * 16807) % 2147483647) / 2147483647
}

/** Planks about 20 cm wide and up to 1.3 m long, running front to back; one tile covers TILE_METRES square. */
export const LAMINATE_TILE_METRES = 1.6

export function laminateTexture() {
  const texture = canvasTexture(512, (g) => {
    const random = seeded(7)
    const rows = 8
    const plank = 512 / rows
    for (let row = 0; row < rows; row++) {
      let x = -random() * 300
      while (x < 512) {
        const length = 220 + random() * 200
        g.fillStyle = `hsl(32, 36%, ${60 + random() * 9}%)`
        g.fillRect(x, row * plank, length, plank)
        g.strokeStyle = 'rgba(90, 60, 30, 0.09)'
        g.lineWidth = 1
        for (let i = 0; i < 7; i++) {
          const y = row * plank + random() * plank
          g.beginPath()
          g.moveTo(x, y)
          g.bezierCurveTo(x + length / 3, y + random() * 6 - 3, x + (2 * length) / 3, y + random() * 6 - 3, x + length, y + random() * 4 - 2)
          g.stroke()
        }
        g.fillStyle = 'rgba(60, 40, 20, 0.35)'
        g.fillRect(x, row * plank, 1.5, plank)
        x += length
      }
      g.fillStyle = 'rgba(60, 40, 20, 0.3)'
      g.fillRect(0, row * plank, 512, 1.5)
    }
  })
  // The planks are drawn running across; the house's run front to back (owner).
  texture.rotation = Math.PI / 2
  return texture
}

/** White marble with soft grey veins, for the kitchen worktops and breakfast bar. */
function marbleTexture() {
  marble ??= canvasTexture(512, (g, size) => {
    const random = seeded(31)
    g.fillStyle = '#f4f3f0'
    g.fillRect(0, 0, size, size)
    for (let i = 0; i < 4000; i++) {
      g.fillStyle = `rgba(200, 200, 205, ${random() * 0.08})`
      g.fillRect(random() * size, random() * size, 3, 3)
    }
    for (let vein = 0; vein < 9; vein++) {
      g.strokeStyle = `rgba(140, 142, 150, ${0.15 + random() * 0.3})`
      g.lineWidth = 0.6 + random() * 1.8
      let x = random() * size
      let y = 0
      g.beginPath()
      g.moveTo(x, y)
      while (y < size) {
        x += (random() - 0.4) * 40
        y += 20 + random() * 30
        g.lineTo(x, y)
      }
      g.stroke()
    }
  })
  return marble
}

/** A chunky basket weave, tinted by the fabric colour. */
function weaveTexture() {
  weave ??= canvasTexture(128, (g, size) => {
    const random = seeded(11)
    g.fillStyle = '#ffffff'
    g.fillRect(0, 0, size, size)
    const cell = 8
    for (let y = 0; y < size; y += cell) {
      for (let x = 0; x < size; x += cell) {
        const across = ((x + y) / cell) % 2 === 0
        for (let i = 0; i < 3; i++) {
          const tone = 205 + random() * 50
          g.fillStyle = `rgb(${tone}, ${tone}, ${tone})`
          if (across) g.fillRect(x, y + i * (cell / 3), cell, cell / 3 - 0.5)
          else g.fillRect(x + i * (cell / 3), y, cell / 3 - 0.5, cell)
        }
      }
    }
  })
  return weave
}

function fabric(colour) {
  const map = weaveTexture()
  return new THREE.MeshStandardMaterial({ color: colour, map, roughness: 0.95 })
}

/** Scales a box's texture coordinates so a shared repeating texture keeps its real size on any floor. */
export function scaleUvs(geometry, width, depth, tile = LAMINATE_TILE_METRES) {
  const uv = geometry.attributes.uv
  for (let i = 0; i < uv.count; i++) {
    uv.setXY(i, (uv.getX(i) * width) / tile, (uv.getY(i) * depth) / tile)
  }
  uv.needsUpdate = true
}

/**
 * A door leaf, 2 m high, `width` wide, lying along x with its hinge edge at x = 0. Internal doors are wood with four
 * panels on each face; outside doors plain and painted.
 */
function leaf(width, external) {
  const group = new THREE.Group()
  if (external) {
    group.add(box(width, 2.0, 0.045, material(COLOURS.externalDoor, { roughness: 0.5 }), width / 2, 1.0, 0))
    group.add(box(0.12, 0.03, 0.06, material(COLOURS.steel, { metalness: 0.7 }), width - 0.1, 1.0, 0))
    return group
  }
  group.add(box(width, 2.0, 0.04, material(COLOURS.doorPaint, { roughness: 0.45 }), width / 2, 1.0, 0))
  const panel = material(COLOURS.doorPanel, { roughness: 0.5 })
  const panelWidth = (width - 0.3) / 2
  for (const side of [-1, 1]) {
    for (const column of [0, 1]) {
      for (const [bottom, height] of [[0.15, 0.75], [1.05, 0.75]]) {
        const x = 0.1 + panelWidth / 2 + column * (panelWidth + 0.1)
        group.add(box(panelWidth, height, 0.012, panel, x, bottom + height / 2, side * 0.024))
      }
    }
  }
  group.add(cylinder(0.012, 0.11, material(COLOURS.steel, { metalness: 0.7 }), width - 0.08, 1.0, 0.045).rotateX(Math.PI / 2))
  return group
}

/**
 * A door in an opening `width` wide on a wall running along x ('h') or z ('v') from (startX, startZ) to (endX, endZ),
 * hung from its start, or its end when `hinge` is 'end': standing open about 60° towards +z ('h') or +x ('v') when
 * swing is 1, or for a sliding door, pushed most of the way past the opening, on that side of the wall.
 */
export function door(opening, axis, width, startX, startZ, endX, endZ) {
  const pivot = new THREE.Group()
  const hingeEnd = opening.hinge === 'end'
  pivot.position.set(hingeEnd ? endX : startX, 0, hingeEnd ? endZ : startZ)
  // Hung from the end, the leaf points back along the wall, which also turns its sides round.
  const swing = (opening.swing ?? 1) * (hingeEnd ? -1 : 1)
  const door = leaf(width, opening.external)
  pivot.add(door)
  // The leaf is built along +x; turn it to run along the wall, away from the hinge.
  if (axis === 'v') pivot.rotation.y = hingeEnd ? Math.PI / 2 : -Math.PI / 2
  else if (hingeEnd) pivot.rotation.y = Math.PI
  if (opening.sliding) {
    door.position.set(width * 0.75, 0, axis === 'h' ? 0.07 * swing : -0.07 * swing)
  } else {
    const angle = Math.PI / 3
    door.rotation.y = axis === 'h' ? -angle * swing : angle * swing
  }
  return pivot
}

export function windowPane(axis, width, x, z, sill, top) {
  const pane = new THREE.Mesh(
    new THREE.BoxGeometry(axis === 'h' ? width : 0.03, top - sill, axis === 'h' ? 0.03 : width),
    material(COLOURS.glass, { transparent: true, opacity: 0.32, roughness: 0.1, metalness: 0.1, depthWrite: false })
  )
  pane.position.set(x, (sill + top) / 2, z)
  pane.renderOrder = 3
  return pane
}

// Which edge of an item's footprint is its back (a chair's or sofa's backrest, a toilet's cistern, the side away
// from a screen or a heater's grille), from the way it faces.
function backEdge(facing) {
  switch (facing) {
    case 'back': return { axis: 'z', sign: 1 }
    case 'front': return { axis: 'z', sign: -1 }
    case 'left': return { axis: 'x', sign: 1 }
    default: return { axis: 'x', sign: -1 }
  }
}

function legs(group, w, d, height, mat, inset = 0.05, size = 0.02, square = false) {
  for (const [sx, sz] of [[-1, -1], [1, -1], [-1, 1], [1, 1]]) {
    const x = sx * (w / 2 - inset)
    const z = sz * (d / 2 - inset)
    group.add(square ? box(size * 2, height, size * 2, mat, x, height / 2, z) : cylinder(size, height, mat, x, height / 2, z))
  }
}

/**
 * A sofa piece facing `facing`. `noArm` leaves out the arm at that end ('front', 'back', 'left' or 'right'), so two
 * pieces make a corner sofa. The Euphoria style has square arms with soft pads on top, separate seat and back
 * cushions and dark square feet.
 */
function sofa(item, w, d) {
  const group = new THREE.Group()
  const back = backEdge(item.facing)
  const along = back.axis === 'z' ? w : d // the sofa's length
  const deep = back.axis === 'z' ? d : w // front to back
  const cover = fabric(FABRICS[item.colour] ?? FABRICS.default)
  const euphoria = item.style === 'euphoria'
  const feet = euphoria ? 0.1 : 0.05
  // Positions in the sofa's own terms, u along its length and v from its back (negative) to its front.
  const place = (mesh, u, v) => {
    if (back.axis === 'z') mesh.position.set(u, mesh.position.y, -v * back.sign)
    else mesh.position.set(-v * back.sign, mesh.position.y, u)
    return mesh
  }
  const block = (length, height, depth, y, u, v, mat = cover) =>
    place(box(back.axis === 'z' ? length : depth, height, back.axis === 'z' ? depth : length, mat, 0, y, 0), u, v)

  if (euphoria) legs(group, w, d, feet, material(COLOURS.black), 0.06, 0.025, true)
  group.add(block(along, 0.22, deep, feet + 0.11, 0, 0))
  const armWidth = euphoria ? 0.2 : 0.16
  const ends = []
  for (const end of [-1, 1]) {
    const name = back.axis === 'z' ? (end < 0 ? 'left' : 'right') : (end < 0 ? 'back' : 'front')
    if (item.noArm !== name) ends.push(end)
  }
  const inner = along - ends.length * armWidth
  const innerStart = -along / 2 + (ends.includes(-1) ? armWidth : 0)
  // Back cushions and seat cushions, about 70 cm each.
  const count = Math.max(1, Math.round(inner / 0.7))
  const step = inner / count
  for (let i = 0; i < count; i++) {
    const u = innerStart + step * (i + 0.5)
    group.add(block(step - 0.02, 0.16, deep - 0.28, feet + 0.3, u, 0.1))
    group.add(block(step - 0.03, 0.42, 0.2, feet + 0.55, u, -deep / 2 + 0.17))
  }
  group.add(block(along, 0.36, 0.12, feet + 0.4, 0, -deep / 2 + 0.06))
  for (const end of ends) {
    const u = end * (along / 2 - armWidth / 2)
    group.add(block(armWidth, euphoria ? 0.46 : 0.42, deep, feet + (euphoria ? 0.23 : 0.21), u, 0))
    if (euphoria) {
      const pad = place(shade(new THREE.Mesh(new THREE.CapsuleGeometry(0.09, deep - 0.3, 6, 12), cover)), u, 0)
      pad.rotation.set(back.axis === 'z' ? Math.PI / 2 : 0, 0, back.axis === 'z' ? 0 : Math.PI / 2)
      pad.position.y = feet + 0.52
      group.add(pad)
    }
  }
  return group
}

/** A double-panel radiator: a run of vertical fins between top and bottom rails, on two short pipes. */
function radiator(w, d) {
  const group = new THREE.Group()
  const long = Math.max(w, d)
  const alongX = w >= d
  const paint = material(COLOURS.radiator, { roughness: 0.4 })
  const bottom = 0.15
  const height = 0.6
  const fins = Math.max(6, Math.round(long / 0.05))
  for (let i = 0; i < fins; i++) {
    const offset = -long / 2 + (long * (i + 0.5)) / fins
    const fin = alongX ? box(0.03, height, 0.09, paint, offset, bottom + height / 2, 0) : box(0.09, height, 0.03, paint, 0, bottom + height / 2, offset)
    group.add(fin)
  }
  for (const y of [bottom + 0.02, bottom + height - 0.02]) {
    group.add(alongX ? box(long, 0.04, 0.1, paint, 0, y, 0) : box(0.1, 0.04, long, paint, 0, y, 0))
  }
  for (const end of [-1, 1]) {
    const offset = end * (long / 2 - 0.04)
    group.add(alongX ? cylinder(0.012, bottom, paint, offset, bottom / 2, 0) : cylinder(0.012, bottom, paint, 0, bottom / 2, offset))
  }
  return group
}

/**
 * One FURNITURE item, centred on its footprint. w and d are its footprint in metres along x and z. Returns the
 * group and the materials that glow when its `litBy` device is on.
 */
export function buildFurniture(item, w, d) {
  const group = new THREE.Group()
  const glow = []
  const back = backEdge(item.facing)
  const along = (offset) => (back.axis === 'z' ? [0, offset * back.sign] : [offset * back.sign, 0])

  switch (item.type) {
    case 'desk':
    case 'table': {
      const top = item.type === 'desk' ? 0.74 : 0.75
      group.add(box(w, 0.04, d, material(COLOURS.wood, { roughness: 0.55 }), 0, top, 0))
      legs(group, w, d, top - 0.02, material(COLOURS.woodDark))
      break
    }
    case 'chair': {
      if (item.style === 'swivel') {
        group.add(swivelChair(w, d, back))
        break
      }
      const seat = material(COLOURS.chair)
      group.add(box(w * 0.85, 0.05, d * 0.85, seat, 0, 0.46, 0))
      legs(group, w * 0.85, d * 0.85, 0.44, material(COLOURS.black), 0.03)
      const [bx, bz] = along((back.axis === 'z' ? d : w) * 0.4)
      group.add(box(back.axis === 'z' ? w * 0.85 : 0.04, 0.45, back.axis === 'z' ? 0.04 : d * 0.85, seat, bx, 0.7, bz))
      break
    }
    case 'sofa':
      group.add(sofa(item, w, d))
      break
    case 'counter':
    case 'bar': {
      const top = item.type === 'bar' ? 1.02 : 0.88
      const stone = new THREE.MeshStandardMaterial({ map: marbleTexture(), roughness: 0.25, metalness: 0.02 })
      const slab = new THREE.BoxGeometry(w + 0.02, 0.04, d + 0.02)
      scaleUvs(slab, w, d, 1.2)
      const worktop = shade(new THREE.Mesh(slab, stone))
      worktop.position.y = top
      group.add(worktop)
      if (item.type === 'bar') group.add(box(w * 0.96, top, 0.06, material(COLOURS.carcass), 0, top / 2, -d / 2 + 0.05))
      else group.add(box(w, top - 0.02, d, material(COLOURS.carcass), 0, (top - 0.02) / 2, 0))
      break
    }
    case 'sink':
      group.add(box(w * 0.85, 0.02, d * 0.7, material('#8a8f95', { metalness: 0.6, roughness: 0.3 }), 0, 0.905, 0))
      group.add(cylinder(0.015, 0.25, material(COLOURS.steel, { metalness: 0.7, roughness: 0.25 }), 0, 1.03, -d * 0.4))
      break
    case 'hob': {
      group.add(box(w, 0.012, d, material(COLOURS.black, { roughness: 0.25 }), 0, 0.906, 0))
      const ring = material('#3a3d42', { roughness: 0.4 })
      for (const [sx, sz] of [[-1, -1], [1, -1], [-1, 1], [1, 1]]) {
        group.add(cylinder(Math.min(w, d) * 0.16, 0.004, ring, sx * w * 0.24, 0.914, sz * d * 0.24))
      }
      break
    }
    case 'tall': {
      group.add(box(w, 2.0, d, material(COLOURS.appliance, { roughness: 0.5 }), 0, 1.0, 0))
      const face = w / 2 + 0.005
      if (item.label === 'oven') {
        group.add(box(0.01, 0.55, d * 0.8, material(COLOURS.black, { roughness: 0.15 }), face, 1.05, 0))
      } else {
        group.add(box(0.012, 0.01, d * 0.9, material(COLOURS.steel), face, 1.25, 0))
        group.add(box(0.012, 0.6, 0.02, material(COLOURS.steel), face, 1.0, d * 0.4))
      }
      break
    }
    case 'heater': {
      group.add(box(w, 0.5, d, material('#d6d6d4', { roughness: 0.6 }), 0, 0.27, 0))
      const grille = material('#3b2a22', { emissive: GLOW.heater, emissiveIntensity: 0 })
      glow.push({ material: grille, colour: GLOW.heater, intensity: 1.4 })
      const [gx, gz] = along(-((back.axis === 'z' ? d : w) / 2 + 0.006))
      group.add(box(back.axis === 'z' ? w * 0.8 : 0.01, 0.32, back.axis === 'z' ? 0.01 : d * 0.8, grille, gx, 0.3, gz))
      // The heat goes out of the grille, the way the heater faces.
      const out = back.axis === 'z' ? new THREE.Vector3(0, 0, -back.sign) : new THREE.Vector3(-back.sign, 0, 0)
      const cone = heatCone(out, new THREE.Vector3(gx, 0.3, gz))
      group.add(cone)
      glow.push({ object: cone })
      break
    }
    case 'monitor':
      group.add(monitor(item, w, d, back))
      break
    case 'keyboard':
      group.add(box(w, 0.018, d, material('#2a2d31', { roughness: 0.6 }), 0, DESK_TOP + 0.009, 0))
      break
    case 'mouse': {
      const mouse = shade(new THREE.Mesh(new THREE.CapsuleGeometry(Math.min(w, d) / 2, Math.abs(w - d), 6, 12), material('#2a2d31', { roughness: 0.4 })))
      mouse.rotation.set(Math.PI / 2, 0, w > d ? Math.PI / 2 : 0)
      mouse.scale.set(1, 1, 0.45)
      mouse.position.y = DESK_TOP + 0.016
      group.add(mouse)
      break
    }
    case 'printer':
      group.add(box(w, 0.26, d, material('#d9dadc', { roughness: 0.5 }), 0, DESK_TOP + 0.13, 0))
      group.add(box(w * 0.7, 0.01, d * 0.35, material('#3a3d42', { roughness: 0.6 }), 0, DESK_TOP + 0.265, -d * 0.2))
      break
    case 'boiler':
      group.add(box(w, 0.72, d, material(COLOURS.appliance, { roughness: 0.45 }), 0, 1.5, 0))
      group.add(box(w * 0.3, 0.06, 0.01, material(COLOURS.black), 0, 1.3, d / 2 + 0.006))
      group.add(cylinder(0.02, 1.1, material(COLOURS.steel, { metalness: 0.6 }), -w * 0.25, 0.57, 0))
      group.add(cylinder(0.02, 1.1, material(COLOURS.steel, { metalness: 0.6 }), w * 0.25, 0.57, 0))
      break
    case 'toilet': {
      const porcelain = material('#fbfbfb', { roughness: 0.3 })
      const depth = back.axis === 'z' ? d : w
      const [cx, cz] = along(depth / 2 - 0.09)
      group.add(box(back.axis === 'z' ? w * 0.8 : 0.18, 0.4, back.axis === 'z' ? 0.18 : d * 0.8, porcelain, cx, 0.62, cz))
      const [px, pz] = along(-depth * 0.08)
      group.add(cylinder(Math.min(w, d) * 0.4, 0.4, porcelain, px, 0.2, pz))
      break
    }
    case 'cabinet':
      group.add(box(w, 0.45, d, material(COLOURS.cabinet, { roughness: 0.6 }), 0, 0.225, 0))
      break
    case 'tv': {
      // A 16:9 screen of the given diagonal, standing on its cabinet.
      const diagonal = (item.inches ?? 50) * 0.0254
      const width = diagonal * 0.8716
      const height = diagonal * 0.4903
      const screen = material(COLOURS.black, { roughness: 0.2, metalness: 0.2 })
      const alongX = w >= d
      group.add(box(alongX ? width : 0.04, height, alongX ? 0.04 : width, screen, 0, 0.52 + height / 2, 0))
      group.add(box(alongX ? 0.25 : 0.12, 0.06, alongX ? 0.12 : 0.25, screen, 0, 0.48, 0))
      break
    }
    case 'stickLight':
      group.add(twigTree(item.id, glow))
      break
    case 'person': {
      const figure = buildFigure(item.person ?? {})
      figure.rotation.y = { front: 0, back: Math.PI, left: -Math.PI / 2, right: Math.PI / 2 }[item.facing ?? 'front']
      group.add(figure)
      break
    }
    case 'radiator':
      group.add(radiator(w, d))
      break
    default:
      group.add(box(w, 0.5, d, material(COLOURS.cabinet), 0, 0.25, 0))
  }

  return { group, glow }
}

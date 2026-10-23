<template>
  <div ref="container" class="house-scene" :class="{ hovering: hoveredId }">
    <canvas ref="canvas" class="house-canvas" aria-label="3D model of the house. Drag to turn it, pinch or scroll to zoom, two fingers or right-drag to move it, tap a device for its details."></canvas>
  </div>
</template>

<script setup>
// The 3D house: rooms from utils/houseModel.js, one marker per device, coloured by its status. Three.js objects are
// kept out of Vue's reactivity on purpose; the scene is rebuilt only when the theme or the floor shown changes, and
// device markers are updated in place when states change.
import { ref, onMounted, onBeforeUnmount, watch } from 'vue'
import * as THREE from 'three'
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js'
import { CSS2DRenderer, CSS2DObject } from 'three/examples/jsm/renderers/CSS2DRenderer.js'
import {
  ROOMS, UNPLACED, PLAN_UNIT_METRES as S, STOREY_HEIGHT_METRES, WALL_HEIGHT_METRES, KIND_HEIGHT,
  placeDevices, deviceKind, deviceStatus, deviceNamed, primaryEntity, cutOpenings, wallSegments, placeOpening,
  defaultLayout, nearestWall,
} from '../utils/houseModel.js'
import { buildDeviceModel, WALL_KINDS } from './houseDeviceModels.js'
import { laminateTexture, scaleUvs, door, windowPane, buildFurniture } from './houseFurniture.js'
import { meshIndex, linksOf, uniqueLinks, routeToHub, lqiBand } from '../utils/zigbee.js'

const props = defineProps({
  devices: { type: Array, default: () => [] },
  selectedId: { type: String, default: null },
  // 'all' (both floors, the first lifted clear so the ground floor can be seen), 'ground' or 'first'.
  floorMode: { type: String, default: 'all' },
  // Arrange mode: anything (devices, furniture, doors, windows) can be picked and dragged, instead of a tap opening a
  // device's panel.
  arranging: { type: Boolean, default: false },
  // Device spots set in arrange mode, in DEVICE_SPOTS' shape (see arrangement() in houseModel).
  overrides: { type: Object, default: null },
  // The furniture and openings to draw ({ furniture, openings }); the house's own when null.
  layout: { type: Object, default: null },
  // What arrange mode has picked: { kind: 'device' | 'furniture' | 'opening', id }.
  picked: { type: Object, default: null },
  // The Zigbee mesh ({ nodes, links }) and whether to draw it: every link faintly, or with a device selected, that
  // device's links and its likely route to the hub, labelled with their LQI.
  zigbee: { type: Object, default: null },
  showSignal: { type: Boolean, default: false },
})
// 'select' sends { kind, id } or null for a tap (only devices outside arrange mode); 'move' sends
// { kind, id, x, y } in plan units when a dragged thing is let go.
const emit = defineEmits(['select', 'move'])

const container = ref(null)
const canvas = ref(null)
const hoveredId = ref(null)

// The house (not the side pad) is centred on the origin.
const CENTRE_X = 34
const CENTRE_Y = 39.5
const LIFT_METRES = 2.2
const MARKER_SCALE = 1.3
const toWorldX = (x) => (x - CENTRE_X) * S
const toWorldZ = (y) => (y - CENTRE_Y) * S

let renderer, labelRenderer, scene, camera, controls, frame, resizeObserver, themeObserver
let floorGroups = {}
let markers = new Map() // deviceId -> { group, body, hit, ring, deviceKey }
let hoverLabel = null
let laminate = null
let glowing = [] // furniture that lights up while a device is on: [{ name, glow: [{ material, colour, intensity }] }]
let colours = {}
let downAt = null
// Furniture and openings arrange mode can pick, by "kind:id": { object, kind, id, floor }.
let pickables = new Map()
let selectionBox = null // outline round the picked piece of furniture or opening
let ghost = null // where a dragged door or window will land
let drag = null // { kind, id, floor, x, y, moved, object, offset }
let signalGroup = null // the Zigbee signal lines and their LQI labels
let signalDrawn = '' // what signalGroup was drawn for, so a state update that moves nothing doesn't redraw it
const SIGNAL_COLOURS = { strong: '#2fb36a', fair: '#e0a526', weak: '#e5533d' }
const fallbackLayout = defaultLayout()
const currentLayout = () => props.layout ?? fallbackLayout
const raycaster = new THREE.Raycaster()
const pointer = new THREE.Vector2()

function readColours() {
  const style = getComputedStyle(document.documentElement)
  const css = (name, fallback) => (style.getPropertyValue(name) || '').trim() || fallback
  colours = {
    background: css('--bg-primary', '#f5f5f5'),
    floor: css('--bg-secondary', '#ffffff'),
    ground: css('--bg-tertiary', '#f8f9fa'),
    wall: css('--text-secondary', '#666666'),
    edge: css('--text-primary', '#2c3e50'),
    on: css('--color-warning', '#f39c12'),
    off: css('--text-tertiary', '#999999'),
    alert: css('--color-danger', '#e74c3c'),
    reading: css('--color-primary', '#42a5f5'),
    selected: css('--color-primary', '#42a5f5'),
  }
}

function floorHeight(floor) {
  if (floor === 'ground') return 0
  return STOREY_HEIGHT_METRES + (props.floorMode === 'all' ? LIFT_METRES : 0)
}

function buildFloor(floor) {
  const group = new THREE.Group()
  const rooms = ROOMS.filter((r) => r.floor === floor)
  // Laminate throughout and off-white walls (owner), see-through so the rooms can be looked into.
  const floorMaterial = new THREE.MeshStandardMaterial({ map: laminate, roughness: 0.65 })
  const wallMaterial = new THREE.MeshStandardMaterial({ color: '#f2eee6', transparent: true, opacity: 0.32, depthWrite: false, side: THREE.DoubleSide })
  const edgeMaterial = new THREE.LineBasicMaterial({ color: '#8f8a80', transparent: true, opacity: 0.7 })

  // The cupboard under the stairs and the rooms inside the den share their room's floor.
  for (const room of rooms.filter((r) => !r.inner && !r.nested)) {
    const geometry = new THREE.BoxGeometry(room.w * S, 0.06, room.h * S)
    scaleUvs(geometry, room.w * S, room.h * S)
    const slab = new THREE.Mesh(geometry, floorMaterial)
    slab.position.set(toWorldX(room.x + room.w / 2), -0.03, toWorldZ(room.y + room.h / 2))
    slab.receiveShadow = true
    group.add(slab)
  }

  const wallHeight = WALL_HEIGHT_METRES
  const layout = currentLayout()
  const openings = layout.openings.filter((o) => o.floor === floor)
  const outlinePoints = []
  const span = (seg, from, to) => (seg.axis === 'h'
    ? [toWorldX(from), toWorldZ(seg.at), toWorldX(to), toWorldZ(seg.at)]
    : [toWorldX(seg.at), toWorldZ(from), toWorldX(seg.at), toWorldZ(to)])
  // A piece of wall from `from` to `to` along the segment, between two heights.
  const addWall = (seg, from, to, bottom, top) => {
    const length = (to - from) * S
    const middle = (from + to) / 2
    const wall = new THREE.Mesh(
      new THREE.BoxGeometry(seg.axis === 'h' ? length : 0.08, top - bottom, seg.axis === 'h' ? 0.08 : length),
      wallMaterial
    )
    wall.position.set(seg.axis === 'h' ? toWorldX(middle) : toWorldX(seg.at), (bottom + top) / 2, seg.axis === 'h' ? toWorldZ(seg.at) : toWorldZ(middle))
    wall.renderOrder = 2
    group.add(wall)
  }

  for (const seg of wallSegments(rooms.filter((r) => !r.nested))) {
    const { pieces, cuts } = cutOpenings(seg, openings)
    for (const piece of pieces) {
      addWall(seg, piece.start, piece.end, 0, wallHeight)
      const [ax, az, bx, bz] = span(seg, piece.start, piece.end)
      outlinePoints.push(ax, 0.01, az, bx, 0.01, bz)
    }
    for (const cut of cuts) {
      const from = Math.max(cut.from, seg.start)
      const to = Math.min(cut.to, seg.end)
      if (cut.type === 'door') {
        addWall(seg, from, to, 2.05, wallHeight)
        const [ax, az, bx, bz] = span(seg, from, to)
        group.add(door(cut, seg.axis, (to - from) * S, ax, az, bx, bz))
      } else if (cut.type === 'window') {
        addWall(seg, from, to, 0, 0.9)
        addWall(seg, from, to, 2.1, wallHeight)
        const [ax, az, bx, bz] = span(seg, from, to)
        group.add(windowPane(seg.axis, (to - from) * S, (ax + bx) / 2, (az + bz) / 2, 0.9, 2.1))
        outlinePoints.push(ax, 0.01, az, bx, 0.01, bz)
      }
    }
    // The top edge runs over doors and windows, but not across an open-plan gap.
    for (const piece of cutOpenings(seg, openings.filter((o) => o.type === 'open')).pieces) {
      const [ax, az, bx, bz] = span(seg, piece.start, piece.end)
      outlinePoints.push(ax, wallHeight, az, bx, wallHeight, bz)
    }
  }
  const outline = new THREE.BufferGeometry()
  outline.setAttribute('position', new THREE.Float32BufferAttribute(outlinePoints, 3))
  group.add(new THREE.LineSegments(outline, edgeMaterial))

  // An invisible box over each door, window and open gap, so arrange mode can pick it.
  for (const opening of openings) {
    const length = (opening.to - opening.from) * S
    const middle = (opening.from + opening.to) / 2
    const hit = new THREE.Mesh(
      new THREE.BoxGeometry(opening.axis === 'h' ? length : 0.4, 2.1, opening.axis === 'h' ? 0.4 : length),
      new THREE.MeshBasicMaterial({ visible: false })
    )
    hit.position.set(opening.axis === 'h' ? toWorldX(middle) : toWorldX(opening.at), 1.05, opening.axis === 'h' ? toWorldZ(opening.at) : toWorldZ(middle))
    hit.userData = { kind: 'opening', id: opening.id, floor }
    group.add(hit)
    pickables.set(`opening:${opening.id}`, { object: hit, kind: 'opening', id: opening.id, floor })
  }

  for (const item of layout.furniture.filter((f) => f.floor === floor)) {
    const { group: piece, glow } = buildFurniture(item, item.w * S, item.h * S)
    piece.position.set(toWorldX(item.x + item.w / 2), 0, toWorldZ(item.y + item.h / 2))
    piece.rotation.y = ((item.rotation ?? 0) * Math.PI) / 180
    piece.userData = { kind: 'furniture', id: item.id, floor }
    group.add(piece)
    pickables.set(`furniture:${item.id}`, { object: piece, kind: 'furniture', id: item.id, floor })
    if (item.litBy) glowing.push({ name: item.litBy, glow })
  }

  for (const room of rooms.filter((r) => r.stairs)) group.add(buildStairs(room, floor))

  // Devices with no room wait on a paved patio beside the house.
  if (floor === 'ground') {
    const pad = new THREE.Mesh(
      new THREE.BoxGeometry(UNPLACED.w * S, 0.04, UNPLACED.h * S),
      new THREE.MeshStandardMaterial({ color: '#c9c3b8', roughness: 0.95 })
    )
    pad.position.set(toWorldX(UNPLACED.x + UNPLACED.w / 2), -0.01, toWorldZ(UNPLACED.y + UNPLACED.h / 2))
    pad.receiveShadow = true
    group.add(pad)
  }

  return group
}

// Grey-carpeted stairs (owner) climb along the hallway's den-side wall from the front towards the back, over the
// cupboard under them.
function buildStairs(room, floor) {
  const group = new THREE.Group()
  const carpet = new THREE.MeshStandardMaterial({ color: '#8e9196', roughness: 1 })
  const width = 7 * S
  const x = toWorldX(room.x + 3.5)
  if (floor === 'ground') {
    const steps = 13
    const front = room.y + room.h - 3
    const back = room.y + 1
    const depth = ((front - back) / steps) * S
    const riser = STOREY_HEIGHT_METRES / steps
    for (let i = 0; i < steps; i++) {
      const top = (i + 1) * riser
      // Each step is solid down to the floor, so the flight reads as a staircase from any side.
      const step = new THREE.Mesh(new THREE.BoxGeometry(width, top, depth), carpet)
      step.position.set(x, top / 2, toWorldZ(front) - depth * (i + 0.5))
      step.castShadow = true
      step.receiveShadow = true
      group.add(step)
    }
  } else {
    const material = new THREE.MeshStandardMaterial({ color: '#8e9196', transparent: true, opacity: 0.35 })
    const opening = new THREE.Mesh(new THREE.BoxGeometry(width, 0.02, 10 * S), material)
    opening.position.set(x, 0.01, toWorldZ(room.y + room.h - 5))
    group.add(opening)
  }
  return group
}

/**
 * Which way a wall-mounted device faces when its spot doesn't say: away from the wall it is on (within 15 cm),
 * otherwise towards the front. Radians about the vertical, for a model that faces +z.
 */
function wallFacing(kind, floor, x, y) {
  if (!WALL_KINDS.has(kind)) return 0
  const wall = nearestWall(floor, x, y)
  if (!wall || wall.distance > 1.2) return 0
  if (wall.axis === 'h') return y >= wall.at ? 0 : Math.PI
  return x >= wall.at ? Math.PI / 2 : -Math.PI / 2
}

function toneColour(tone) {
  return colours[tone] ?? colours.reading
}

function styleMarker(marker, device) {
  const { tone } = deviceStatus(device)
  const selected = device.id === props.selectedId || (props.picked?.kind === 'device' && props.picked.id === device.id)
  if (marker.led) {
    // A white device shows its state on its LED: lit in the state's colour, dark when off.
    marker.tint.color.set(tone === 'off' ? '#555555' : toneColour(tone))
    marker.tint.emissive.set(tone === 'off' ? '#000000' : toneColour(tone))
    marker.tint.emissiveIntensity = tone === 'off' ? 0 : 1.2
  } else {
    marker.tint.color.set(toneColour(tone))
    marker.tint.emissive.set(tone === 'on' && marker.kind === 'light' ? toneColour('on') : '#000000')
    marker.tint.emissiveIntensity = tone === 'on' && marker.kind === 'light' ? 0.9 : 0
  }
  marker.update?.(device)
  marker.ring.visible = selected || device.id === hoveredId.value
  marker.facing.visible = props.arranging && marker.ring.visible
  marker.ring.material.opacity = selected ? 0.9 : 0.4
  marker.body.scale.setScalar(selected ? MARKER_SCALE * 1.25 : MARKER_SCALE)
}

function syncDevices() {
  if (!scene) return
  const placed = placeDevices(props.devices, props.overrides)
  const seen = new Set()
  for (const { device, room, x, y, height: spotHeight, rotation } of placed) {
    seen.add(device.id)
    const kind = deviceKind(device)
    let marker = markers.get(device.id)
    if (!marker || marker.kind !== kind || marker.floor !== room.floor) {
      if (marker) removeMarker(device.id)
      marker = createMarker(device.id, kind, room.floor)
      markers.set(device.id, marker)
    }
    const height = spotHeight ?? KIND_HEIGHT[kind] ?? 0.9
    marker.group.position.set(toWorldX(x), 0, toWorldZ(y))
    marker.body.position.y = height
    marker.body.rotation.y = rotation ? (rotation * Math.PI) / 180 : wallFacing(kind, room.floor, x, y)
    marker.hit.position.y = height
    marker.stalk.scale.y = Math.max(0.01, height)
    marker.device = device
    marker.facing.rotation.y = marker.body.rotation.y
    styleMarker(marker, device)
  }
  for (const id of [...markers.keys()]) if (!seen.has(id)) removeMarker(id)
  for (const { name, glow } of glowing) {
    const on = primaryEntity(deviceNamed(props.devices, name))?.state === 'on'
    for (const part of glow) {
      if (part.object) part.object.visible = on // a heater's heat cone
      else part.material.emissiveIntensity = on ? part.intensity : 0
    }
  }
  updateHoverLabel()
  updateSignal()
}

function createMarker(id, kind, floor) {
  const group = new THREE.Group()
  const model = buildDeviceModel(kind)
  // The model sits inside its own group, which HouseScene raises, turns and scales.
  const body = new THREE.Group()
  body.add(model.object)
  body.scale.setScalar(MARKER_SCALE)
  group.add(body)
  // A bigger invisible sphere to aim at, so small devices are easy to tap on a phone.
  const hit = new THREE.Mesh(new THREE.SphereGeometry(0.32, 10, 8), new THREE.MeshBasicMaterial({ visible: false }))
  hit.userData.deviceId = id
  group.add(hit)
  const stalkGeometry = new THREE.BufferGeometry().setFromPoints([new THREE.Vector3(0, 0, 0), new THREE.Vector3(0, 1, 0)])
  const stalk = new THREE.Line(stalkGeometry, new THREE.LineBasicMaterial({ color: colours.wall, transparent: true, opacity: 0.35 }))
  group.add(stalk)
  const ring = new THREE.Mesh(new THREE.RingGeometry(0.22, 0.3, 32), new THREE.MeshBasicMaterial({ color: colours.selected, transparent: true, opacity: 0.9, side: THREE.DoubleSide }))
  ring.rotation.x = -Math.PI / 2
  ring.position.y = 0.02
  group.add(ring)
  // Which way the device faces (its rotation; 0 is towards the front of the house), shown with the ring.
  const facing = new THREE.Group()
  const pointerShape = new THREE.Shape([new THREE.Vector2(-0.08, 0.3), new THREE.Vector2(0.08, 0.3), new THREE.Vector2(0, 0.46)])
  const notch = new THREE.Mesh(new THREE.ShapeGeometry(pointerShape), new THREE.MeshBasicMaterial({ color: colours.selected, side: THREE.DoubleSide }))
  notch.rotation.x = Math.PI / 2
  notch.position.y = 0.021
  facing.add(notch)
  group.add(facing)
  floorGroups[floor].add(group)
  return { group, body, hit, stalk, ring, facing, kind, floor, device: null, tint: model.tint, led: model.led, update: model.update }
}

function removeMarker(id) {
  const marker = markers.get(id)
  if (!marker) return
  marker.group.parent?.remove(marker.group)
  marker.group.traverse((o) => { o.geometry?.dispose(); o.material?.map?.dispose(); o.material?.dispose() })
  markers.delete(id)
}

function updateHoverLabel() {
  const id = hoveredId.value ?? props.selectedId ?? (props.picked?.kind === 'device' ? props.picked.id : null)
  const marker = id ? markers.get(id) : null
  if (!marker || !marker.group.parent?.visible) {
    hoverLabel.visible = false
    return
  }
  const status = deviceStatus(marker.device)
  hoverLabel.element.querySelector('.name').textContent = marker.device.name
  hoverLabel.element.querySelector('.status').textContent = status.text
  hoverLabel.element.dataset.tone = status.tone
  marker.body.updateWorldMatrix(true, false)
  const worldPosition = marker.body.getWorldPosition(new THREE.Vector3())
  hoverLabel.position.copy(worldPosition).add(new THREE.Vector3(0, 0.35, 0))
  hoverLabel.visible = true
}

function buildHouse() {
  for (const group of Object.values(floorGroups)) {
    scene.remove(group)
    group.traverse((o) => { o.geometry?.dispose(); o.material?.dispose() })
  }
  markers = new Map()
  glowing = []
  pickables = new Map()
  laminate ??= laminateTexture()
  floorGroups = { ground: buildFloor('ground'), first: buildFloor('first') }
  for (const [floor, group] of Object.entries(floorGroups)) {
    group.position.y = floorHeight(floor)
    group.visible = props.floorMode === 'all' || props.floorMode === floor
    scene.add(group)
  }
  syncDevices()
  updateSelectionBox()
}

/** The device the signal lines are about: the one whose panel is open, or the one picked in arrange mode. */
function focusedDevice() {
  return props.selectedId ?? (props.picked?.kind === 'device' ? props.picked.id : null)
}

/** Where a device's marker is, in the scene, or null when its floor is hidden or it isn't drawn. */
function markerPoint(id) {
  const marker = markers.get(id)
  if (!marker || !marker.group.parent?.visible) return null
  marker.body.updateWorldMatrix(true, false)
  return marker.body.getWorldPosition(new THREE.Vector3())
}

function beam(from, to, radius, colour, opacity) {
  const direction = new THREE.Vector3().subVectors(to, from)
  const length = direction.length()
  const mesh = new THREE.Mesh(
    new THREE.CylinderGeometry(radius, radius, length, 8, 1, true),
    new THREE.MeshBasicMaterial({ color: colour, transparent: true, opacity, depthWrite: false })
  )
  mesh.position.copy(from).addScaledVector(direction, 0.5)
  mesh.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), direction.normalize())
  mesh.renderOrder = 6
  return mesh
}

function lqiLabel(lqi, at, onRoute) {
  const div = document.createElement('div')
  div.className = `scene-lqi ${lqiBand(lqi)}${onRoute ? ' route' : ''}`
  div.textContent = String(lqi)
  const label = new CSS2DObject(div)
  label.position.copy(at)
  return label
}

/** Redraws the signal lines if anything they depend on has changed. */
function updateSignal() {
  if (!scene) return
  const focus = focusedDevice()
  const show = props.showSignal && props.zigbee
  const points = show ? [...markers.keys()].map((id) => markerPoint(id)?.toArray().map((n) => n.toFixed(2)).join(',')).join(';') : ''
  const drawnFor = `${show}|${focus}|${points}|${props.zigbee?.links?.length ?? 0}`
  if (drawnFor === signalDrawn) return
  signalDrawn = drawnFor
  if (signalGroup) {
    scene.remove(signalGroup)
    signalGroup.traverse((o) => { o.geometry?.dispose(); o.material?.dispose(); o.element?.remove() })
    signalGroup = null
  }
  if (!show) return
  signalGroup = new THREE.Group()
  const index = meshIndex(props.zigbee)
  if (focus && index.roles.has(focus)) {
    const route = routeToHub(props.zigbee, focus, index) ?? []
    const onRoute = new Set(route.slice(1).map((step, i) => [route[i].id, step.id].sort().join('|')))
    const from = markerPoint(focus)
    for (const link of from ? linksOf(props.zigbee, focus, index) : []) {
      const to = markerPoint(link.id)
      if (!to || onRoute.has([focus, link.id].sort().join('|'))) continue
      signalGroup.add(beam(from, to, 0.012, SIGNAL_COLOURS[lqiBand(link.lqi)], 0.75))
      signalGroup.add(lqiLabel(link.lqi, new THREE.Vector3().lerpVectors(from, to, 0.5), false))
    }
    // The likely route to the hub, thicker, hop by hop.
    for (let i = 1; i < route.length; i++) {
      const a = markerPoint(route[i - 1].id)
      const b = markerPoint(route[i].id)
      if (!a || !b) continue
      signalGroup.add(beam(a, b, 0.035, SIGNAL_COLOURS[lqiBand(route[i].lqi)], 0.95))
      signalGroup.add(lqiLabel(route[i].lqi, new THREE.Vector3().lerpVectors(a, b, 0.5), true))
    }
  } else {
    for (const link of uniqueLinks(props.zigbee, index)) {
      const a = markerPoint(link.a)
      const b = markerPoint(link.b)
      if (!a || !b) continue
      const band = lqiBand(link.lqi)
      signalGroup.add(beam(a, b, band === 'weak' ? 0.006 : 0.01, SIGNAL_COLOURS[band], { strong: 0.8, fair: 0.55, weak: 0.25 }[band]))
    }
  }
  scene.add(signalGroup)
}

/** Outlines the picked piece of furniture or opening (a picked device shows its ring instead). */
function updateSelectionBox() {
  if (selectionBox) {
    scene.remove(selectionBox)
    selectionBox.geometry.dispose()
    selectionBox = null
  }
  const picked = props.arranging && props.picked ? pickables.get(`${props.picked.kind}:${props.picked.id}`) : null
  if (!picked || !picked.object.parent?.visible) return
  selectionBox = new THREE.BoxHelper(picked.object, colours.selected)
  selectionBox.material.depthTest = false
  selectionBox.renderOrder = 10
  scene.add(selectionBox)
}

/** Where on a floor (plan units) the pointer is, or null when it points at the sky. */
function floorPoint(event, floor) {
  const rect = canvas.value.getBoundingClientRect()
  pointer.x = ((event.clientX - rect.left) / rect.width) * 2 - 1
  pointer.y = -((event.clientY - rect.top) / rect.height) * 2 + 1
  raycaster.setFromCamera(pointer, camera)
  const plane = new THREE.Plane(new THREE.Vector3(0, 1, 0), -floorGroups[floor].position.y)
  const hit = raycaster.ray.intersectPlane(plane, new THREE.Vector3())
  return hit ? { x: hit.x / S + CENTRE_X, y: hit.z / S + CENTRE_Y } : null
}

// A nice day (owner): blue sky fading to haze at the horizon, a lawn round the house and a warm sun with soft shadows.
function buildOutdoors() {
  const sky = document.createElement('canvas')
  sky.width = 2
  sky.height = 256
  const g = sky.getContext('2d')
  const gradient = g.createLinearGradient(0, 0, 0, 256)
  gradient.addColorStop(0, '#3f8fdc')
  gradient.addColorStop(0.65, '#9fcaf0')
  gradient.addColorStop(1, '#e3f0fa')
  g.fillStyle = gradient
  g.fillRect(0, 0, 2, 256)
  const skyTexture = new THREE.CanvasTexture(sky)
  skyTexture.colorSpace = THREE.SRGBColorSpace
  scene.background = skyTexture
  scene.fog = new THREE.Fog('#d6e9f8', 35, 90)

  const lawn = document.createElement('canvas')
  lawn.width = 256
  lawn.height = 256
  const l = lawn.getContext('2d')
  l.fillStyle = '#5e9a3c'
  l.fillRect(0, 0, 256, 256)
  let seed = 3
  const random = () => (seed = (seed * 16807) % 2147483647) / 2147483647
  for (let i = 0; i < 9000; i++) {
    l.fillStyle = `hsl(${88 + random() * 22}, ${40 + random() * 20}%, ${28 + random() * 22}%)`
    l.fillRect(random() * 256, random() * 256, 1.5, 3 + random() * 3)
  }
  const grassTexture = new THREE.CanvasTexture(lawn)
  grassTexture.colorSpace = THREE.SRGBColorSpace
  grassTexture.wrapS = THREE.RepeatWrapping
  grassTexture.wrapT = THREE.RepeatWrapping
  grassTexture.repeat.set(60, 60)
  grassTexture.anisotropy = 8
  const grass = new THREE.Mesh(new THREE.PlaneGeometry(180, 180), new THREE.MeshStandardMaterial({ map: grassTexture, roughness: 1 }))
  grass.rotation.x = -Math.PI / 2
  grass.position.y = -0.065
  grass.receiveShadow = true
  scene.add(grass)

  scene.add(new THREE.HemisphereLight('#cfe6ff', '#5d7d43', 1.1))
  const sun = new THREE.DirectionalLight('#fff1d6', 2.4)
  sun.position.set(-9, 16, 11)
  sun.castShadow = true
  sun.shadow.mapSize.set(2048, 2048)
  Object.assign(sun.shadow.camera, { left: -10, right: 10, top: 10, bottom: -10, near: 1, far: 50 })
  sun.shadow.bias = -0.0004
  sun.shadow.normalBias = 0.02
  scene.add(sun)
}

function aim(event) {
  const rect = canvas.value.getBoundingClientRect()
  pointer.x = ((event.clientX - rect.left) / rect.width) * 2 - 1
  pointer.y = -((event.clientY - rect.top) / rect.height) * 2 + 1
  raycaster.setFromCamera(pointer, camera)
}

/** The device under the pointer, if any. */
function pick(event) {
  aim(event)
  const targets = [...markers.values()].filter((m) => m.group.parent?.visible).map((m) => m.hit)
  const hit = raycaster.intersectObjects(targets, false)[0]
  return hit?.object.userData.deviceId ?? null
}

/**
 * What arrange mode would pick under the pointer: a device first (their markers are small), otherwise the nearest
 * piece of furniture, door, window or gap. { kind, id, floor } or null.
 */
function pickAnything(event) {
  const deviceId = pick(event)
  if (deviceId) return { kind: 'device', id: deviceId, floor: markers.get(deviceId).floor }
  const targets = [...pickables.values()].filter((p) => p.object.parent?.visible).map((p) => p.object)
  for (const hit of raycaster.intersectObjects(targets, true)) {
    let object = hit.object
    while (object && !object.userData.kind) object = object.parent
    if (object) return { kind: object.userData.kind, id: object.userData.id, floor: object.userData.floor }
  }
  return null
}

function draggedObject(target) {
  return target.kind === 'device' ? markers.get(target.id)?.group : pickables.get(`${target.kind}:${target.id}`)?.object
}

function onPointerDown(event) {
  downAt = { x: event.clientX, y: event.clientY }
  if (!props.arranging) return
  const target = pickAnything(event)
  if (!target) return
  // Caught before OrbitControls sees the press, so dragging a thing doesn't also turn the house.
  controls.enabled = false
  const start = floorPoint(event, target.floor)
  const object = draggedObject(target)
  drag = { ...target, start, origin: object?.position.clone(), object, moved: false, pointerId: event.pointerId }
  canvas.value.setPointerCapture?.(event.pointerId)
}

function onPointerUp(event) {
  if (drag) {
    const finished = drag
    drag = null
    controls.enabled = true
    canvas.value.releasePointerCapture?.(finished.pointerId)
    removeGhost()
    const point = floorPoint(event, finished.floor)
    if (finished.moved && point && finished.start) {
      // The object's own centre moved by as much as the pointer did, so a grab off-centre doesn't jump.
      const centre = planCentreOf(finished)
      emit('move', { kind: finished.kind, id: finished.id, x: centre.x + point.x - finished.start.x, y: centre.y + point.y - finished.start.y })
    } else {
      if (finished.object && finished.origin) finished.object.position.copy(finished.origin)
      emit('select', { kind: finished.kind, id: finished.id })
    }
    downAt = null
    return
  }
  if (!downAt) return
  const moved = Math.hypot(event.clientX - downAt.x, event.clientY - downAt.y)
  downAt = null
  if (moved >= 6) return
  if (props.arranging) emit('select', null)
  else {
    const id = pick(event)
    emit('select', id ? { kind: 'device', id } : null)
  }
}

/** The plan position of the dragged thing's centre before the drag. */
function planCentreOf(target) {
  if (target.kind === 'device') {
    return { x: target.origin.x / S + CENTRE_X, y: target.origin.z / S + CENTRE_Y }
  }
  const layout = currentLayout()
  if (target.kind === 'furniture') {
    const item = layout.furniture.find((f) => f.id === target.id)
    return { x: item.x + item.w / 2, y: item.y + item.h / 2 }
  }
  const opening = layout.openings.find((o) => o.id === target.id)
  const middle = (opening.from + opening.to) / 2
  return opening.axis === 'h' ? { x: middle, y: opening.at } : { x: opening.at, y: middle }
}

function removeGhost() {
  if (!ghost) return
  ghost.parent?.remove(ghost)
  ghost.geometry.dispose()
  ghost.material.dispose()
  ghost = null
}

/** Shows where a dragged door or window will land: the nearest wall, as placeOpening would put it. */
function showGhost(target, x, y) {
  const opening = currentLayout().openings.find((o) => o.id === target.id)
  if (!opening) return
  const landed = placeOpening(opening, x, y)
  const length = (landed.to - landed.from) * S
  const middle = (landed.from + landed.to) / 2
  if (!ghost) {
    ghost = new THREE.Mesh(new THREE.BoxGeometry(1, 2.1, 1), new THREE.MeshBasicMaterial({ color: colours.selected, transparent: true, opacity: 0.35, depthWrite: false }))
    floorGroups[target.floor].add(ghost)
  }
  ghost.scale.set(landed.axis === 'h' ? length : 0.12, 1, landed.axis === 'h' ? 0.12 : length)
  ghost.position.set(landed.axis === 'h' ? toWorldX(middle) : toWorldX(landed.at), 1.05, landed.axis === 'h' ? toWorldZ(landed.at) : toWorldZ(middle))
}

function onPointerMove(event) {
  if (drag) {
    if (!drag.moved && Math.hypot(event.clientX - downAt.x, event.clientY - downAt.y) < 4) return
    drag.moved = true
    const point = floorPoint(event, drag.floor)
    if (!point || !drag.start) return
    const dx = point.x - drag.start.x
    const dy = point.y - drag.start.y
    if (drag.kind === 'opening') {
      const centre = planCentreOf(drag)
      showGhost(drag, centre.x + dx, centre.y + dy)
    } else if (drag.object && drag.origin) {
      drag.object.position.set(drag.origin.x + dx * S, drag.origin.y, drag.origin.z + dy * S)
      if (drag.kind === 'device') updateHoverLabel()
      if (selectionBox) selectionBox.update()
    }
    return
  }
  if (event.pointerType !== 'mouse' || event.buttons) return
  const id = pick(event)
  if (id !== hoveredId.value) {
    hoveredId.value = id
    for (const marker of markers.values()) styleMarker(marker, marker.device)
    updateHoverLabel()
  }
}

function resize() {
  const { clientWidth: width, clientHeight: height } = container.value
  if (!width || !height) return
  renderer.setSize(width, height, false)
  labelRenderer.setSize(width, height)
  camera.aspect = width / height
  camera.updateProjectionMatrix()
}

function animate() {
  frame = requestAnimationFrame(animate)
  // Heat shimmers: each visible cone breathes a little, out of step with the others.
  const t = performance.now() / 1000
  for (const { glow } of glowing) {
    for (const part of glow) {
      if (!part.object?.visible) continue
      const phase = part.object.id
      part.object.material.opacity = 0.42 + 0.16 * Math.sin(t * 3.2 + phase)
      part.object.scale.setScalar(1 + 0.05 * Math.sin(t * 2.1 + phase * 1.7))
    }
  }
  controls.update()
  renderer.render(scene, camera)
  labelRenderer.render(scene, camera)
}

/** Back to the starting view, from the front and above. */
function resetView() {
  // Further back on a tall, narrow screen (a phone held upright), so the whole house fits across it.
  const distance = camera.aspect < 1.2 ? Math.min(2.4, Math.pow(1.2 / camera.aspect, 0.9)) : 1
  const target = new THREE.Vector3(0, props.floorMode === 'first' ? STOREY_HEIGHT_METRES : 1, 0)
  camera.position.copy(target).add(new THREE.Vector3(-5, 10, 13).multiplyScalar(distance))
  controls.target.copy(target)
  controls.update()
}

defineExpose({ resetView })

onMounted(() => {
  readColours()
  renderer = new THREE.WebGLRenderer({ canvas: canvas.value, antialias: true })
  renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2))
  labelRenderer = new CSS2DRenderer()
  labelRenderer.domElement.className = 'scene-labels'
  container.value.appendChild(labelRenderer.domElement)

  renderer.shadowMap.enabled = true
  renderer.shadowMap.type = THREE.PCFSoftShadowMap
  scene = new THREE.Scene()
  camera = new THREE.PerspectiveCamera(45, 1, 0.1, 200)
  buildOutdoors()

  const div = document.createElement('div')
  div.className = 'scene-device-label'
  div.innerHTML = '<span class="name"></span><span class="status"></span>'
  hoverLabel = new CSS2DObject(div)
  hoverLabel.visible = false
  scene.add(hoverLabel)

  controls = new OrbitControls(camera, renderer.domElement)
  controls.enableDamping = true
  controls.dampingFactor = 0.08
  controls.screenSpacePanning = true
  controls.minDistance = 2.5
  controls.maxDistance = 40
  controls.maxPolarAngle = Math.PI * 0.49
  controls.touches = { ONE: THREE.TOUCH.ROTATE, TWO: THREE.TOUCH.DOLLY_PAN }

  buildHouse()
  resize()
  resetView()
  resizeObserver = new ResizeObserver(resize)
  resizeObserver.observe(container.value)

  // Themes are CSS variables switched on the root element; rebuild in the new colours when it changes.
  themeObserver = new MutationObserver(() => { readColours(); buildHouse() })
  themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme', 'class', 'style'] })

  // Capture phase, so a press on something to drag can switch OrbitControls off before it starts turning the house.
  canvas.value.addEventListener('pointerdown', onPointerDown, { capture: true })
  canvas.value.addEventListener('pointerup', onPointerUp)
  canvas.value.addEventListener('pointermove', onPointerMove)
  animate()
})

onBeforeUnmount(() => {
  cancelAnimationFrame(frame)
  resizeObserver?.disconnect()
  themeObserver?.disconnect()
  canvas.value?.removeEventListener('pointerdown', onPointerDown, { capture: true })
  canvas.value?.removeEventListener('pointerup', onPointerUp)
  canvas.value?.removeEventListener('pointermove', onPointerMove)
  controls?.dispose()
  scene?.traverse((o) => { o.geometry?.dispose(); o.material?.dispose() })
  renderer?.dispose()
  labelRenderer?.domElement.remove()
})

watch(() => props.devices, syncDevices)
watch(() => props.selectedId, () => { for (const m of markers.values()) styleMarker(m, m.device); updateHoverLabel() })
watch(() => props.overrides, syncDevices, { deep: true })
watch(() => props.layout, buildHouse, { deep: true })
watch(() => props.picked, () => {
  for (const m of markers.values()) styleMarker(m, m.device)
  updateSelectionBox()
  updateHoverLabel()
  updateSignal()
}, { deep: true })
watch(() => [props.showSignal, props.zigbee, props.selectedId], updateSignal)
watch(() => props.arranging, () => {
  for (const m of markers.values()) styleMarker(m, m.device)
  updateSelectionBox()
})
watch(() => props.floorMode, () => {
  for (const [floor, group] of Object.entries(floorGroups)) {
    group.position.y = floorHeight(floor)
    group.visible = props.floorMode === 'all' || props.floorMode === floor
  }
  controls.target.y = props.floorMode === 'first' ? STOREY_HEIGHT_METRES : 1
  updateHoverLabel()
  updateSignal()
})
</script>

<style scoped>
.house-scene {
  position: relative;
  width: 100%;
  height: 100%;
  overflow: hidden;
  touch-action: none;
}

.house-scene.hovering {
  cursor: pointer;
}

.house-canvas {
  display: block;
  width: 100%;
  height: 100%;
}

.house-scene :deep(.scene-labels) {
  position: absolute;
  inset: 0;
  pointer-events: none;
}

.house-scene :deep(.scene-lqi) {
  font-size: 0.68rem;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
  color: #fff;
  padding: 0.05rem 0.35rem;
  border-radius: 999px;
  pointer-events: none;
  opacity: 0.85;
}

.house-scene :deep(.scene-lqi.route) {
  font-size: 0.78rem;
  opacity: 1;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.35);
}

.house-scene :deep(.scene-lqi.strong) { background: #2fb36a; }
.house-scene :deep(.scene-lqi.fair) { background: #c98e12; }
.house-scene :deep(.scene-lqi.weak) { background: #d4442f; }

.house-scene :deep(.scene-device-label) {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.1rem;
  background: var(--bg-secondary);
  color: var(--text-primary);
  border: 1px solid var(--border-color);
  box-shadow: 0 4px 12px var(--shadow-md);
  border-radius: 8px;
  padding: 0.3rem 0.6rem;
  font-size: 0.8rem;
  white-space: nowrap;
  transform: translateY(-50%);
}

.house-scene :deep(.scene-device-label .name) {
  font-weight: 600;
}

.house-scene :deep(.scene-device-label .status) {
  color: var(--text-secondary);
  font-variant-numeric: tabular-nums;
}

.house-scene :deep(.scene-device-label .status:empty) {
  display: none;
}
</style>

<template>
  <div class="house-3d-view">
    <HouseScene
      ref="sceneRef"
      class="scene"
      :devices="devices"
      :selected-id="arranging ? null : selectedId"
      :floor-mode="floorMode"
      :arranging="arranging"
      :overrides="overrides"
      :layout="displayLayout"
      :picked="picked"
      :zigbee="zigbee"
      :show-signal="showSignal"
      @select="onSelect"
      @move="onMove"
    />

    <div class="toolbar">
      <div class="floor-switch" role="group" aria-label="Floors shown">
        <button
          v-for="option in FLOOR_OPTIONS"
          :key="option.id"
          type="button"
          class="floor-btn"
          :class="{ active: floorMode === option.id }"
          :aria-pressed="floorMode === option.id"
          @click="floorMode = option.id"
        >
          {{ option.label }}
        </button>
      </div>
      <button type="button" class="tool-btn" :class="{ active: showSignal }" :aria-pressed="showSignal" :disabled="!zigbee" :title="zigbee ? '' : 'The house app has not sent the Zigbee mesh yet'" @click="showSignal = !showSignal">Signal</button>
      <button type="button" class="tool-btn" @click="sceneRef?.resetView()">Reset view</button>
      <button type="button" class="tool-btn" :class="{ active: arranging }" :aria-pressed="arranging" @click="toggleArranging">Arrange</button>
    </div>

    <HouseArrangePanel
      v-if="arranging"
      :picked="pickedInfo"
      :json="layoutText"
      @action="applyAction"
      @reset="resetArrangement"
      @done="toggleArranging"
    />

    <div class="status-bar">
      <span class="live-chip" :class="liveState.tone">{{ liveState.text }}</span>
      <span class="hint">Drag to turn · pinch or scroll to zoom · two fingers or right-drag to move</span>
    </div>

    <div v-if="loaded && devices.length === 0" class="empty">
      <template v-if="unsupported">
        <p class="empty-title">The server doesn't have the 3D view yet</p>
        <p>Its devices will appear once the API is updated to a version with the 3D view.</p>
      </template>
      <template v-else>
        <p class="empty-title">No devices yet</p>
        <p>The house app sends its devices when it starts. If this stays empty, it needs updating to a version with the 3D view.</p>
      </template>
    </div>

    <HouseDeviceModal
      v-if="selectedDevice && !arranging"
      :device="selectedDevice"
      :room-name="selectedRoomName"
      :pending="pending"
      :z-index="1000"
      :docked="true"
      :zigbee="zigbee"
      :device-names="deviceNames"
      @command="sendCommand"
      @close="selectedId = null"
    />
  </div>
</template>

<script setup>
import { ref, computed, watch, onMounted, onUnmounted } from 'vue'
import HouseScene from '../components/HouseScene.vue'
import HouseDeviceModal from '../components/HouseDeviceModal.vue'
import HouseArrangePanel from '../components/HouseArrangePanel.vue'
import { houseDevicesApi } from '../services/houseDevicesApi.js'
import { useSignalR } from '../composables/useSignalR.js'
import { getHouseId } from '../utils/cookies.js'
import {
  applyStateChanges, roomForArea, roomById, roomAt, deviceKey, arrangement, defaultLayout, layoutJson,
  moveFurniture, rotateFurniture, resizeFurniture, placeOpening, resizeOpening, flipHinge, flipSwing, duplicateItem,
  EDIT_STEPS, PLAN_UNIT_METRES,
} from '../utils/houseModel.js'
import { FURNITURE_NAMES } from '../components/houseFurniture.js'
import { heatingApi } from '../services/heatingApi.js'
import { detectorStates, whereEveryoneIs, placeHouseholdAt } from '../utils/household.js'

const FLOOR_OPTIONS = [
  { id: 'all', label: 'Both floors' },
  { id: 'ground', label: 'Ground floor' },
  { id: 'first', label: 'First floor' },
]
// The house app streams states for 10 minutes after each request; ask again well before that runs out.
const WATCH_RENEW_MS = 4 * 60 * 1000
const COMMAND_TIMEOUT_MS = 20 * 1000

const sceneRef = ref(null)
const devices = ref([])
const updatedAt = ref(null)
const lastLiveAt = ref(null)
const loaded = ref(false)
const unsupported = ref(false)
// The Zigbee mesh the house app sends with its snapshot, and whether the signal lines are on.
const zigbee = ref(null)
const showSignal = ref(false)
const deviceNames = computed(() => Object.fromEntries(devices.value.map((d) => [d.id, d.name])))
const selectedId = ref(null)
// The floors shown are remembered between visits (owner, 2026-10-22).
const FLOOR_STORAGE = 'house3d-floor'
function loadFloorMode() {
  try {
    const saved = localStorage.getItem(FLOOR_STORAGE)
    return FLOOR_OPTIONS.some((option) => option.id === saved) ? saved : 'all'
  } catch {
    return 'all'
  }
}
const floorMode = ref(loadFloorMode())
watch(floorMode, (mode) => {
  try {
    localStorage.setItem(FLOOR_STORAGE, mode)
  } catch {
    // Storage refused (a private window): the choice lasts until the page is closed.
  }
})
const pending = ref({})
const now = ref(Date.now())

const houseId = getHouseId()
const signalR = houseId ? useSignalR(houseId) : null
const commandEntities = new Map() // commandId -> entityId
let renewTimer = null
let clockTimer = null

// ---- Arrange mode: everything the owner moves is kept in this browser until the JSON is sent back and made the
// house's layout in utils/houseModel.js.
// Bumped whenever an arrangement is made the house's own in houseModel.js, so an old saved copy can't hide it.
const ARRANGEMENT_STORAGE = 'house3d-arrangement-v3'
function loadArrangement() {
  try {
    return JSON.parse(localStorage.getItem(ARRANGEMENT_STORAGE) ?? 'null')
  } catch {
    return null
  }
}
const saved = loadArrangement()
const arranging = ref(false)
const picked = ref(null)
const overrides = ref(saved?.overrides ?? {})
const layout = ref(saved?.layout ?? defaultLayout())
watch([overrides, layout], () => {
  try {
    localStorage.setItem(ARRANGEMENT_STORAGE, JSON.stringify({ overrides: overrides.value, layout: layout.value }))
  } catch {
    // Private windows can refuse storage; the arrangement then lasts until the page is closed.
  }
}, { deep: true })

// ---- Where Ruth and Ste are (utils/household.js): their phones come from the room states the house app publishes,
// their desks and the TVs from the devices. The key changes only when someone moves, so a state update that moves
// nobody doesn't redraw the house. Arrange mode shows everyone at their usual place, to be edited.
const roomStates = ref([])
async function loadRoomStates() {
  try {
    const response = await heatingApi.getRoomStates()
    roomStates.value = response?.roomStates ?? []
  } catch {
    // reportApiError has said so; without room states the phones read as away and Ruth and Ste aren't drawn.
  }
}
const householdKey = computed(() => JSON.stringify(whereEveryoneIs({
  devices: devices.value,
  detectors: detectorStates(roomStates.value),
  now: new Date(now.value),
})))
const displayLayout = computed(() => {
  if (arranging.value) return layout.value
  return { ...layout.value, furniture: placeHouseholdAt(layout.value.furniture, JSON.parse(householdKey.value)) }
})

function toggleArranging() {
  arranging.value = !arranging.value
  picked.value = null
  selectedId.value = null
}

function onSelect(target) {
  if (arranging.value) picked.value = target
  else selectedId.value = target?.kind === 'device' ? target.id : null
}

/** Every device's current spot (built-in or moved), keyed as the JSON keys it. */
const spots = computed(() => arrangement(devices.value, overrides.value))

function setDeviceSpot(device, change) {
  const key = deviceKey(device, devices.value)
  overrides.value = { ...overrides.value, [key]: { ...spots.value[key], ...change } }
}

function onMove({ kind, id, x, y }) {
  if (kind === 'device') {
    const device = devices.value.find((d) => d.id === id)
    const current = device && spots.value[deviceKey(device, devices.value)]
    const room = current && roomAt(roomById(current.room).floor, x, y)
    // Dropped outside every room: put it back where it was.
    if (!room) { overrides.value = { ...overrides.value }; return }
    setDeviceSpot(device, { room: room.id, x, y })
  } else if (kind === 'furniture') {
    layout.value = { ...layout.value, furniture: layout.value.furniture.map((f) => (f.id === id ? moveFurniture(f, x, y) : f)) }
  } else {
    layout.value = { ...layout.value, openings: layout.value.openings.map((o) => (o.id === id ? placeOpening(o, x, y) : o)) }
  }
  picked.value = { kind, id }
}

function applyAction(action) {
  const p = picked.value
  if (!p) return
  if (p.kind === 'device') {
    const device = devices.value.find((d) => d.id === p.id)
    const spot = device && spots.value[deviceKey(device, devices.value)]
    if (!spot) return
    const turn = { 'rotate-left': EDIT_STEPS.rotation, 'rotate-right': -EDIT_STEPS.rotation }[action]
    if (turn) setDeviceSpot(device, { rotation: (((spot.rotation + turn) % 360) + 360) % 360 })
    if (action === 'higher') setDeviceSpot(device, { height: Math.round((spot.height + 0.05) * 100) / 100 })
    if (action === 'lower') setDeviceSpot(device, { height: Math.max(0, Math.round((spot.height - 0.05) * 100) / 100) })
    return
  }
  const list = p.kind === 'furniture' ? 'furniture' : 'openings'
  const items = layout.value[list]
  const item = items.find((i) => i.id === p.id)
  if (!item) return
  const step = EDIT_STEPS.size
  const change = {
    'rotate-left': () => rotateFurniture(item, EDIT_STEPS.rotation),
    'rotate-right': () => rotateFurniture(item, -EDIT_STEPS.rotation),
    longer: () => resizeFurniture(item, step * 2, 0),
    shorter: () => resizeFurniture(item, -step * 2, 0),
    deeper: () => resizeFurniture(item, 0, step * 2),
    shallower: () => resizeFurniture(item, 0, -step * 2),
    wider: () => resizeOpening(item, step * 2),
    narrower: () => resizeOpening(item, -step * 2),
    'flip-hinge': () => flipHinge(item),
    'flip-swing': () => flipSwing(item),
  }[action]
  if (change) {
    layout.value = { ...layout.value, [list]: items.map((i) => (i.id === p.id ? change() : i)) }
  } else if (action === 'duplicate') {
    const copy = duplicateItem(items, item)
    layout.value = { ...layout.value, [list]: [...items, copy] }
    picked.value = { kind: p.kind, id: copy.id }
  } else if (action === 'delete') {
    layout.value = { ...layout.value, [list]: items.filter((i) => i.id !== p.id) }
    picked.value = null
  }
}

function resetArrangement() {
  overrides.value = {}
  layout.value = defaultLayout()
  picked.value = null
}

/** What the panel says about the picked thing. */
const pickedInfo = computed(() => {
  const p = picked.value
  if (!p) return null
  if (p.kind === 'device') {
    const device = devices.value.find((d) => d.id === p.id)
    const spot = device && spots.value[deviceKey(device, devices.value)]
    if (!device || !spot) return null
    return { kind: 'device', title: device.name, subtitle: `${roomById(spot.room)?.name ?? ''} · ${spot.height} m up · turned ${spot.rotation}°` }
  }
  if (p.kind === 'furniture') {
    const item = layout.value.furniture.find((f) => f.id === p.id)
    if (!item) return null
    const name = item.person?.name ?? FURNITURE_NAMES[item.label] ?? FURNITURE_NAMES[item.type] ?? item.type
    const size = `${(Math.max(item.w, item.h) * PLAN_UNIT_METRES).toFixed(2)} × ${(Math.min(item.w, item.h) * PLAN_UNIT_METRES).toFixed(2)} m`
    return { kind: 'furniture', title: name, subtitle: `${item.id} · ${size}${item.rotation ? ` · turned ${item.rotation}°` : ''}` }
  }
  const opening = layout.value.openings.find((o) => o.id === p.id)
  if (!opening) return null
  const name = opening.type === 'window' ? 'Window' : opening.type === 'open' ? 'Open gap' : opening.sliding ? 'Sliding door' : opening.external ? 'Outside door' : 'Door'
  return {
    kind: 'opening',
    title: name,
    subtitle: `${opening.id} · ${((opening.to - opening.from) * PLAN_UNIT_METRES).toFixed(2)} m wide`,
    door: opening.type === 'door',
    sliding: !!opening.sliding,
  }
})

/** The layout as JSON, one device, piece of furniture or opening per line so it reads and diffs well. */
const layoutText = computed(() => {
  const data = layoutJson(devices.value, overrides.value, layout.value)
  const devicesText = Object.entries(data.devices).map(([key, spot]) => `    ${JSON.stringify(key)}: ${JSON.stringify(spot)}`).join(',\n')
  const list = (items) => items.map((item) => `    ${JSON.stringify(item)}`).join(',\n')
  return `{\n  "devices": {\n${devicesText}\n  },\n  "furniture": [\n${list(data.furniture)}\n  ],\n  "openings": [\n${list(data.openings)}\n  ]\n}`
})

const selectedDevice = computed(() => devices.value.find((d) => d.id === selectedId.value) ?? null)
const selectedRoomName = computed(() => (selectedDevice.value ? roomForArea(selectedDevice.value.area).name : ''))

const liveState = computed(() => {
  if (!loaded.value) return { text: 'Loading…', tone: 'waiting' }
  if (lastLiveAt.value && now.value - lastLiveAt.value < 60 * 1000) return { text: 'Live', tone: 'live' }
  if (signalR?.isConnected.value && updatedAt.value) {
    const time = new Date(updatedAt.value).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
    return { text: `As of ${time}`, tone: 'waiting' }
  }
  return { text: 'Not connected', tone: 'offline' }
})

async function loadDevices() {
  try {
    const snapshot = await houseDevicesApi.getDevices()
    devices.value = snapshot.devices ?? []
    zigbee.value = snapshot.zigbee ?? null
    updatedAt.value = snapshot.updatedAt ?? null
    unsupported.value = snapshot.unsupported === true
  } catch {
    // reportApiError has told the user.
  } finally {
    loaded.value = true
  }
}

function setPending(entityId, value) {
  const next = { ...pending.value }
  if (value) next[entityId] = value
  else delete next[entityId]
  pending.value = next
}

async function sendCommand(entity, action) {
  setPending(entity.entityId, { state: 'sending' })
  try {
    const commandId = await houseDevicesApi.sendCommand(entity.entityId, action)
    commandEntities.set(commandId, entity.entityId)
    setTimeout(() => {
      if (commandEntities.has(commandId)) {
        commandEntities.delete(commandId)
        setPending(entity.entityId, { state: 'failed', message: 'No answer from the house. Is the house app running and up to date?' })
      }
    }, COMMAND_TIMEOUT_MS)
  } catch {
    setPending(entity.entityId, { state: 'failed', message: "Couldn't reach the server. Check the connection and try again." })
  }
}

function onCommandResult(result) {
  const entityId = commandEntities.get(result.commandId)
  if (!entityId) return
  commandEntities.delete(result.commandId)
  if (result.ok) {
    setPending(entityId, { state: 'done' })
    setTimeout(() => { if (pending.value[entityId]?.state === 'done') setPending(entityId, null) }, 3000)
  } else {
    setPending(entityId, { state: 'failed', message: result.message })
  }
}

function onStates(payload) {
  devices.value = applyStateChanges(devices.value, payload?.changes)
  lastLiveAt.value = Date.now()
}

function watchLive() {
  houseDevicesApi.watch()
}

async function onVisibility() {
  if (document.visibilityState !== 'visible' || !signalR) return
  await signalR.ensureConnected()
  await Promise.all([loadDevices(), loadRoomStates()])
  watchLive()
}

onMounted(async () => {
  await Promise.all([loadDevices(), loadRoomStates()])
  if (signalR) {
    await signalR.connect()
    signalR.on('house-devices-changed', loadDevices)
    signalR.on('house-device-states', onStates)
    signalR.on('house-device-command-result', onCommandResult)
    signalR.on('room-states-changed', loadRoomStates)
  }
  watchLive()
  renewTimer = setInterval(() => { if (document.visibilityState === 'visible') watchLive() }, WATCH_RENEW_MS)
  clockTimer = setInterval(() => { now.value = Date.now() }, 15 * 1000)
  document.addEventListener('visibilitychange', onVisibility)
})

onUnmounted(async () => {
  clearInterval(renewTimer)
  clearInterval(clockTimer)
  document.removeEventListener('visibilitychange', onVisibility)
  if (signalR) {
    signalR.off('house-devices-changed')
    signalR.off('house-device-states')
    signalR.off('house-device-command-result')
    signalR.off('room-states-changed')
    await signalR.disconnect()
  }
})
</script>

<style scoped>
.house-3d-view {
  position: relative;
  flex: 1;
  min-height: 0;
  display: flex;
}

.scene {
  flex: 1;
  min-height: 0;
}

.toolbar {
  position: absolute;
  top: 0.75rem;
  left: max(0.75rem, env(safe-area-inset-left, 0px));
  right: max(0.75rem, env(safe-area-inset-right, 0px));
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  pointer-events: none;
}

.floor-switch,
.tool-btn {
  pointer-events: auto;
}

.floor-switch {
  display: inline-flex;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  padding: 0.2rem;
  box-shadow: 0 2px 8px var(--shadow);
}

.floor-btn,
.tool-btn {
  font: inherit;
  font-size: 0.85rem;
  border: none;
  background: none;
  color: var(--text-secondary);
  padding: 0.35rem 0.7rem;
  border-radius: 6px;
  cursor: pointer;
}

.tool-btn:disabled {
  opacity: 0.5;
  cursor: default;
}

.tool-btn.active {
  border-color: var(--color-primary);
  color: var(--color-primary);
}

.floor-btn.active {
  background: var(--color-primary);
  color: #fff;
}

.tool-btn {
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  box-shadow: 0 2px 8px var(--shadow);
  border-radius: 8px;
}

.floor-btn:focus-visible,
.tool-btn:focus-visible {
  outline: 2px solid var(--color-primary);
  outline-offset: 2px;
}

.status-bar {
  position: absolute;
  bottom: max(0.75rem, env(safe-area-inset-bottom, 0px));
  left: max(0.75rem, env(safe-area-inset-left, 0px));
  right: max(0.75rem, env(safe-area-inset-right, 0px));
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.4rem 0.75rem;
  pointer-events: none;
  font-size: 0.8rem;
}

.live-chip {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 999px;
  padding: 0.2rem 0.65rem;
  color: var(--text-secondary);
}

.live-chip::before {
  content: '';
  width: 0.5rem;
  height: 0.5rem;
  border-radius: 50%;
  background: var(--text-tertiary);
}

.live-chip.live::before { background: var(--color-success); }
.live-chip.offline::before { background: var(--color-danger); }

.hint {
  color: var(--text-tertiary);
}

.empty {
  position: absolute;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -50%);
  width: min(90%, 360px);
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 12px;
  padding: 1rem 1.25rem;
  box-shadow: 0 8px 24px var(--shadow-md);
  color: var(--text-secondary);
  text-align: center;
}

.empty p {
  margin: 0;
}

.empty-title {
  font-weight: 600;
  color: var(--text-primary);
  margin-bottom: 0.4rem !important;
}

@media (max-width: 480px) {
  .hint {
    display: none;
  }
}
</style>

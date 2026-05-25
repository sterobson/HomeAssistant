import { ref, computed } from 'vue'
import { heatingConfigApi } from '../services/heatingConfigApi.js'

// Singleton reactive state
const config = ref({ rooms: [], rules: [] })
const loading = ref(false)
const saving = ref(false)
const error = ref(null)

let configLoaded = false

function generateId() {
  return crypto.randomUUID()
}

async function loadConfig() {
  if (configLoaded) return
  try {
    loading.value = true
    error.value = null
    const response = await heatingConfigApi.getConfig()
    config.value = response || { rooms: [], rules: [] }
    configLoaded = true
  } catch (err) {
    console.error('Failed to load heating config:', err)
    error.value = err.message
  } finally {
    loading.value = false
  }
}

async function forceReloadConfig() {
  configLoaded = false
  await loadConfig()
}

async function saveConfig() {
  try {
    saving.value = true
    error.value = null
    await heatingConfigApi.saveConfig(config.value)
  } catch (err) {
    console.error('Failed to save heating config:', err)
    error.value = err.message
    throw err
  } finally {
    saving.value = false
  }
}

// Room CRUD
function addRoom(room) {
  config.value.rooms.push({
    id: generateId(),
    name: '',
    floor: 0,
    priority: 2,
    devices: [],
    schedules: [],
    ...room
  })
  return saveConfig()
}

function updateRoom(roomId, updates) {
  const room = config.value.rooms.find(r => r.id === roomId)
  if (room) {
    Object.assign(room, updates)
    return saveConfig()
  }
}

function deleteRoom(roomId) {
  config.value.rooms = config.value.rooms.filter(r => r.id !== roomId)
  return saveConfig()
}

// Device CRUD
function addDevice(roomId, device) {
  const room = config.value.rooms.find(r => r.id === roomId)
  if (room) {
    room.devices.push({
      id: generateId(),
      name: '',
      type: 0,
      sensorEntityId: null,
      targetEntityId: null,
      switchEntityId: null,
      powerSensorEntityId: null,
      controlMode: null,
      ruleIds: [],
      ruleCombinator: 0,
      ...device
    })
    return saveConfig()
  }
}

function updateDevice(roomId, deviceId, updates) {
  const room = config.value.rooms.find(r => r.id === roomId)
  if (room) {
    const device = room.devices.find(d => d.id === deviceId)
    if (device) {
      Object.assign(device, updates)
      return saveConfig()
    }
  }
}

function deleteDevice(roomId, deviceId) {
  const room = config.value.rooms.find(r => r.id === roomId)
  if (room) {
    room.devices = room.devices.filter(d => d.id !== deviceId)
    return saveConfig()
  }
}

// Rule CRUD
function addRule(rule) {
  config.value.rules.push({
    id: generateId(),
    name: '',
    ...rule
  })
  return saveConfig()
}

function updateRule(ruleId, updates) {
  const rule = config.value.rules.find(r => r.id === ruleId)
  if (rule) {
    Object.assign(rule, updates)
    return saveConfig()
  }
}

function deleteRule(ruleId) {
  // Remove rule from all devices that reference it
  for (const room of config.value.rooms) {
    for (const device of room.devices) {
      device.ruleIds = device.ruleIds.filter(id => id !== ruleId)
    }
  }
  config.value.rules = config.value.rules.filter(r => r.id !== ruleId)
  return saveConfig()
}

// Rule binding
function bindRuleToDevice(roomId, deviceId, ruleId) {
  const room = config.value.rooms.find(r => r.id === roomId)
  if (room) {
    const device = room.devices.find(d => d.id === deviceId)
    if (device && !device.ruleIds.includes(ruleId)) {
      device.ruleIds.push(ruleId)
      return saveConfig()
    }
  }
}

function unbindRuleFromDevice(roomId, deviceId, ruleId) {
  const room = config.value.rooms.find(r => r.id === roomId)
  if (room) {
    const device = room.devices.find(d => d.id === deviceId)
    if (device) {
      device.ruleIds = device.ruleIds.filter(id => id !== ruleId)
      return saveConfig()
    }
  }
}

function setDeviceRuleCombinator(roomId, deviceId, combinator) {
  const room = config.value.rooms.find(r => r.id === roomId)
  if (room) {
    const device = room.devices.find(d => d.id === deviceId)
    if (device) {
      device.ruleCombinator = combinator
      return saveConfig()
    }
  }
}

// Schedule CRUD
function addSchedule(roomId, schedule) {
  const room = config.value.rooms.find(r => r.id === roomId)
  if (room) {
    room.schedules.push({
      id: generateId(),
      time: '08:00',
      temperature: 20,
      rampUpMinutes: 0,
      days: 127,
      conditions: 0,
      ...schedule
    })
    return saveConfig()
  }
}

function updateSchedule(roomId, scheduleId, updates) {
  const room = config.value.rooms.find(r => r.id === roomId)
  if (room) {
    const schedule = room.schedules.find(s => s.id === scheduleId)
    if (schedule) {
      Object.assign(schedule, updates)
      return saveConfig()
    }
  }
}

function deleteSchedule(roomId, scheduleId) {
  const room = config.value.rooms.find(r => r.id === roomId)
  if (room) {
    room.schedules = room.schedules.filter(s => s.id !== scheduleId)
    return saveConfig()
  }
}

// Computed helpers
const rooms = computed(() => config.value.rooms || [])
const rules = computed(() => config.value.rules || [])

const roomsByFloor = computed(() => {
  const floors = new Map()
  for (const room of rooms.value) {
    const floor = room.floor ?? 0
    if (!floors.has(floor)) {
      floors.set(floor, [])
    }
    floors.get(floor).push(room)
  }
  // Sort floors descending (highest first)
  return [...floors.entries()]
    .sort((a, b) => b[0] - a[0])
    .map(([floor, rooms]) => ({ floor, rooms }))
})

function moveRoom(roomId, targetFloor, targetIndex) {
  const room = config.value.rooms.find(r => r.id === roomId)
  if (!room) return

  room.floor = targetFloor

  // Reorder: collect rooms on the target floor, remove this room, insert at targetIndex
  const floorRooms = config.value.rooms.filter(r => r.floor === targetFloor && r.id !== roomId)
  const otherRooms = config.value.rooms.filter(r => r.floor !== targetFloor && r.id !== roomId)

  floorRooms.splice(targetIndex, 0, room)
  config.value.rooms = [...otherRooms, ...floorRooms]

  return saveConfig()
}

function reorderRoomOnFloor(roomId, targetFloor, targetIndex) {
  const room = config.value.rooms.find(r => r.id === roomId)
  if (!room) return

  const oldFloor = room.floor
  room.floor = targetFloor

  // Get all rooms grouped, maintaining order
  const floorMap = new Map()
  for (const r of config.value.rooms) {
    const f = r.floor ?? 0
    if (!floorMap.has(f)) floorMap.set(f, [])
    if (r.id !== roomId) floorMap.get(f).push(r)
  }

  // Ensure target floor exists
  if (!floorMap.has(targetFloor)) floorMap.set(targetFloor, [])

  // Insert at position
  const targetRooms = floorMap.get(targetFloor)
  const clampedIndex = Math.min(targetIndex, targetRooms.length)
  targetRooms.splice(clampedIndex, 0, room)

  // Clean up old floor if it was different and is now empty
  if (oldFloor !== targetFloor && floorMap.has(oldFloor) && floorMap.get(oldFloor).length === 0) {
    floorMap.delete(oldFloor)
  }

  // Rebuild rooms array (preserve floor order: highest first in array)
  const sortedFloors = [...floorMap.keys()].sort((a, b) => b - a)
  config.value.rooms = sortedFloors.flatMap(f => floorMap.get(f))

  return saveConfig()
}

function getRuleById(ruleId) {
  return config.value.rules.find(r => r.id === ruleId)
}

export function useHeatingConfig() {
  return {
    config,
    loading,
    saving,
    error,
    rooms,
    rules,
    roomsByFloor,
    loadConfig,
    forceReloadConfig,
    saveConfig,
    addRoom,
    updateRoom,
    deleteRoom,
    addDevice,
    updateDevice,
    deleteDevice,
    addRule,
    updateRule,
    deleteRule,
    bindRuleToDevice,
    unbindRuleFromDevice,
    setDeviceRuleCombinator,
    addSchedule,
    updateSchedule,
    deleteSchedule,
    moveRoom,
    reorderRoomOnFloor,
    getRuleById
  }
}

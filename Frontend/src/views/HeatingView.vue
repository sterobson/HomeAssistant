<template>
  <div class="heating-view">
    <div v-if="loading" class="loading-state">
      <div class="spinner"></div>
      <p>Loading schedules...</p>
    </div>

    <div v-else-if="error" class="error-state">
      <svg width="48" height="48" viewBox="0 0 16 16" fill="currentColor">
        <path d="M8 0a8 8 0 100 16A8 8 0 008 0zM7 4h2v5H7V4zm0 6h2v2H7v-2z"/>
      </svg>
      <p>{{ error }}</p>
      <button class="btn btn-retry" @click="loadSchedules">Retry</button>
    </div>

    <div v-else class="rooms-container">
      <div v-if="rooms.length === 0" class="empty-state">
        <svg width="64" height="64" viewBox="0 0 16 16" fill="currentColor">
          <path d="M2 2a2 2 0 012-2h8a2 2 0 012 2v12a2 2 0 01-2 2H4a2 2 0 01-2-2V2zm2-1a1 1 0 00-1 1v12a1 1 0 001 1h8a1 1 0 001-1V2a1 1 0 00-1-1H4z"/>
          <path d="M5 4h6v1H5V4zm0 2h6v1H5V6zm0 2h6v1H5V8z"/>
        </svg>
        <p>No rooms configured yet</p>
      </div>

      <div v-if="rooms.length > 0" class="layout-toolbar">
        <button v-if="editLayout" class="btn-layout" title="Add an empty group at the top level" @click="openAddGroup">
          Add group
        </button>
        <button
          class="btn-layout"
          :class="{ active: editLayout }"
          :title="editLayout ? 'Finish editing the layout' : 'Reorder rooms, choose icons, and make groups'"
          @click="editLayout = !editLayout"
        >
          {{ editLayout ? 'Done' : 'Edit layout' }}
        </button>
      </div>

      <!-- The layout is a tree of any depth; LayoutNode renders itself for each group's children. -->
      <LayoutNode v-for="node in layoutTree" :key="node.key" :node="node" />
    </div>

    <IconPickerModal
      v-if="iconTarget"
      :title="iconTarget.title"
      :current-icon="iconTarget.current"
      :editable-name="true"
      :current-name="iconTarget.name ?? ''"
      :name-optional="iconTarget.kind === 'room'"
      :name-label="iconTarget.kind === 'room' ? 'Display name' : 'Name'"
      :name-placeholder="iconTarget.canonicalName ?? ''"
      :name-hint="iconTarget.kind === 'room' ? `Shown in the app only. This room stays wired to its radiator and sensors as ${iconTarget.canonicalName}, so renaming it is always safe. Leave empty to use that name.` : ''"
      :deletable="iconTarget.kind === 'group'"
      @save="handleIconSave"
      @delete="handleDeleteGroup"
      @cancel="iconTarget = null"
    />

    <MoveToModal
      v-if="moveTarget"
      :title="moveTarget.title"
      :targets="moveTarget.targets"
      @select="handleMoveTo"
      @cancel="moveTarget = null"
    />

    <Toast
      v-if="toast.visible"
      :message="toast.message"
      :type="toast.type"
      :duration="toast.duration"
      @close="toast.visible = false"
    />
  </div>
</template>

<script setup>
import { ref, onMounted, onUnmounted, reactive, computed, watch, provide, toRef } from 'vue'
import RoomCard from '../components/RoomCard.vue'
import LayoutNode from '../components/LayoutNode.vue'
import MoveToModal from '../components/MoveToModal.vue'
import IconPickerModal from '../components/IconPickerModal.vue'
import Toast from '../components/Toast.vue'
import { heatingApi } from '../services/heatingApi.js'
import { useSignalR } from '../composables/useSignalR.js'
import { getHouseId } from '../utils/cookies.js'
import { roomLabel } from '../utils/rooms.js'
import {
  fromLayoutDto, toLayoutDto, canMove, move, moveTo, moveTargets, addGroup, updateGroup, deleteGroup,
  newGroupId, normaliseIcon, roomIdsIn
} from '../utils/roomLayout.js'
import { HEATING_LAYOUT } from '../utils/layoutContext.js'
import { drivableDevices, presenceZones } from '../utils/deviceRules.js'

const props = defineProps({
  occupancyFilter: {
    type: String,
    default: null // 'occupied', 'vacant', or null for all
  }
})

const emit = defineEmits(['house-state-changed'])

const rooms = ref([])
const roomStates = ref([])
const houseOccupancyState = ref(0) // 0 = Home, 1 = Away
const loading = ref(true)
const error = ref(null)
const statePollingInterval = ref(null)
const expandedRoomId = ref(null)
// Groups open independently, any number at once (a nested group needs its parents open too).
const expandedGroupIds = ref(new Set())

// How the rooms are arranged: a tree of rooms and groups to any depth. See utils/roomLayout.js.
const layoutTree = ref([])
const roomsById = computed(() => new Map(rooms.value.map(room => [room.id, room])))
// What the device rules editor needs: the zones a presence rule can name, and every device it could be saved for.
const presenceZoneList = computed(() => presenceZones(rooms.value))
const drivableDeviceList = computed(() => drivableDevices(rooms.value))

// Layout editing: reorder, move into groups, choose icons, make and delete groups.
const editLayout = ref(false)
const iconTarget = ref(null) // { kind: 'room' | 'group' | 'new-group', id?, name?, title, current }
const moveTarget = ref(null) // { key, title, targets }
const lastHiddenTime = ref(null)
const toast = reactive({
  visible: false,
  message: '',
  type: 'success',
  duration: 3000
})

// Get houseId from cookies (reactive so it updates when cookie changes)
const houseId = computed(() => getHouseId())

// Watch for occupancy filter changes and update house state accordingly
watch(() => props.occupancyFilter, async (newFilter, oldFilter) => {
  // Only update if filter actually changed and we have rooms loaded
  if (newFilter === oldFilter || rooms.value.length === 0) {
    return
  }

  let newState = houseOccupancyState.value

  // Map filter to house state
  if (newFilter === 'occupied') {
    newState = 0 // Home
  } else if (newFilter === 'vacant') {
    newState = 1 // Away
  } else {
    // No filter - don't change state
    return
  }

  // Only update if state actually changed
  if (newState !== houseOccupancyState.value) {
    houseOccupancyState.value = newState

    // Emit the change to App.vue
    emit('house-state-changed', newState)

    // Save the updated state to the backend
    try {
      const cleanedData = cleanRoomsForApi(rooms.value)
      await heatingApi.setSchedules(cleanedData)

      const stateLabel = newState === 0 ? 'Home (Occupied)' : 'Away (Vacant)'
      showToast(`House state changed to ${stateLabel}`, 'info')
    } catch (err) {
      console.error('Error saving house state:', err)
      showToast('Failed to save house state', 'error')
    }
  }
})

// SignalR connection - will be recreated when houseId changes
let signalR = useSignalR(houseId.value)

// Cookie helpers
const EXPANDED_ROOM_COOKIE = 'heating-app-expanded-room'

const setCookie = (name, value, days = 365) => {
  const expires = new Date()
  expires.setTime(expires.getTime() + days * 24 * 60 * 60 * 1000)
  document.cookie = `${name}=${encodeURIComponent(value)};expires=${expires.toUTCString()};path=/`
}

const getCookie = (name) => {
  const nameEQ = name + "="
  const ca = document.cookie.split(';')
  for (let i = 0; i < ca.length; i++) {
    let c = ca[i]
    while (c.charAt(0) === ' ') c = c.substring(1, c.length)
    if (c.indexOf(nameEQ) === 0) return decodeURIComponent(c.substring(nameEQ.length, c.length))
  }
  return null
}

const deleteCookie = (name) => {
  document.cookie = `${name}=;expires=Thu, 01 Jan 1970 00:00:00 UTC;path=/`
}

// Initialize SignalR connection
const initializeConnection = async () => {
  if (!houseId.value) {
    return
  }

  // Disconnect existing connection if any
  if (signalR) {
    signalR.off('room-states-changed')
    signalR.off('schedules-changed')
    await signalR.disconnect()
  }

  // Create new SignalR connection with current houseId
  signalR = useSignalR(houseId.value)

  // Connect to SignalR
  try {
    await signalR.connect()

    // Listen for room states changes
    signalR.on('room-states-changed', (data) => {
      console.log('Room states changed:', data)
      loadRoomStates()
    })

    // Listen for schedules changes
    signalR.on('schedules-changed', (data) => {
      console.log('Schedules changed:', data)
      loadSchedules(true) // Silent update - no loading spinner
    })
  } catch (err) {
    console.error('Failed to connect to SignalR:', err)
    // Fallback to polling if SignalR fails
    if (statePollingInterval.value) {
      clearInterval(statePollingInterval.value)
    }
    statePollingInterval.value = setInterval(() => {
      loadRoomStates()
    }, 30000)
  }
}

// Watch for houseId changes
watch(houseId, async (newHouseId, oldHouseId) => {
  // Clear any existing polling interval
  if (statePollingInterval.value) {
    clearInterval(statePollingInterval.value)
    statePollingInterval.value = null
  }

  if (!newHouseId) {
    // House disconnected
    loading.value = false
    error.value = 'No house connected. Please select a house from settings.'
    rooms.value = []
    return
  }

  // House connected or changed - clear error and reload
  error.value = null
  loading.value = true

  // Load schedules
  await loadSchedules()

  // Initialize SignalR connection
  await initializeConnection()
})

// Page Visibility API handler - called when tab is hidden/shown
const handleVisibilityChange = async () => {
  if (document.hidden) {
    // Tab became hidden - record the time
    lastHiddenTime.value = Date.now()
    console.log('Tab hidden at:', new Date(lastHiddenTime.value))
  } else {
    // Tab became visible - check if we need to refresh
    console.log('Tab visible')

    if (lastHiddenTime.value) {
      const hiddenDuration = (Date.now() - lastHiddenTime.value) / 1000 // seconds
      console.log(`Tab was hidden for ${hiddenDuration.toFixed(0)} seconds`)

      // If hidden for more than 30 seconds, refresh everything
      if (hiddenDuration > 30) {
        console.log('Tab was hidden for a while, refreshing connection and data...')

        // Ensure SignalR is connected
        if (signalR && houseId.value) {
          try {
            const reconnected = await signalR.ensureConnected()
            if (reconnected) {
              console.log('SignalR reconnected after tab became visible')
            }
          } catch (err) {
            console.error('Failed to ensure SignalR connection:', err)
          }
        }

        // Refresh schedules and states
        await loadSchedules()
        showToast('Data refreshed', 'info', 2000)
      }
    }

    lastHiddenTime.value = null
  }
}

onMounted(async () => {
  // Load previously expanded room from cookie
  const savedExpandedRoom = getCookie(EXPANDED_ROOM_COOKIE)
  if (savedExpandedRoom && savedExpandedRoom !== 'null') {
    expandedRoomId.value = savedExpandedRoom
  }

  // Only load schedules and connect to SignalR if we have a house ID
  if (!houseId.value) {
    loading.value = false
    error.value = 'No house connected. Please select a house from settings.'
    return
  }

  loadSchedules()
  await initializeConnection()

  // Set up Page Visibility API to handle tab backgrounding/foregrounding
  document.addEventListener('visibilitychange', handleVisibilityChange)
})

onUnmounted(async () => {
  if (statePollingInterval.value) {
    clearInterval(statePollingInterval.value)
  }

  // Remove visibility change listener
  document.removeEventListener('visibilitychange', handleVisibilityChange)

  // Disconnect from SignalR
  if (signalR) {
    signalR.off('room-states-changed')
    signalR.off('schedules-changed')
    await signalR.disconnect()
  }
})

// Helper function to sort schedules by time
const sortSchedules = (schedules) => {
  return schedules.sort((a, b) => a.time.localeCompare(b.time))
}

// Merge schedules with room states for display
const mergeSchedulesWithStates = (schedules, states) => {
  return schedules.map(room => {
    const state = states.find(s => s.roomId === room.id)

    return {
      ...room,
      currentTemperature: state?.currentTemperature ?? null,
      heatingActive: state?.heatingActive ?? false,
      activeScheduleId: state?.activeScheduleTrackId ?? null,
      capabilities: state?.capabilities ?? 3, // Default to both capabilities if state not found
      // What the zone is bound to, from the NetDaemon app. Display only; never sent back.
      devices: state?.devices ?? []
    }
  })
}

// Clean room data for API - remove UI-only properties
const cleanRoomsForApi = (rooms) => {
  return {
    houseOccupancyState: houseOccupancyState.value,
    // The arrangement of rooms and groups. Like every other field it must be sent, because this payload
    // replaces the stored blob: leaving it out would reset everyone's layout on the next save.
    layout: toLayoutDto(layoutTree.value),
    rooms: rooms.map(room => ({
      id: room.id,
      name: room.name,
      // Must be round-tripped: this whitelist is the whole payload, so omitting
      // displayName here would wipe every room's display name on the next save.
      displayName: room.displayName ?? null,
      group: room.group ?? null,
      icon: room.icon ?? null,
      boost: room.boost,
      schedules: room.schedules.map(schedule => ({
        id: schedule.id,
        time: schedule.time,
        temperature: schedule.temperature,
        rampUpMinutes: schedule.rampUpMinutes || 30,
        days: schedule.days || 0,
        conditions: schedule.conditions || 0,
        conditionOperator: schedule.conditionOperator || 1
      }))
    }))
  }
}

const loadSchedules = async (silent = false) => {
  if (!silent) {
    loading.value = true
    error.value = null
  }

  try {
    const data = await heatingApi.getSchedules()

    // Store house occupancy state
    houseOccupancyState.value = data.houseOccupancyState ?? 0

    // Emit initial state to App.vue
    emit('house-state-changed', houseOccupancyState.value)

    // Sort schedules for each room
    data.rooms.forEach(room => {
      sortSchedules(room.schedules)
    })

    // Load room states separately
    try {
      const statesData = await heatingApi.getRoomStates()
      roomStates.value = statesData.roomStates || []
    } catch (stateErr) {
      console.warn('Failed to load room states, using defaults:', stateErr)
      roomStates.value = []
    }

    // Merge schedules with states
    rooms.value = mergeSchedulesWithStates(data.rooms, roomStates.value)
    layoutTree.value = fromLayoutDto(data.layout, rooms.value)
  } catch (err) {
    if (!silent) {
      error.value = 'Failed to load schedules. Please try again.'
    }
    console.error('Error loading schedules:', err)
  } finally {
    if (!silent) {
      loading.value = false
    }
  }
}

const loadRoomStates = async () => {
  try {
    const statesData = await heatingApi.getRoomStates()
    roomStates.value = statesData.roomStates || []

    // Update room states without affecting schedule configuration
    rooms.value = rooms.value.map(room => {
      const state = roomStates.value.find(s => s.roomId === room.id)
      return {
        ...room,
        currentTemperature: state?.currentTemperature ?? room.currentTemperature,
        heatingActive: state?.heatingActive ?? room.heatingActive,
        activeScheduleId: state?.activeScheduleTrackId ?? room.activeScheduleId,
        devices: state?.devices ?? room.devices
      }
    })
  } catch (err) {
    console.error('Error loading room states:', err)
    // Don't show error to user as this is a background update
  }
}

const handleUpdateSchedule = async (roomId, updatedSchedule) => {
  const room = rooms.value.find(r => r.id === roomId)
  if (room) {
    const scheduleIndex = room.schedules.findIndex(s => s.id === updatedSchedule.id)
    if (scheduleIndex > -1) {
      room.schedules[scheduleIndex] = updatedSchedule
      sortSchedules(room.schedules)

      // Save immediately
      try {
        const cleanedData = cleanRoomsForApi(rooms.value)
        await heatingApi.setSchedules(cleanedData)
        showToast('Schedule updated successfully', 'success')
      } catch (err) {
        console.error('Error saving schedule:', err)
        showToast('Failed to save schedule', 'error')
      }
    }
  }
}

const handleDeleteSchedule = async (roomId, scheduleId) => {
  const room = rooms.value.find(r => r.id === roomId)
  if (room) {
    room.schedules = room.schedules.filter(s => s.id !== scheduleId)
    sortSchedules(room.schedules)

    // Save immediately
    try {
      const cleanedData = cleanRoomsForApi(rooms.value)
      await heatingApi.setSchedules(cleanedData)
      showToast('Schedule deleted successfully', 'success')
    } catch (err) {
      console.error('Error saving schedule:', err)
      showToast('Failed to delete schedule', 'error')
    }
  }
}

const handleAddSchedule = async (roomId, newSchedule) => {
  const room = rooms.value.find(r => r.id === roomId)
  if (room) {
    room.schedules.push(newSchedule)
    sortSchedules(room.schedules)

    // Save immediately
    try {
      const cleanedData = cleanRoomsForApi(rooms.value)
      await heatingApi.setSchedules(cleanedData)
      showToast('Schedule added successfully', 'success')
    } catch (err) {
      console.error('Error saving schedule:', err)
      showToast('Failed to add schedule', 'error')
    }
  }
}

const showToast = (message, type = 'success', duration = 3000) => {
  toast.message = message
  toast.type = type
  toast.duration = duration
  toast.visible = true
}

const handleBoost = async (roomId, boostData) => {
  const room = rooms.value.find(r => r.id === roomId)
  if (room) {
    // Calculate start and end times in UTC
    const startTime = new Date()
    const endTime = new Date(startTime.getTime() + boostData.duration * 60 * 60 * 1000)

    room.boost = {
      startTime: startTime.toISOString(),
      endTime: endTime.toISOString(),
      temperature: boostData.temperature
    }

    // Save immediately
    try {
      const cleanedData = cleanRoomsForApi(rooms.value)
      await heatingApi.setSchedules(cleanedData)

      // Format duration text
      const totalMinutes = Math.round(boostData.duration * 60)
      const hours = Math.floor(totalMinutes / 60)
      const minutes = totalMinutes % 60
      let durationText = ''

      if (hours === 0) {
        durationText = `${minutes} minutes`
      } else if (minutes === 0) {
        durationText = hours === 1 ? '1 hour' : `${hours} hours`
      } else {
        const hourText = hours === 1 ? '1 hour' : `${hours} hours`
        durationText = `${hourText} ${minutes} minutes`
      }

      showToast(`Boost activated for ${roomLabel(room)} (${boostData.temperature}°C for ${durationText})`, 'success')
    } catch (err) {
      console.error('Error saving boost:', err)
      showToast('Failed to activate boost', 'error')
    }
  }
}

const handleCancelBoost = async (roomId) => {
  const room = rooms.value.find(r => r.id === roomId)
  if (room) {
    room.boost = null

    // Save immediately
    try {
      const cleanedData = cleanRoomsForApi(rooms.value)
      await heatingApi.setSchedules(cleanedData)
      showToast(`Boost cancelled for ${roomLabel(room)}`, 'info')
    } catch (err) {
      console.error('Error saving boost cancellation:', err)
      showToast('Failed to cancel boost', 'error')
    }
  }
}

// A group boost is the same boost on every zone in the group, at any depth, saved in one go. Like a zone
// boost it sets that exact temperature, up or down, and boosting again changes it.
const handleGroupBoost = async (groupNode, boostData) => {
  const ids = new Set(roomIdsIn(groupNode))
  const startTime = new Date()
  const endTime = new Date(startTime.getTime() + boostData.duration * 60 * 60 * 1000)
  const previous = rooms.value.map(room => room.boost)

  for (const room of rooms.value) {
    if (ids.has(room.id)) {
      room.boost = { startTime: startTime.toISOString(), endTime: endTime.toISOString(), temperature: boostData.temperature }
    }
  }

  try {
    await heatingApi.setSchedules(cleanRoomsForApi(rooms.value))
    showToast(`Boost activated for ${groupNode.name} (${boostData.temperature}°C)`, 'success')
  } catch (err) {
    console.error('Error saving group boost:', err)
    rooms.value.forEach((room, index) => { room.boost = previous[index] })
    showToast('Failed to activate boost', 'error')
  }
}

const handleCancelGroupBoost = async (groupNode) => {
  const ids = new Set(roomIdsIn(groupNode))
  const previous = rooms.value.map(room => room.boost)

  for (const room of rooms.value) {
    if (ids.has(room.id)) room.boost = null
  }

  try {
    await heatingApi.setSchedules(cleanRoomsForApi(rooms.value))
    showToast(`Boost cancelled for ${groupNode.name}`, 'info')
  } catch (err) {
    console.error('Error cancelling group boost:', err)
    rooms.value.forEach((room, index) => { room.boost = previous[index] })
    showToast('Failed to cancel boost', 'error')
  }
}

// Groups expand one at a time, matching how rooms already behave. Not persisted: the
// expanded-room cookie holds a room id, and a group has no id to store.
// ---- Layout editing --------------------------------------------------------------------------
// Each change saves immediately, like boosts and renames. If the save fails everything is put back,
// so the screen never shows an arrangement that was not stored.
const snapshot = () => ({
  rooms: rooms.value,
  tree: layoutTree.value,
  expanded: new Set(expandedGroupIds.value)
})

const saveLayout = async (previous, successMessage) => {
  try {
    await heatingApi.setSchedules(cleanRoomsForApi(rooms.value))
    if (successMessage) showToast(successMessage, 'success')
  } catch (err) {
    rooms.value = previous.rooms
    layoutTree.value = previous.tree
    expandedGroupIds.value = previous.expanded
    console.error('Error saving layout:', err)
    showToast('Failed to save the layout', 'error')
  }
}

const openGroup = (id) => {
  expandedGroupIds.value = new Set([...expandedGroupIds.value, id])
}

const handleToggleGroup = (id) => {
  const next = new Set(expandedGroupIds.value)
  if (next.has(id)) next.delete(id)
  else next.add(id)
  expandedGroupIds.value = next
}

const handleMoveNode = async (key, delta) => {
  const previous = snapshot()
  const next = move(layoutTree.value, key, delta)
  if (next === layoutTree.value) return

  layoutTree.value = next
  await saveLayout(previous)
}

const openMoveTo = (key) => {
  moveTarget.value = { key, title: 'Move to', targets: moveTargets(layoutTree.value, key) }
}

const handleMoveTo = async (targetId) => {
  const target = moveTarget.value
  moveTarget.value = null
  if (!target) return

  const previous = snapshot()
  const next = moveTo(layoutTree.value, target.key, targetId)
  if (next === layoutTree.value) return

  layoutTree.value = next
  if (targetId) openGroup(targetId) // so you can see where it went
  await saveLayout(previous, 'Moved')
}

// A room's name here is display-only: it sets room.displayName and never touches room.name, which is
// the join key the NetDaemon app uses to find the room's radiator, plugs and sensors. Empty means
// "use the room's own name".
const openRoomEdit = (room) => {
  iconTarget.value = {
    kind: 'room',
    id: room.id,
    title: 'Edit room',
    current: room.icon ?? '',
    name: room.displayName ?? '',
    canonicalName: room.name
  }
}

const openGroupEdit = (node) => {
  iconTarget.value = { kind: 'group', id: node.id, title: 'Edit group', current: node.icon ?? '', name: node.name }
}

const openAddGroup = () => {
  iconTarget.value = { kind: 'new-group', title: 'Add group', current: '', name: '' }
}

const handleIconSave = async ({ icon, name } = {}) => {
  const target = iconTarget.value
  iconTarget.value = null
  if (!target) return

  const previous = snapshot()
  const cleaned = normaliseIcon(icon)
  let message

  if (target.kind === 'room') {
    const trimmedName = name?.trim() ?? ''
    const displayName = trimmedName.length > 0 ? trimmedName : null
    rooms.value = previous.rooms.map(room => (room.id === target.id ? { ...room, icon: cleaned, displayName } : room))
    message = 'Room saved'
  } else if (target.kind === 'group') {
    layoutTree.value = updateGroup(layoutTree.value, target.id, { name, icon: cleaned })
    message = 'Group saved'
  } else {
    const id = newGroupId(layoutTree.value)
    layoutTree.value = addGroup(layoutTree.value, { id, name: name.trim(), icon: cleaned })
    openGroup(id)
    message = 'Group added'
  }

  await saveLayout(previous, message)
}

// Deleting a group keeps what is inside it: its rooms and groups move up one level.
const handleDeleteGroup = async () => {
  const target = iconTarget.value
  iconTarget.value = null
  if (!target || target.kind !== 'group') return

  const previous = snapshot()
  layoutTree.value = deleteGroup(layoutTree.value, target.id)
  await saveLayout(previous, 'Group deleted. Its rooms were kept.')
}

const handleToggleExpand = (roomId) => {
  // If clicking the already-expanded room, collapse it
  if (expandedRoomId.value === roomId) {
    expandedRoomId.value = null
    deleteCookie(EXPANDED_ROOM_COOKIE)
  } else {
    // Otherwise, expand this room and collapse others
    expandedRoomId.value = roomId
    setCookie(EXPANDED_ROOM_COOKIE, roomId)
  }
}


// Everything LayoutNode needs, provided last so every handler it points at already exists.
provide(HEATING_LAYOUT, {
  roomsById,
  presenceZones: presenceZoneList,
  drivableDevices: drivableDeviceList,
  editLayout,
  occupancyFilter: toRef(props, 'occupancyFilter'),
  expandedRoomId,
  expandedGroupIds,
  canMove: (key, delta) => canMove(layoutTree.value, key, delta),
  onMove: handleMoveNode,
  onMoveTo: openMoveTo,
  onEditRoom: openRoomEdit,
  onEditGroup: openGroupEdit,
  onToggleGroup: handleToggleGroup,
  onUpdateSchedule: handleUpdateSchedule,
  onDeleteSchedule: handleDeleteSchedule,
  onAddSchedule: handleAddSchedule,
  onBoost: handleBoost,
  onCancelBoost: handleCancelBoost,
  onBoostGroup: handleGroupBoost,
  onCancelGroupBoost: handleCancelGroupBoost,
  onToggleExpand: handleToggleExpand
})
</script>

<style scoped>
.heating-view {
  width: 100%;
}

.loading-state,
.error-state,
.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 3rem 1rem;
  text-align: center;
  color: var(--text-secondary);
}

.spinner {
  width: 48px;
  height: 48px;
  border: 4px solid var(--border-color);
  border-top: 4px solid var(--color-primary);
  border-radius: 50%;
  animation: spin 1s linear infinite;
}

.spinner-small {
  width: 16px;
  height: 16px;
  border: 2px solid #ffffff;
  border-top: 2px solid transparent;
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}

@keyframes spin {
  0% { transform: rotate(0deg); }
  100% { transform: rotate(360deg); }
}

.error-state svg {
  color: var(--color-danger);
  margin-bottom: 1rem;
}

.error-state p {
  margin-bottom: 1rem;
  font-size: 1.1rem;
}

.empty-state svg {
  color: var(--text-tertiary);
  margin-bottom: 1rem;
}

.empty-state p {
  font-size: 1.1rem;
}

.rooms-container {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.btn {
  padding: 0.75rem 1.5rem;
  border: none;
  border-radius: 6px;
  font-size: 1rem;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.5rem;
}

.btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.btn-retry {
  background-color: var(--color-primary);
  color: white;
}

.btn-retry:hover:not(:disabled) {
  background-color: var(--color-primary-hover);
}

.btn:active:not(:disabled) {
  transform: scale(0.98);
}

.layout-toolbar {
  display: flex;
  justify-content: flex-end;
  gap: 0.5rem;
  margin-bottom: 0.5rem;
}

.btn-layout {
  background: none;
  border: 1px solid var(--border-color);
  border-radius: 8px;
  color: var(--text-secondary);
  cursor: pointer;
  font-size: 0.85rem;
  font-weight: 600;
  padding: 0.4rem 0.9rem;
  transition: all 0.2s;
}

.btn-layout:hover {
  background-color: var(--hover-bg);
  color: var(--text-primary);
}

.btn-layout.active {
  background-color: var(--color-primary);
  border-color: var(--color-primary);
  color: white;
}
</style>

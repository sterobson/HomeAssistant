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
        <!-- Status on the left, opposite the layout buttons. -->
        <div class="toolbar-status">
          <!-- Only in fingerbot mode, when the app drives the boiler. A belief, not a reading: nothing sees the
               boiler itself (CLAUDE.md, "The boiler has no feedback"). -->
          <button
            v-if="boilerState?.appControlsBoiler"
            type="button"
            class="boiler-status"
            :class="{ on: boilerState.believedOn }"
            title="What the app believes, from its own presses: nothing can read the boiler itself. Select for its history."
            @click="showBoilerHistory = true"
          >
            <svg v-if="boilerState.believedOn" width="14" height="14" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
              <path d="M8 16c3.314 0 6-2 6-5.5 0-1.5-.5-4-2.5-6 .25 1.5-1.25 2-1.25 2C11 4 9 .5 6 0c.357 2 .5 4-2 6-1.25 1-2 2.729-2 4.5C2 14 4.686 16 8 16m0-1c-1.657 0-3-1-3-2.75 0-.75.25-2 1.25-3C6.125 10 7 10.5 7 10.5c-.375-1.25.5-3.25 2-3.5-.179 1-.25 2 1 3 .625.5 1 1.364 1 2.25C11 14 9.657 15 8 15"/>
            </svg>
            {{ boilerStatusText }}
          </button>
          <!-- A low battery that belongs to no zone or group (no HA area, or an area not placed anywhere). -->
          <button
            v-if="unplacedLowest"
            type="button"
            class="battery-alert"
            title="See every battery and choose where each area's devices show"
            @click="showBatteries = true"
          >
            <BatteryBadge :battery="unplacedLowest" :others-low="unplacedLow.length - 1" />
          </button>
        </div>
        <button v-if="!editLayout" class="btn-layout" title="Every device in the house, filtered by kind" @click="showHouseDevices = true">
          Devices
        </button>
        <button v-if="editLayout" class="btn-layout" title="Every battery, and where each area's devices show" @click="showBatteries = true">
          Batteries
        </button>
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

    <BoilerHistoryModal v-if="showBoilerHistory" @close="showBoilerHistory = false" />

    <RoomDevicesModal
      v-if="showHouseDevices"
      title="Devices in the house"
      :sections="houseSections"
      :presence-zones="presenceZoneList"
      :all-devices="drivableDeviceList"
      :all-zones="rooms"
      @close="showHouseDevices = false"
    />

    <BatteriesModal
      v-if="showBatteries"
      :batteries="batteries"
      :rooms="rooms"
      :targets="batteryTargets"
      :area-placement="areaPlacement"
      :saving="savingAreas"
      :error="areasError"
      @save="handleSaveAreas"
      @cancel="closeBatteries"
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
import BatteryBadge from '../components/BatteryBadge.vue'
import BatteriesModal from '../components/BatteriesModal.vue'
import RoomDevicesModal from '../components/RoomDevicesModal.vue'
import BoilerHistoryModal from '../components/BoilerHistoryModal.vue'
import { houseDeviceSections } from '../utils/roomDevices.js'
import { heatingApi } from '../services/heatingApi.js'
import { useDeviceSettings } from '../composables/useDeviceSettings.js'
import { isLow, lowestLow, placeBatteries, placeTargets } from '../utils/batteries.js'
import { useSignalR } from '../composables/useSignalR.js'
import { getHouseId } from '../utils/cookies.js'
import { roomLabel } from '../utils/rooms.js'
import {
  fromLayoutDto, toLayoutDto, canMove, move, moveTo, moveTargets, addGroup, updateGroup, deleteGroup,
  newGroupId, normaliseIcon, roomIdsIn
} from '../utils/roomLayout.js'
import { HEATING_LAYOUT } from '../utils/layoutContext.js'
import { drivableDevices, presenceZones } from '../utils/deviceRules.js'
import { groupBoost, groupLockedBy, roomLockedBy } from '../utils/boosts.js'
import { useFormatting } from '../composables/useFormatting.js'

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

// What the NetDaemon app believes the boiler is doing ({ appControlsBoiler, believedOn, since }), from room states.
const { formatTimeDisplay } = useFormatting()
const boilerState = ref(null)
const boilerStatusText = computed(() => {
  const state = boilerState.value
  if (!state) return ''
  const since = state.since ? new Date(state.since) : null
  const sinceText = since && !Number.isNaN(since.getTime())
    ? ` since ${formatTimeDisplay(`${String(since.getHours()).padStart(2, '0')}:${String(since.getMinutes()).padStart(2, '0')}`)}`
    : ''
  return `Boiler ${state.believedOn ? 'on' : 'off'}${sinceText}`
})
const roomsById = computed(() => new Map(rooms.value.map(room => [room.id, room])))

// Every replaceable battery in the house, from room states, and where each shows (utils/batteries.js): on the zone
// whose heating uses it, else on the zone or group its HA area is mapped to, else at the top of the page.
const batteries = ref([])
const { loadSettings, areaPlacement, saveAreaPlacement } = useDeviceSettings()
const batteryPlacement = computed(() => placeBatteries(batteries.value, rooms.value, layoutTree.value, areaPlacement.value))
const unplacedLow = computed(() => batteryPlacement.value.unplaced.filter(isLow))
// Every zone's devices and every battery, in layout order, for the house-wide Devices list (owner, 2026-10-21).
const showHouseDevices = ref(false)
// The boiler's on/off history, opened from the boiler status (owner, 2026-10-21).
const showBoilerHistory = ref(false)
const houseSections = computed(() => houseDeviceSections(layoutTree.value, roomsById.value, batteryPlacement.value))
const unplacedLowest = computed(() => lowestLow(unplacedLow.value))
const batteryTargets = computed(() => placeTargets(layoutTree.value, roomsById.value))
const showBatteries = ref(false)
const savingAreas = ref(false)
const areasError = ref('')

const closeBatteries = () => {
  if (savingAreas.value) return
  areasError.value = ''
  showBatteries.value = false
}

const handleSaveAreas = async (placement) => {
  savingAreas.value = true
  areasError.value = ''
  try {
    await saveAreaPlacement(placement)
    showBatteries.value = false
  } catch (err) {
    areasError.value = err?.message || 'The areas could not be saved. Try again.'
  } finally {
    savingAreas.value = false
  }
}
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

// Which zone and which groups are open survives a reload. Kept as JSON so a room's id comes back as the number it
// is: the cookie this replaced stored it as text, which never matched the room again.
const EXPANDED_STORAGE_KEY = 'heating-expanded'
const LEGACY_EXPANDED_ROOM_COOKIE = 'heating-app-expanded-room'

const loadExpanded = () => {
  document.cookie = `${LEGACY_EXPANDED_ROOM_COOKIE}=;expires=Thu, 01 Jan 1970 00:00:00 UTC;path=/`
  try {
    const saved = JSON.parse(localStorage.getItem(EXPANDED_STORAGE_KEY) ?? 'null')
    if (!saved) return
    expandedRoomId.value = saved.roomId ?? null
    expandedGroupIds.value = new Set(Array.isArray(saved.groupIds) ? saved.groupIds : [])
  } catch {
    // Unreadable or blocked storage: start with everything closed.
  }
}

const saveExpanded = () => {
  try {
    localStorage.setItem(EXPANDED_STORAGE_KEY, JSON.stringify({
      roomId: expandedRoomId.value,
      groupIds: [...expandedGroupIds.value]
    }))
  } catch {
    // Storage blocked: the page still works, it just won't remember.
  }
}

watch([expandedRoomId, expandedGroupIds], saveExpanded)

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
  // Where each HA area's battery devices show. Not awaited: until it loads they warn at the top of the page.
  loadSettings()

  loadExpanded()

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
      devices: state?.devices ?? [],
      // Which presence detectors count for the zone, and all there are (utils/zoneOccupancy.js). Display only.
      occupancy: state?.occupancy ?? null,
      // Tracks due now but held back by their room-occupancy condition (since 2026-10-21). Display only.
      waitingForOccupancyTrackIds: state?.waitingForOccupancyTrackIds ?? []
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
      boilerState.value = statesData.boiler ?? null
      batteries.value = statesData.batteries ?? []
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
    boilerState.value = statesData.boiler ?? null
    batteries.value = statesData.batteries ?? []

    // Update room states without affecting schedule configuration
    rooms.value = rooms.value.map(room => {
      const state = roomStates.value.find(s => s.roomId === room.id)
      return {
        ...room,
        currentTemperature: state?.currentTemperature ?? room.currentTemperature,
        heatingActive: state?.heatingActive ?? room.heatingActive,
        activeScheduleId: state?.activeScheduleTrackId ?? room.activeScheduleId,
        // A zone's occupancy can change its capabilities (gaining or losing presence), so refresh both.
        capabilities: state?.capabilities ?? room.capabilities,
        devices: state?.devices ?? room.devices,
        occupancy: state?.occupancy ?? room.occupancy ?? null,
        waitingForOccupancyTrackIds: state?.waitingForOccupancyTrackIds ?? room.waitingForOccupancyTrackIds ?? []
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

// A zone's own boost. Refused while a group the zone sits in is boosted: only that group can change it.
const handleBoost = async (roomId, boostData) => {
  const room = rooms.value.find(r => r.id === roomId)
  if (room && !roomLockedBy(layoutTree.value, room)) {
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
  if (room && !roomLockedBy(layoutTree.value, room)) {
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

// A group boost is the same boost on every zone in the group, at any depth, saved in one go and tagged with
// the group (utils/boosts.js). Like a zone boost it sets that exact temperature, up or down, and boosting
// again changes it. It replaces any boost already inside the group, a zone's own or a smaller group's.
const handleGroupBoost = async (groupNode, boostData) => {
  if (groupLockedBy(layoutTree.value, groupNode, roomsById.value)) return
  const ids = new Set(roomIdsIn(groupNode))
  const startTime = new Date()
  const endTime = new Date(startTime.getTime() + boostData.duration * 60 * 60 * 1000)
  const previous = rooms.value.map(room => room.boost)

  for (const room of rooms.value) {
    if (ids.has(room.id)) {
      room.boost = groupBoost(groupNode, startTime, endTime, boostData.temperature)
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

// Cancels this group's own boost only: a zone boost set since, or a smaller group's, is left alone.
const handleCancelGroupBoost = async (groupNode) => {
  if (groupLockedBy(layoutTree.value, groupNode, roomsById.value)) return
  const ids = new Set(roomIdsIn(groupNode))
  const previous = rooms.value.map(room => room.boost)

  for (const room of rooms.value) {
    if (ids.has(room.id) && room.boost?.groupId === groupNode.id) room.boost = null
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

// One zone open at a time: opening one closes the other. Saved by the watch on the expanded state.
const handleToggleExpand = (roomId) => {
  expandedRoomId.value = expandedRoomId.value === roomId ? null : roomId
}


// Everything LayoutNode needs, provided last so every handler it points at already exists.
provide(HEATING_LAYOUT, {
  roomsById,
  presenceZones: presenceZoneList,
  drivableDevices: drivableDeviceList,
  // Every zone with its occupancy, for the first save of the zones' presence detectors.
  zones: rooms,
  layoutTree,
  batteryPlacement,
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
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 0.5rem;
}

/* Pushed to the left of the toolbar, opposite the layout buttons. */
.toolbar-status {
  margin-right: auto;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
}

.boiler-status {
  background: none;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  gap: 0.35rem;
  padding: 0.3rem 0.7rem;
  border: 1px solid var(--border-color);
  border-radius: 999px;
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--text-secondary);
}

.boiler-status:hover {
  background-color: var(--hover-bg);
}

.boiler-status.on {
  border-color: var(--color-warning);
  color: var(--color-warning);
}

.battery-alert {
  display: inline-flex;
  align-items: center;
  padding: 0.3rem 0.6rem;
  border: 1px solid var(--color-warning);
  border-radius: 999px;
  background: none;
  cursor: pointer;
}

.battery-alert:hover {
  background-color: var(--hover-bg);
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

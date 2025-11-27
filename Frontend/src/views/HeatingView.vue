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

      <RoomCard
        v-for="room in rooms"
        :key="room.id"
        :room="room"
        :is-expanded="expandedRoomId === room.id"
        @update-schedule="handleUpdateSchedule"
        @delete-schedule="handleDeleteSchedule"
        @add-schedule="handleAddSchedule"
        @boost="handleBoost"
        @cancel-boost="handleCancelBoost"
        @toggle-expand="handleToggleExpand"
      />
    </div>

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
import { ref, onMounted, onUnmounted, reactive } from 'vue'
import RoomCard from '../components/RoomCard.vue'
import Toast from '../components/Toast.vue'
import { heatingApi } from '../services/heatingApi.js'

const rooms = ref([])
const roomStates = ref([])
const loading = ref(true)
const error = ref(null)
const statePollingInterval = ref(null)
const expandedRoomId = ref(null)
const toast = reactive({
  visible: false,
  message: '',
  type: 'success',
  duration: 3000
})

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

// TODO: SignalR Connection - Connect to SignalR when component mounts
// onMounted(async () => {
//   await heatingApi.connectToSignalR()
// })
//
// onUnmounted(async () => {
//   await heatingApi.disconnectFromSignalR()
// })

onMounted(() => {
  // Load previously expanded room from cookie
  const savedExpandedRoom = getCookie(EXPANDED_ROOM_COOKIE)
  if (savedExpandedRoom && savedExpandedRoom !== 'null') {
    expandedRoomId.value = savedExpandedRoom
  }

  loadSchedules()
  // Poll room states every 30 seconds to update current temperatures and heating status
  // TODO: Replace with SignalR notifications when implemented
  statePollingInterval.value = setInterval(() => {
    loadRoomStates()
  }, 30000)
})

onUnmounted(() => {
  if (statePollingInterval.value) {
    clearInterval(statePollingInterval.value)
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
      activeScheduleId: state?.activeScheduleTrackId ?? null
    }
  })
}

const loadSchedules = async () => {
  loading.value = true
  error.value = null

  try {
    const data = await heatingApi.getSchedules()
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
  } catch (err) {
    error.value = 'Failed to load schedules. Please try again.'
    console.error('Error loading schedules:', err)
  } finally {
    loading.value = false
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
        activeScheduleId: state?.activeScheduleTrackId ?? room.activeScheduleId
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
        await heatingApi.setSchedules({ rooms: rooms.value })
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
      await heatingApi.setSchedules({ rooms: rooms.value })
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
      await heatingApi.setSchedules({ rooms: rooms.value })
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
    const endTime = new Date()
    endTime.setHours(endTime.getHours() + boostData.duration)

    room.boost = {
      startTime: startTime.toISOString(),
      endTime: endTime.toISOString(),
      temperature: boostData.temperature
    }

    // Save immediately
    try {
      await heatingApi.setSchedules({ rooms: rooms.value })
      const durationText = boostData.duration === 1 ? '1 hour' : `${boostData.duration} hours`
      showToast(`Boost activated for ${room.name} (${boostData.temperature}°C for ${durationText})`, 'success')
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
      await heatingApi.setSchedules({ rooms: rooms.value })
      showToast(`Boost cancelled for ${room.name}`, 'info')
    } catch (err) {
      console.error('Error saving boost cancellation:', err)
      showToast('Failed to cancel boost', 'error')
    }
  }
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
</style>

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
        @update-schedule="handleUpdateSchedule"
        @delete-schedule="handleDeleteSchedule"
        @add-schedule="handleAddSchedule"
        @boost="handleBoost"
        @cancel-boost="handleCancelBoost"
      />

      <div class="save-section" v-if="hasChanges">
        <button class="btn btn-save" @click="saveSchedules" :disabled="saving">
          <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" v-if="!saving">
            <path d="M2 1a1 1 0 00-1 1v12a1 1 0 001 1h12a1 1 0 001-1V2a1 1 0 00-1-1H9.5a1 1 0 00-1 1v7.293l2.646-2.647a.5.5 0 01.708.708l-3.5 3.5a.5.5 0 01-.708 0l-3.5-3.5a.5.5 0 11.708-.708L7.5 9.293V2a2 2 0 012-2H14a2 2 0 012 2v12a2 2 0 01-2 2H2a2 2 0 01-2-2V2a2 2 0 012-2h5.5a.5.5 0 010 1H2z"/>
          </svg>
          <div class="spinner-small" v-else></div>
          {{ saving ? 'Saving...' : 'Save Changes' }}
        </button>
        <button class="btn btn-discard" @click="discardChanges" :disabled="saving">
          Discard Changes
        </button>
      </div>
    </div>

    <ConfirmModal
      v-if="showDiscardConfirm"
      title="Discard Changes"
      message="Are you sure you want to discard all unsaved changes?"
      confirm-text="Discard"
      cancel-text="Keep Editing"
      @confirm="handleDiscardConfirm"
      @cancel="handleDiscardCancel"
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
import { ref, onMounted, onUnmounted, reactive } from 'vue'
import RoomCard from '../components/RoomCard.vue'
import ConfirmModal from '../components/ConfirmModal.vue'
import Toast from '../components/Toast.vue'
import { heatingApi } from '../services/heatingApi.js'

const rooms = ref([])
const roomStates = ref([])
const loading = ref(true)
const error = ref(null)
const hasChanges = ref(false)
const saving = ref(false)
const originalData = ref(null)
const showDiscardConfirm = ref(false)
const statePollingInterval = ref(null)
const toast = reactive({
  visible: false,
  message: '',
  type: 'success',
  duration: 3000
})

// TODO: SignalR Connection - Connect to SignalR when component mounts
// onMounted(async () => {
//   await heatingApi.connectToSignalR()
// })
//
// onUnmounted(async () => {
//   await heatingApi.disconnectFromSignalR()
// })

onMounted(() => {
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
    originalData.value = JSON.parse(JSON.stringify(data.rooms))
    hasChanges.value = false
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

const handleUpdateSchedule = (roomId, updatedSchedule) => {
  const room = rooms.value.find(r => r.id === roomId)
  if (room) {
    const scheduleIndex = room.schedules.findIndex(s => s.id === updatedSchedule.id)
    if (scheduleIndex > -1) {
      room.schedules[scheduleIndex] = updatedSchedule
      sortSchedules(room.schedules)
      hasChanges.value = true
    }
  }
}

const handleDeleteSchedule = (roomId, scheduleId) => {
  const room = rooms.value.find(r => r.id === roomId)
  if (room) {
    room.schedules = room.schedules.filter(s => s.id !== scheduleId)
    sortSchedules(room.schedules)
    hasChanges.value = true
  }
}

const handleAddSchedule = (roomId, newSchedule) => {
  const room = rooms.value.find(r => r.id === roomId)
  if (room) {
    room.schedules.push(newSchedule)
    sortSchedules(room.schedules)
    hasChanges.value = true
  }
}

const showToast = (message, type = 'success', duration = 3000) => {
  toast.message = message
  toast.type = type
  toast.duration = duration
  toast.visible = true
}

const handleBoost = (roomId, boostData) => {
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

    hasChanges.value = true

    const durationText = boostData.duration === 1 ? '1 hour' : `${boostData.duration} hours`
    showToast(`Boost activated for ${room.name} (${boostData.temperature}°C for ${durationText})`, 'success')
  }
}

const handleCancelBoost = (roomId) => {
  const room = rooms.value.find(r => r.id === roomId)
  if (room) {
    room.boost = null

    hasChanges.value = true

    showToast(`Boost cancelled for ${room.name}`, 'info')
  }
}

const saveSchedules = async () => {
  saving.value = true

  try {
    await heatingApi.setSchedules({ rooms: rooms.value })
    originalData.value = JSON.parse(JSON.stringify(rooms.value))
    hasChanges.value = false

    // Show success feedback (you could add a toast notification here)
    console.log('Schedules saved successfully!')
  } catch (err) {
    error.value = 'Failed to save schedules. Please try again.'
    console.error('Error saving schedules:', err)
  } finally {
    saving.value = false
  }
}

const discardChanges = () => {
  showDiscardConfirm.value = true
}

const handleDiscardConfirm = () => {
  rooms.value = JSON.parse(JSON.stringify(originalData.value))
  hasChanges.value = false
  showDiscardConfirm.value = false
}

const handleDiscardCancel = () => {
  showDiscardConfirm.value = false
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

.save-section {
  position: sticky;
  bottom: 0;
  background: var(--bg-secondary);
  padding: 1rem;
  border-radius: 8px;
  box-shadow: 0 -2px 10px var(--shadow);
  display: flex;
  gap: 0.75rem;
  margin-top: 1rem;
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

.btn-save {
  flex: 2;
  background-color: var(--color-success);
  color: white;
}

.btn-save:hover:not(:disabled) {
  background-color: var(--color-success-hover);
}

.btn-discard {
  flex: 1;
  background-color: var(--color-danger);
  color: white;
}

.btn-discard:hover:not(:disabled) {
  background-color: var(--color-danger-hover);
}

.btn:active:not(:disabled) {
  transform: scale(0.98);
}

@media (max-width: 600px) {
  .save-section {
    flex-direction: column;
    padding: 0.75rem;
  }

  .btn-save,
  .btn-discard {
    flex: 1;
  }
}
</style>

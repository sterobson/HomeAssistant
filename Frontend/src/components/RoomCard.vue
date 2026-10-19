<template>
  <div class="room-card">
    <div
      class="room-card-header"
      :class="{
        'collapsed-heating': isCollapsed && room.heatingActive
      }"
    >
      <div class="header-left" :class="{ 'not-expandable': !canSetTemperature }" @click="canSetTemperature && toggleCollapse()">
        <div class="room-name-wrapper">
          <h2 class="room-name"><span v-if="room.icon" class="room-icon" aria-hidden="true">{{ room.icon }}</span>{{ roomDisplayName }}</h2>
          <button v-if="canSetTemperature" class="collapse-btn" :class="{ collapsed: isCollapsed }" @click="toggleCollapse">
            <svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor">
              <path d="M5 7l5 5 5-5H5z"/>
            </svg>
          </button>
        </div>
        <div class="room-status">
          <div v-if="room.heatingActive" class="heating-status active" title="Heating active">
            <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor">
              <path d="M8 16c3.314 0 6-2 6-5.5 0-1.5-.5-4-2.5-6 .25 1.5-1.25 2-1.25 2C11 4 9 .5 6 0c.357 2 .5 4-2 6-1.25 1-2 2.729-2 4.5C2 14 4.686 16 8 16m0-1c-1.657 0-3-1-3-2.75 0-.75.25-2 1.25-3C6.125 10 7 10.5 7 10.5c-.375-1.25.5-3.25 2-3.5-.179 1-.25 2 1 3 .625.5 1 1.364 1 2.25C11 14 9.657 15 8 15"/>
            </svg>
          </div>
          <span class="current-temp">{{ formatTempWithUnit(room.currentTemperature) }}</span>
        </div>
      </div>
      <div v-if="!editMode" class="header-actions">
        <div class="icon-btn-placeholder">
          <button v-if="!isBoostActive && canSetTemperature" class="icon-btn boost-btn" @click.stop="handleBoost" title="Boost heating">
            <svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor">
              <path d="M11.251.068a.5.5 0 01.227.58L9.677 6.5H13a.5.5 0 01.364.843l-8 8.5a.5.5 0 01-.842-.49L6.323 9.5H3a.5.5 0 01-.364-.843l8-8.5a.5.5 0 01.615-.09z"/>
            </svg>
          </button>
        </div>
        <button class="icon-btn history-btn" @click.stop="handleHistory" title="View temperature history">
          <svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor">
            <path d="M0 0h1v15h15v1H0V0zm10 3.5a.5.5 0 0 1 .5-.5h4a.5.5 0 0 1 .5.5v4a.5.5 0 0 1-1 0V4.9l-3.613 4.417a.5.5 0 0 1-.74.037L7.06 6.767l-3.656 5.027a.5.5 0 0 1-.808-.588l4-5.5a.5.5 0 0 1 .758-.06l2.609 2.61L13.445 4H10.5a.5.5 0 0 1-.5-.5z"/>
          </svg>
        </button>
      </div>
      <div v-else class="header-actions">
        <button class="icon-btn edit-btn" @click.stop="emit('edit')" title="Edit name and icon" aria-label="Edit name and icon">
          <svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
            <path d="M12.146.146a.5.5 0 01.708 0l3 3a.5.5 0 010 .708l-10 10a.5.5 0 01-.168.11l-5 2a.5.5 0 01-.65-.65l2-5a.5.5 0 01.11-.168l10-10zM11.207 2.5L13.5 4.793 14.793 3.5 12.5 1.207 11.207 2.5zm1.586 3L10.5 3.207 4 9.707V10h.5a.5.5 0 01.5.5v.5h.5a.5.5 0 01.5.5v.5h.293l6.5-6.5zm-9.761 5.175l-.106.106-1.528 3.821 3.821-1.528.106-.106A.5.5 0 015 12.5V12h-.5a.5.5 0 01-.5-.5V11h-.5a.5.5 0 01-.468-.325z"/>
          </svg>
        </button>
        <button class="icon-btn edit-btn" :disabled="!canMoveUp" @click.stop="emit('move', -1)" title="Move up" aria-label="Move up">
          <svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true"><path d="M7.646 4.646a.5.5 0 01.708 0l6 6a.5.5 0 01-.708.708L8 5.707l-5.646 5.647a.5.5 0 01-.708-.708l6-6z"/></svg>
        </button>
        <button class="icon-btn edit-btn" :disabled="!canMoveDown" @click.stop="emit('move', 1)" title="Move down" aria-label="Move down">
          <svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true"><path d="M1.646 4.646a.5.5 0 01.708 0L8 10.293l5.646-5.647a.5.5 0 01.708.708l-6 6a.5.5 0 01-.708 0l-6-6a.5.5 0 010-.708z"/></svg>
        </button>
        <button class="icon-btn edit-btn" @click.stop="emit('move-to')" title="Move to…" aria-label="Move to">
          <svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true"><path d="M1 3.5A1.5 1.5 0 012.5 2h2.764c.958 0 1.76.56 2.311 1.184C7.985 3.648 8.48 4 9 4h4.5A1.5 1.5 0 0115 5.5v7a1.5 1.5 0 01-1.5 1.5h-11A1.5 1.5 0 011 12.5v-9zM2.5 3a.5.5 0 00-.5.5V6h12v-.5a.5.5 0 00-.5-.5H9c-.964 0-1.71-.629-2.174-1.154C6.374 3.334 5.82 3 5.264 3H2.5zM14 7H2v5.5a.5.5 0 00.5.5h11a.5.5 0 00.5-.5V7z"/></svg>
        </button>
      </div>
    </div>

    <div v-show="!isCollapsed" class="room-card-content">
      <BoostCard
        v-if="isBoostActive"
        :boost-end-time="room.boost.endTime"
        :boost-temperature="room.boost.temperature"
        @cancel="handleCancelBoost"
      />

      <template v-else>
        <div class="schedules-list">
          <ScheduleItem
            v-for="schedule in sortedSchedules"
            :key="schedule.id"
            :schedule="schedule"
            :is-active="schedule.id === room.activeScheduleId"
            :heating-active="room.heatingActive"
            @edit="handleEdit"
            @delete="handleDelete"
          />
        </div>

        <button v-if="canSetTemperature" class="btn btn-add" @click="handleAdd">
          <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor">
            <path d="M8 0v16M0 8h16" stroke="currentColor" stroke-width="2"/>
          </svg>
          Add Schedule
        </button>
      </template>

      <!-- Tucked inside the expanded card rather than in the header, so it costs nothing until a room is opened. -->
      <button type="button" class="devices-link" @click="showDevices = true">
        <svg width="14" height="14" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
          <path d="M6 0a.5.5 0 01.5.5V3h3V.5a.5.5 0 011 0V3h1a.5.5 0 01.5.5v3A3.5 3.5 0 018.5 10c-.002.434-.01.845-.04 1.22-.041.514-.126 1.003-.317 1.424a2.083 2.083 0 01-.97 1.028C6.725 13.9 6.169 14 5.5 14c-.998 0-1.61.33-1.974.718A1.922 1.922 0 003 16H2c0-.616.232-1.367.797-1.968C3.374 13.42 4.261 13 5.5 13c.581 0 .962-.088 1.218-.219.241-.123.4-.3.514-.55.121-.266.193-.621.23-1.09.027-.34.035-.718.037-1.141A3.5 3.5 0 014 6.5v-3a.5.5 0 01.5-.5h1V.5A.5.5 0 016 0z"/>
        </svg>
        Devices ({{ deviceCount }})
      </button>

      <ScheduleEditor
        v-if="showEditor"
        :schedule="editingSchedule"
        :occupancy-filter="occupancyFilter"
        :room-capabilities="room.capabilities"
        @save="handleSave"
        @cancel="handleCancel"
      />
    </div>

    <BoostModal
      v-if="showBoost"
      :room-name="roomDisplayName"
      @boost="handleBoostConfirm"
      @cancel="handleBoostCancel"
    />

    <ConfirmModal
      v-if="showDeleteConfirm"
      title="Delete Schedule"
      message="Are you sure you want to delete this schedule?"
      confirm-text="Delete"
      cancel-text="Cancel"
      @confirm="handleDeleteConfirm"
      @cancel="handleDeleteCancel"
    />

    <TemperatureHistoryModal
      :show="showHistory"
      :room-name="roomDisplayName"
      :history-data="historyData"
      :loading="historyLoading"
      :error="historyError"
      @close="handleHistoryClose"
      @retry="handleHistory"
      @date-change="handleDateChange"
    />

    <RoomDevicesModal
      v-if="showDevices"
      :title="`Devices in ${roomDisplayName}`"
      :sections="deviceSectionsForRoom"
      @close="showDevices = false"
    />
  </div>
</template>

<script setup>
import { ref, computed } from 'vue'
import ScheduleItem from './ScheduleItem.vue'
import ScheduleEditor from './ScheduleEditor.vue'
import BoostModal from './BoostModal.vue'
import BoostCard from './BoostCard.vue'
import ConfirmModal from './ConfirmModal.vue'
import TemperatureHistoryModal from './TemperatureHistoryModal.vue'
import RoomDevicesModal from './RoomDevicesModal.vue'
import { useFormatting } from '../composables/useFormatting.js'
import { heatingApi } from '../services/heatingApi.js'
import { roomLabel } from '../utils/rooms.js'
import { deviceSections } from '../utils/roomDevices.js'

const { formatTempWithUnit } = useFormatting()

const props = defineProps({
  room: {
    type: Object,
    required: true
  },
  isExpanded: {
    type: Boolean,
    default: false
  },
  occupancyFilter: {
    type: String,
    default: null // 'occupied', 'vacant', or null for all
  },
  // Layout editing: the action buttons give way to choose-icon / move-up / move-down.
  editMode: {
    type: Boolean,
    default: false
  },
  canMoveUp: {
    type: Boolean,
    default: false
  },
  canMoveDown: {
    type: Boolean,
    default: false
  }
})

const emit = defineEmits(['update-schedule', 'delete-schedule', 'add-schedule', 'boost', 'cancel-boost', 'toggle-expand', 'move', 'move-to', 'edit'])

// Display name when the room has one, otherwise its canonical name. Never show
// room.name directly — see utils/rooms.js.
const roomDisplayName = computed(() => roomLabel(props.room))

const isCollapsed = computed(() => !props.isExpanded)
const showEditor = ref(false)
const editingSchedule = ref(null)
const showBoost = ref(false)
const showDeleteConfirm = ref(false)
const scheduleToDelete = ref(null)
const showHistory = ref(false)
const showDevices = ref(false)
const deviceCount = computed(() => props.room.devices?.length ?? 0)
const deviceSectionsForRoom = computed(() => deviceSections([props.room]))
const historyData = ref([])
const historyLoading = ref(false)
const historyError = ref(null)

// Check if room can set temperature (flag 1 in RoomCapabilities)
const canSetTemperature = computed(() => {
  return (props.room.capabilities & 1) !== 0
})

// Filter and sort schedules based on occupancy filter
const sortedSchedules = computed(() => {
  let schedules = [...props.room.schedules]

  // Filter by occupancy if a filter is active
  if (props.occupancyFilter) {
    schedules = schedules.filter(schedule => {
      // Check if schedule has house occupancy conditions
      const hasHouseOccupied = (schedule.conditions & 1) !== 0 // HouseOccupied flag
      const hasHouseUnoccupied = (schedule.conditions & 2) !== 0 // HouseUnoccupied flag
      const hasBothFlags = hasHouseOccupied && hasHouseUnoccupied

      // If both flags are set, schedule applies to both states
      if (hasBothFlags) {
        return true
      }

      // If occupied filter is active, show schedules with HouseOccupied flag
      if (props.occupancyFilter === 'occupied') {
        return hasHouseOccupied
      }

      // If vacant filter is active, show schedules with HouseUnoccupied flag
      if (props.occupancyFilter === 'vacant') {
        return hasHouseUnoccupied
      }

      return true
    })
  }

  // Sort by time
  return schedules.sort((a, b) => {
    return a.time.localeCompare(b.time)
  })
})

// Check if boost is actually active (has valid start and end times, and hasn't expired)
const isBoostActive = computed(() => {
  if (!props.room.boost || !props.room.boost.startTime || !props.room.boost.endTime) {
    return false
  }

  const endTime = new Date(props.room.boost.endTime)
  const now = new Date()
  return endTime > now
})

const toggleCollapse = () => {
  emit('toggle-expand', props.room.id)
}

const handleAdd = () => {
  editingSchedule.value = null
  showEditor.value = true
}

const handleEdit = (schedule) => {
  editingSchedule.value = schedule
  showEditor.value = true
}

const handleDelete = (scheduleId) => {
  scheduleToDelete.value = scheduleId
  showDeleteConfirm.value = true
}

const handleDeleteConfirm = () => {
  if (scheduleToDelete.value) {
    emit('delete-schedule', props.room.id, scheduleToDelete.value)
  }
  showDeleteConfirm.value = false
  scheduleToDelete.value = null
}

const handleDeleteCancel = () => {
  showDeleteConfirm.value = false
  scheduleToDelete.value = null
}

const handleSave = (schedule) => {
  if (editingSchedule.value) {
    emit('update-schedule', props.room.id, schedule)
  } else {
    emit('add-schedule', props.room.id, schedule)
  }
  showEditor.value = false
  editingSchedule.value = null
}

const handleCancel = () => {
  showEditor.value = false
  editingSchedule.value = null
}

const handleBoost = () => {
  showBoost.value = true
}

const handleBoostConfirm = (boostData) => {
  emit('boost', props.room.id, boostData)
  showBoost.value = false
}

const handleBoostCancel = () => {
  showBoost.value = false
}

const handleCancelBoost = () => {
  emit('cancel-boost', props.room.id)
}

const handleHistory = async () => {
  showHistory.value = true
  // Initial data load will be triggered by the modal's date-change event
}

const handleDateChange = async (startDate, endDate) => {
  historyLoading.value = true
  historyError.value = null
  historyData.value = []

  try {
    const response = await heatingApi.getRoomHistory(props.room.id, startDate, endDate)
    historyData.value = response.points
  } catch (error) {
    console.error('Error fetching temperature history:', error)
    historyError.value = 'Failed to load temperature history. Please try again.'
  } finally {
    historyLoading.value = false
  }
}

const handleHistoryClose = () => {
  showHistory.value = false
}
</script>

<style scoped>
.room-card {
  background: var(--bg-secondary);
  border-radius: 8px;
  box-shadow: 0 2px 4px var(--shadow);
  margin-bottom: 0.5rem;
  overflow: hidden;
}

.room-card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1rem;
  background-color: var(--bg-secondary);
  border-bottom: 1px solid var(--border-color);
  transition: all 0.2s;
}

.room-card-header.collapsed-heating {
  background: linear-gradient(135deg, rgba(243, 156, 18, 0.1) 0%, rgba(230, 126, 34, 0.1) 100%);
  border: 2px solid #f39c12;
  border-radius: 8px;
}

.header-left {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 0.25rem 0.5rem;
  flex: 1;
  min-width: 0;
  cursor: pointer;
  user-select: none;
}

.header-left.not-expandable {
  cursor: default;
}

.room-name-wrapper {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  flex: 1 1 auto;
  min-width: 0;
}

.room-name {
  font-size: 1.25rem;
  font-weight: 600;
  color: var(--text-primary);
  margin: 0;
  min-width: 0;
  overflow-wrap: anywhere;
}

.room-status {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex: 0 0 auto;
}

.heating-status {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 0.25rem;
  border-radius: 4px;
  color: var(--text-tertiary);
  transition: all 0.3s;
}

.heating-status.active {
  color: #ff6b35;
  animation: flicker 2s ease-in-out infinite;
}

@keyframes flicker {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.8; }
}

.current-temp {
  font-size: 1rem;
  font-weight: 600;
  color: var(--text-primary);
  margin-right: 0.5rem;
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex: 0 0 auto;
}

.icon-btn-placeholder {
  width: 36px;
  height: 36px;
  display: flex;
  align-items: center;
  justify-content: center;
}

.icon-btn {
  color: white;
  border: none;
  padding: 0.5rem;
  border-radius: 8px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.2s;
  width: 36px;
  height: 36px;
}

.boost-btn {
  background: linear-gradient(135deg, var(--color-warning) 0%, #e67e22 100%);
  box-shadow: 0 2px 4px rgba(243, 156, 18, 0.2);
}

.boost-btn:hover {
  transform: translateY(-1px);
  box-shadow: 0 4px 8px rgba(243, 156, 18, 0.3);
}

.boost-btn:active {
  transform: translateY(0);
}

.history-btn {
  background: linear-gradient(135deg, var(--color-primary) 0%, #1976d2 100%);
  box-shadow: 0 2px 4px rgba(66, 165, 245, 0.2);
}

.history-btn:hover {
  transform: translateY(-1px);
  box-shadow: 0 4px 8px rgba(66, 165, 245, 0.3);
}

.history-btn:active {
  transform: translateY(0);
}

.room-icon {
  margin-right: 0.5rem;
}

.edit-btn {
  background-color: var(--btn-secondary-bg);
  color: var(--icon-color);
}

.edit-btn:hover:not(:disabled) {
  background-color: var(--btn-secondary-hover);
  transform: translateY(-1px);
}

.edit-btn:disabled {
  opacity: 0.35;
  cursor: not-allowed;
}

.collapse-btn {
  background: none;
  border: none;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--icon-color);
  transition: transform 0.2s;
  padding: 0.25rem;
}

.collapse-btn:hover {
  color: var(--text-primary);
}

.collapse-btn.collapsed {
  transform: rotate(-90deg);
}

.room-card-content {
  padding: 1rem;
}

.schedules-list {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  margin-bottom: 1rem;
}

.btn {
  padding: 0.75rem 1rem;
  border: none;
  border-radius: 6px;
  font-size: 1rem;
  cursor: pointer;
  font-weight: 500;
  transition: all 0.2s;
  display: flex;
  align-items: center;
  gap: 0.5rem;
  justify-content: center;
}

.btn-add {
  background-color: var(--color-primary);
  color: white;
  width: 100%;
}

.btn-add:hover {
  background-color: var(--color-primary-hover);
}

.btn-add:active {
  transform: scale(0.98);
}

/* Deliberately quiet: a text link under the schedules, not another coloured button. */
.devices-link {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  margin-top: 0.75rem;
  padding: 0.25rem 0.5rem;
  margin-left: -0.5rem;
  background: none;
  border: none;
  border-radius: 6px;
  color: var(--text-secondary);
  font-size: 0.85rem;
  font-family: inherit;
  cursor: pointer;
}

.devices-link:hover {
  color: var(--color-primary);
  background-color: var(--hover-bg);
}

@media (max-width: 600px) {
  .room-card-header {
    padding: 0.75rem;
  }

  .room-name {
    font-size: 1.1rem;
  }

  .room-card-content {
    padding: 0.75rem;
  }
}
</style>

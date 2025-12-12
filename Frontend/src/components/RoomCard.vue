<template>
  <div class="room-card">
    <div
      class="room-card-header"
      :class="{
        'collapsed-heating': isCollapsed && room.heatingActive
      }"
    >
      <div class="header-left" :class="{ 'not-expandable': !canSetTemperature }" @click="canSetTemperature && toggleCollapse()">
        <h2 class="room-name">{{ room.name }}</h2>
        <div class="room-status">
          <div v-if="room.heatingActive" class="heating-status active" title="Heating active">
            <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor">
              <path d="M8 16c3.314 0 6-2 6-5.5 0-1.5-.5-4-2.5-6 .25 1.5-1.25 2-1.25 2C11 4 9 .5 6 0c.357 2 .5 4-2 6-1.25 1-2 2.729-2 4.5C2 14 4.686 16 8 16m0-1c-1.657 0-3-1-3-2.75 0-.75.25-2 1.25-3C6.125 10 7 10.5 7 10.5c-.375-1.25.5-3.25 2-3.5-.179 1-.25 2 1 3 .625.5 1 1.364 1 2.25C11 14 9.657 15 8 15"/>
            </svg>
          </div>
          <span class="current-temp">{{ formatTempWithUnit(room.currentTemperature) }}</span>
        </div>
      </div>
      <div class="header-actions">
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
        <div class="collapse-btn-placeholder">
          <button v-if="canSetTemperature" class="collapse-btn" :class="{ collapsed: isCollapsed }" @click="toggleCollapse">
            <svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor">
              <path d="M5 7l5 5 5-5H5z"/>
            </svg>
          </button>
        </div>
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
      :room-name="room.name"
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
      :room-name="room.name"
      :history-data="historyData"
      :loading="historyLoading"
      :error="historyError"
      @close="handleHistoryClose"
      @retry="handleHistory"
      @date-change="handleDateChange"
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
import { useFormatting } from '../composables/useFormatting.js'
import { heatingApi } from '../services/heatingApi.js'

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
  }
})

const emit = defineEmits(['update-schedule', 'delete-schedule', 'add-schedule', 'boost', 'cancel-boost', 'toggle-expand'])

const isCollapsed = computed(() => !props.isExpanded)
const showEditor = ref(false)
const editingSchedule = ref(null)
const showBoost = ref(false)
const showDeleteConfirm = ref(false)
const scheduleToDelete = ref(null)
const showHistory = ref(false)
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
  flex: 1;
  cursor: pointer;
  user-select: none;
}

.header-left.not-expandable {
  cursor: default;
}

.room-name {
  font-size: 1.25rem;
  font-weight: 600;
  flex: 1;
  color: var(--text-primary);
}

.room-status {
  display: flex;
  align-items: center;
  gap: 0.5rem;
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
}

.icon-btn-placeholder,
.collapse-btn-placeholder {
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

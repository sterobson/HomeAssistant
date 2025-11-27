<template>
  <div class="room-card">
    <div
      class="room-card-header"
      :class="{
        'collapsed-heating': isCollapsed && room.heatingActive
      }"
    >
      <div class="header-left" @click="toggleCollapse">
        <h2 class="room-name">{{ room.name }}</h2>
        <div class="room-status">
          <div class="heating-status" :class="{ active: room.heatingActive }" :title="room.heatingActive ? 'Heating active' : 'Heating off'">
            <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor">
              <path d="M8.5.5a.5.5 0 0 0-1 0v1.518A7 7 0 0 0 2.5 9a7 7 0 0 0 5 6.482V15.5a.5.5 0 0 0 1 0v-.018A7 7 0 0 0 13.5 9a7 7 0 0 0-5-6.482V.5zM8 3.5a5.5 5.5 0 1 1 0 11 5.5 5.5 0 0 1 0-11zm0 2a.5.5 0 0 1 .5.5v3a.5.5 0 0 1-1 0V6a.5.5 0 0 1 .5-.5z"/>
            </svg>
          </div>
          <span class="current-temp">{{ formatTempWithUnit(room.currentTemperature) }}</span>
        </div>
      </div>
      <div class="header-actions">
        <button v-if="!room.boost" class="boost-btn" @click.stop="handleBoost" title="Boost heating">
          <svg width="18" height="18" viewBox="0 0 16 16" fill="currentColor">
            <path d="M11.251.068a.5.5 0 01.227.58L9.677 6.5H13a.5.5 0 01.364.843l-8 8.5a.5.5 0 01-.842-.49L6.323 9.5H3a.5.5 0 01-.364-.843l8-8.5a.5.5 0 01.615-.09z"/>
          </svg>
          BOOST
        </button>
        <button class="collapse-btn" :class="{ collapsed: isCollapsed }" @click="toggleCollapse">
          <svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor">
            <path d="M5 7l5 5 5-5H5z"/>
          </svg>
        </button>
      </div>
    </div>

    <div v-show="!isCollapsed" class="room-card-content">
      <BoostCard
        v-if="room.boost"
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

        <button class="btn btn-add" @click="handleAdd">
          <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor">
            <path d="M8 0v16M0 8h16" stroke="currentColor" stroke-width="2"/>
          </svg>
          Add Schedule
        </button>
      </template>

      <ScheduleEditor
        v-if="showEditor"
        :schedule="editingSchedule"
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
  </div>
</template>

<script setup>
import { ref, computed } from 'vue'
import ScheduleItem from './ScheduleItem.vue'
import ScheduleEditor from './ScheduleEditor.vue'
import BoostModal from './BoostModal.vue'
import BoostCard from './BoostCard.vue'
import ConfirmModal from './ConfirmModal.vue'
import { useFormatting } from '../composables/useFormatting.js'

const { formatTempWithUnit } = useFormatting()

const props = defineProps({
  room: {
    type: Object,
    required: true
  },
  isExpanded: {
    type: Boolean,
    default: false
  }
})

const emit = defineEmits(['update-schedule', 'delete-schedule', 'add-schedule', 'boost', 'cancel-boost', 'toggle-expand'])

const isCollapsed = computed(() => !props.isExpanded)
const showEditor = ref(false)
const editingSchedule = ref(null)
const showBoost = ref(false)
const showDeleteConfirm = ref(false)
const scheduleToDelete = ref(null)

// Sort schedules by time
const sortedSchedules = computed(() => {
  return [...props.room.schedules].sort((a, b) => {
    return a.time.localeCompare(b.time)
  })
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
</script>

<style scoped>
.room-card {
  background: var(--bg-secondary);
  border-radius: 8px;
  box-shadow: 0 2px 4px var(--shadow);
  margin-bottom: 1rem;
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

.boost-btn {
  background: linear-gradient(135deg, var(--color-warning) 0%, #e67e22 100%);
  color: white;
  border: none;
  padding: 0.5rem 0.75rem;
  border-radius: 6px;
  cursor: pointer;
  font-weight: 600;
  font-size: 0.85rem;
  display: flex;
  align-items: center;
  gap: 0.35rem;
  transition: all 0.2s;
  box-shadow: 0 2px 4px rgba(243, 156, 18, 0.2);
}

.boost-btn:hover {
  transform: translateY(-1px);
  box-shadow: 0 4px 8px rgba(243, 156, 18, 0.3);
}

.boost-btn:active {
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

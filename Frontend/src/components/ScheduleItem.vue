<template>
  <div
    class="schedule-item"
    :class="{
      'is-active': isActive,
      'is-active-heating': isActive && heatingActive,
      'is-active-off': isActive && !heatingActive
    }"
    @click="handleEdit"
  >
    <div class="active-indicator" v-if="isActive" title="Currently active schedule">
      <div class="pulse-dot"></div>
    </div>
    <div class="schedule-info">
      <div class="schedule-primary">
        <div class="schedule-time">
          <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" class="icon">
            <path d="M8 0a8 8 0 100 16A8 8 0 008 0zm0 14.5a6.5 6.5 0 110-13 6.5 6.5 0 010 13z"/>
            <path d="M8 3v5l3.5 2-.75 1.25L6.5 8.5V3H8z"/>
          </svg>
          <span class="time-value">{{ formatTimeDisplay(schedule.time) }}</span>
        </div>

        <div class="schedule-temp">
          <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" class="icon">
            <path d="M6 2a2 2 0 012-2 2 2 0 012 2v7.5a3.5 3.5 0 11-4 0V2zm2-1a1 1 0 00-1 1v8a1 1 0 00.5.866 2.5 2.5 0 102.5 0A1 1 0 0010 10V2a1 1 0 00-1-1z"/>
          </svg>
          <span class="temp-value">{{ formatTempWithUnit(schedule.temperature) }}</span>
        </div>
      </div>

      <div class="schedule-meta">
        <div class="schedule-days">
          <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" class="icon">
            <path d="M3.5 0a.5.5 0 01.5.5V1h8V.5a.5.5 0 011 0V1h1a2 2 0 012 2v11a2 2 0 01-2 2H2a2 2 0 01-2-2V3a2 2 0 012-2h1V.5a.5.5 0 01.5-.5zM2 2a1 1 0 00-1 1v1h14V3a1 1 0 00-1-1H2zm13 3H1v9a1 1 0 001 1h12a1 1 0 001-1V5z"/>
          </svg>
          <span class="days-value">{{ daysText }}</span>
        </div>
        <div class="schedule-conditions" v-if="conditionsText">
          <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" class="icon">
            <path d="M8 0a8 8 0 100 16A8 8 0 008 0zM7 4h2v5H7V4zm0 6h2v2H7v-2z"/>
          </svg>
          <span class="conditions-value">{{ conditionsText }}</span>
        </div>
      </div>
    </div>

    <div class="schedule-actions">
      <button class="action-btn edit-btn" @click.stop="handleEdit" title="Edit">
        <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor">
          <path d="M12.146.146a.5.5 0 01.708 0l3 3a.5.5 0 010 .708l-10 10a.5.5 0 01-.168.11l-5 2a.5.5 0 01-.65-.65l2-5a.5.5 0 01.11-.168l10-10zM11.207 2.5L13.5 4.793 14.793 3.5 12.5 1.207 11.207 2.5zm1.586 2.793L10.5 3 1.293 12.207l-.834 2.084 2.084-.834L12.793 5.293z"/>
        </svg>
      </button>
      <button class="action-btn delete-btn" @click.stop="handleDelete" title="Delete">
        <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor">
          <path d="M5.5 5.5A.5.5 0 016 6v6a.5.5 0 01-1 0V6a.5.5 0 01.5-.5zm2.5 0a.5.5 0 01.5.5v6a.5.5 0 01-1 0V6a.5.5 0 01.5-.5zm3 .5a.5.5 0 00-1 0v6a.5.5 0 001 0V6z"/>
          <path fill-rule="evenodd" d="M14.5 3a1 1 0 01-1 1H13v9a2 2 0 01-2 2H5a2 2 0 01-2-2V4h-.5a1 1 0 01-1-1V2a1 1 0 011-1H6a1 1 0 011-1h2a1 1 0 011 1h3.5a1 1 0 011 1v1zM4.118 4L4 4.059V13a1 1 0 001 1h6a1 1 0 001-1V4.059L11.882 4H4.118zM2.5 3V2h11v1h-11z"/>
        </svg>
      </button>
    </div>
  </div>
</template>

<script setup>
import { computed } from 'vue'
import { useFormatting } from '../composables/useFormatting.js'

const { formatTempWithUnit, formatTimeDisplay, formatConditions, formatDays } = useFormatting()

const props = defineProps({
  schedule: {
    type: Object,
    required: true
  },
  isActive: {
    type: Boolean,
    default: false
  },
  heatingActive: {
    type: Boolean,
    default: false
  }
})

const conditionsText = computed(() => {
  return formatConditions(props.schedule.conditions)
})

const daysText = computed(() => {
  return formatDays(props.schedule.days)
})

const emit = defineEmits(['edit', 'delete'])

const handleEdit = () => {
  emit('edit', props.schedule)
}

const handleDelete = () => {
  emit('delete', props.schedule.id)
}
</script>

<style scoped>
.schedule-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1rem;
  background-color: var(--bg-tertiary);
  border-radius: 6px;
  border: 1px solid var(--border-color);
  transition: all 0.2s;
  position: relative;
  cursor: pointer;
}

.schedule-item:hover {
  border-color: var(--color-primary);
}

.schedule-item.is-active {
  /* Active indicator is shown via the pulse dot only */
}

.schedule-item.is-active-heating {
  background: linear-gradient(135deg, rgba(243, 156, 18, 0.1) 0%, rgba(230, 126, 34, 0.1) 100%);
  border: 2px solid #f39c12;
}

.schedule-item.is-active-off {
  background: linear-gradient(135deg, rgba(39, 174, 96, 0.1) 0%, rgba(46, 204, 113, 0.1) 100%);
  border: 2px solid #27ae60;
}

.active-indicator {
  position: absolute;
  left: 0.5rem;
  top: 50%;
  transform: translateY(-50%);
}

.pulse-dot {
  width: 8px;
  height: 8px;
  background-color: var(--color-primary);
  border-radius: 50%;
  position: relative;
  animation: pulse 2s ease-in-out infinite;
}

.pulse-dot::before {
  content: '';
  position: absolute;
  width: 100%;
  height: 100%;
  background-color: var(--color-primary);
  border-radius: 50%;
  animation: pulse-ring 2s ease-in-out infinite;
}

@keyframes pulse {
  0%, 100% {
    opacity: 1;
  }
  50% {
    opacity: 0.6;
  }
}

@keyframes pulse-ring {
  0% {
    transform: scale(1);
    opacity: 1;
  }
  100% {
    transform: scale(2.5);
    opacity: 0;
  }
}

.schedule-info {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  flex: 1;
  margin-left: 1rem;
}

.schedule-primary {
  display: flex;
  align-items: center;
  gap: 1.5rem;
}

.schedule-time,
.schedule-temp,
.schedule-days,
.schedule-conditions {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.9rem;
}

.schedule-meta {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.icon {
  color: var(--icon-color);
  flex-shrink: 0;
}

.time-value {
  font-weight: 600;
  color: var(--text-primary);
}

.temp-value {
  font-weight: 500;
  color: var(--color-danger);
}

.days-value {
  color: var(--text-primary);
  font-weight: 500;
}

.conditions-value {
  color: var(--text-secondary);
}

.schedule-actions {
  display: flex;
  gap: 0.5rem;
}

.action-btn {
  background: none;
  border: none;
  cursor: pointer;
  padding: 0.5rem;
  border-radius: 4px;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.2s;
}

.edit-btn {
  color: var(--color-primary);
}

.edit-btn:hover {
  background-color: var(--hover-bg);
}

.delete-btn {
  color: var(--color-danger);
}

.delete-btn:hover {
  background-color: var(--hover-bg);
}

.action-btn:active {
  transform: scale(0.95);
}

@media (max-width: 600px) {
  .schedule-item {
    padding: 0.75rem;
  }

  .schedule-info {
    font-size: 0.85rem;
  }

  .action-btn {
    padding: 0.4rem;
  }
}
</style>

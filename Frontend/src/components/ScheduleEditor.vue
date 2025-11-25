<template>
  <div class="editor-overlay" @click.self="handleCancel">
    <div class="editor-modal">
      <div class="editor-header">
        <h3>{{ isEditing ? 'Edit Schedule' : 'Add Schedule' }}</h3>
        <button class="close-btn" @click="handleCancel">
          <svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor">
            <path d="M4.646 4.646a.5.5 0 01.708 0L10 9.293l4.646-4.647a.5.5 0 01.708.708L10.707 10l4.647 4.646a.5.5 0 01-.708.708L10 10.707l-4.646 4.647a.5.5 0 01-.708-.708L9.293 10 4.646 5.354a.5.5 0 010-.708z"/>
          </svg>
        </button>
      </div>

      <form @submit.prevent="handleSubmit" class="editor-form">
        <div class="form-group">
          <label for="time">Time</label>
          <input
            id="time"
            v-model="formData.time"
            type="time"
            required
            class="form-control"
          />
        </div>

        <div class="form-group">
          <label for="temperature">Temperature (°C)</label>
          <input
            id="temperature"
            v-model.number="formData.temperature"
            type="number"
            step="0.5"
            min="5"
            max="30"
            required
            class="form-control"
          />
        </div>

        <div class="form-group">
          <label>Days of Week</label>
          <div class="days-selector">
            <button
              v-for="day in daysOfWeek"
              :key="day.value"
              type="button"
              class="day-btn"
              :class="{ active: selectedDays.includes(day.value) }"
              @click="toggleDay(day.value)"
            >
              {{ day.short }}
            </button>
          </div>
        </div>

        <div class="form-group">
          <label>Occupancy</label>
          <div class="occupancy-selector">
            <button
              type="button"
              class="occupancy-btn"
              :class="{ active: occupancy === 'Occupied' }"
              @click="toggleOccupancy('Occupied')"
            >
              Occupied
            </button>
            <button
              type="button"
              class="occupancy-btn"
              :class="{ active: occupancy === 'Unoccupied' }"
              @click="toggleOccupancy('Unoccupied')"
            >
              Unoccupied
            </button>
          </div>
          <small class="form-hint">Optional: Select occupancy requirement</small>
        </div>

        <div class="form-actions">
          <button type="button" class="btn btn-secondary" @click="handleCancel">
            Cancel
          </button>
          <button type="submit" class="btn btn-primary">
            {{ isEditing ? 'Save Changes' : 'Add Schedule' }}
          </button>
        </div>
      </form>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'

const props = defineProps({
  schedule: {
    type: Object,
    default: null
  }
})

const emit = defineEmits(['save', 'cancel'])

const daysOfWeek = [
  { value: 'Mon', short: 'M', full: 'Monday' },
  { value: 'Tue', short: 'T', full: 'Tuesday' },
  { value: 'Wed', short: 'W', full: 'Wednesday' },
  { value: 'Thu', short: 'T', full: 'Thursday' },
  { value: 'Fri', short: 'F', full: 'Friday' },
  { value: 'Sat', short: 'S', full: 'Saturday' },
  { value: 'Sun', short: 'S', full: 'Sunday' }
]

const formData = ref({
  time: '',
  temperature: 20
})

const selectedDays = ref([])
const occupancy = ref(null)

const isEditing = computed(() => props.schedule !== null)

onMounted(() => {
  if (props.schedule) {
    formData.value.time = props.schedule.time
    formData.value.temperature = props.schedule.temperature

    // Parse conditions
    if (props.schedule.conditions) {
      const conditions = props.schedule.conditions.split(',')
      const days = []

      conditions.forEach(condition => {
        const trimmed = condition.trim()
        if (daysOfWeek.some(d => d.value === trimmed)) {
          days.push(trimmed)
        } else if (trimmed === 'Occupied' || trimmed === 'Unoccupied') {
          occupancy.value = trimmed
        }
      })

      selectedDays.value = days
    }
  }
})

const toggleDay = (day) => {
  const index = selectedDays.value.indexOf(day)
  if (index > -1) {
    selectedDays.value.splice(index, 1)
  } else {
    selectedDays.value.push(day)
  }
}

const toggleOccupancy = (value) => {
  // Toggle off if clicking the same button, otherwise set to new value
  if (occupancy.value === value) {
    occupancy.value = null
  } else {
    occupancy.value = value
  }
}

const handleSubmit = () => {
  const conditions = []

  if (selectedDays.value.length > 0) {
    conditions.push(...selectedDays.value)
  }

  if (occupancy.value) {
    conditions.push(occupancy.value)
  }

  const scheduleData = {
    ...formData.value,
    conditions: conditions.join(',')
  }

  if (props.schedule) {
    scheduleData.id = props.schedule.id
  } else {
    scheduleData.id = Date.now()
  }

  emit('save', scheduleData)
}

const handleCancel = () => {
  emit('cancel')
}
</script>

<style scoped>
.editor-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background-color: var(--overlay);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
  padding: 1rem;
}

.editor-modal {
  background: var(--bg-secondary);
  border-radius: 8px;
  box-shadow: 0 4px 20px var(--shadow-md);
  width: 100%;
  max-width: 500px;
  max-height: 90vh;
  overflow-y: auto;
}

.editor-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1.5rem;
  border-bottom: 1px solid var(--border-color);
}

.editor-header h3 {
  font-size: 1.25rem;
  font-weight: 600;
  color: var(--text-primary);
  margin: 0;
}

.close-btn {
  background: none;
  border: none;
  cursor: pointer;
  color: var(--icon-color);
  padding: 0.25rem;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 4px;
  transition: all 0.2s;
}

.close-btn:hover {
  background-color: var(--hover-bg);
}

.editor-form {
  padding: 1.5rem;
}

.form-group {
  margin-bottom: 1.5rem;
}

.form-group label {
  display: block;
  margin-bottom: 0.5rem;
  font-weight: 500;
  color: var(--text-primary);
  font-size: 0.9rem;
}

.form-control {
  width: 100%;
  padding: 0.75rem;
  border: 1px solid var(--border-color);
  background: var(--bg-secondary);
  color: var(--text-primary);
  border-radius: 6px;
  font-size: 1rem;
  transition: all 0.2s;
}

.form-control:focus {
  outline: none;
  border-color: var(--color-primary);
  box-shadow: 0 0 0 3px rgba(52, 152, 219, 0.1);
}

.form-hint {
  display: block;
  margin-top: 0.25rem;
  font-size: 0.8rem;
  color: var(--text-secondary);
}

.days-selector {
  display: flex;
  gap: 0.5rem;
}

.day-btn {
  flex: 1;
  padding: 0.75rem;
  border: 2px solid var(--border-color);
  background: var(--bg-secondary);
  color: var(--text-primary);
  border-radius: 6px;
  cursor: pointer;
  font-weight: 600;
  transition: all 0.2s;
}

.day-btn:hover {
  border-color: var(--color-primary);
  background-color: var(--hover-bg);
}

.day-btn.active {
  background-color: var(--color-primary);
  border-color: var(--color-primary);
  color: white;
}

.day-btn:active {
  transform: scale(0.95);
}

.occupancy-selector {
  display: flex;
  gap: 0.75rem;
}

.occupancy-btn {
  flex: 1;
  padding: 0.75rem 1rem;
  border: 2px solid var(--border-color);
  background: var(--bg-secondary);
  color: var(--text-primary);
  border-radius: 6px;
  cursor: pointer;
  font-weight: 600;
  font-size: 1rem;
  transition: all 0.2s;
}

.occupancy-btn:hover {
  border-color: var(--color-primary);
  background-color: var(--hover-bg);
}

.occupancy-btn.active {
  background-color: var(--color-primary);
  border-color: var(--color-primary);
  color: white;
}

.occupancy-btn:active {
  transform: scale(0.95);
}

.form-actions {
  display: flex;
  gap: 0.75rem;
  margin-top: 2rem;
}

.btn {
  flex: 1;
  padding: 0.75rem 1.5rem;
  border: none;
  border-radius: 6px;
  font-size: 1rem;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s;
}

.btn-secondary {
  background-color: var(--btn-secondary-bg);
  color: var(--text-primary);
}

.btn-secondary:hover {
  background-color: var(--btn-secondary-hover);
}

.btn-primary {
  background-color: var(--color-primary);
  color: white;
}

.btn-primary:hover {
  background-color: var(--color-primary-hover);
}

.btn:active {
  transform: scale(0.98);
}

@media (max-width: 600px) {
  .editor-modal {
    max-width: 100%;
    border-radius: 0;
    max-height: 100vh;
  }

  .editor-header {
    padding: 1rem;
  }

  .editor-form {
    padding: 1rem;
  }

  .days-selector {
    gap: 0.25rem;
  }

  .day-btn {
    padding: 0.6rem;
    font-size: 0.9rem;
  }
}
</style>

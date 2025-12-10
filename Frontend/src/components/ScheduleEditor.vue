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
          <div class="temperature-selector">
            <button type="button" class="temp-btn" @click="decreaseTemp">
              <svg width="24" height="24" viewBox="0 0 16 16" fill="currentColor">
                <path d="M0 8a.5.5 0 01.5-.5h15a.5.5 0 010 1H.5A.5.5 0 010 8z"/>
              </svg>
            </button>
            <div class="temperature-display">
              <span class="temp-value">{{ formData.temperature.toFixed(1) }}</span>
              <span class="temp-unit">°{{ currentTempUnit }}</span>
            </div>
            <button type="button" class="temp-btn" @click="increaseTemp">
              <svg width="24" height="24" viewBox="0 0 16 16" fill="currentColor">
                <path d="M8 0a.5.5 0 01.5.5v7h7a.5.5 0 010 1h-7v7a.5.5 0 01-1 0v-7h-7a.5.5 0 010-1h7v-7A.5.5 0 018 0z"/>
              </svg>
            </button>
          </div>
        </div>

        <div class="form-group">
          <label>Days of Week</label>
          <div class="days-selector">
            <button
              v-for="day in daysOfWeek"
              :key="day.value"
              type="button"
              class="day-btn"
              :class="{ active: isDaySelected(day.value) }"
              @click="toggleDay(day.value)"
            >
              {{ day.short }}
            </button>
          </div>
        </div>

        <div class="form-group" v-if="canDetectRoomOccupancy">
          <label>Room Occupancy</label>
          <div class="occupancy-selector">
            <button
              type="button"
              class="occupancy-btn"
              :class="{ active: isRoomOccupancySelected(4) }"
              @click="setRoomOccupancy(4)"
            >
              Occupied
            </button>
            <button
              type="button"
              class="occupancy-btn"
              :class="{ active: isRoomOccupancySelected(8) }"
              @click="setRoomOccupancy(8)"
            >
              Unoccupied
            </button>
            <button
              type="button"
              class="occupancy-btn"
              :class="{ active: !isRoomOccupancySelected(4) && !isRoomOccupancySelected(8) }"
              @click="setRoomOccupancy(0)"
            >
              Any
            </button>
          </div>
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
import { useFormatting } from '../composables/useFormatting.js'

const { convertToDisplay, convertToInternal, currentTempUnit, getTempStep, roundTemp, getDisplayLimits } = useFormatting()

const props = defineProps({
  schedule: {
    type: Object,
    default: null
  },
  occupancyFilter: {
    type: String,
    default: null // 'occupied', 'vacant', or null for all
  },
  roomCapabilities: {
    type: Number,
    default: 3 // Default to both capabilities (CanSetTemperature | CanDetectRoomOccupancy)
  }
})

const emit = defineEmits(['save', 'cancel'])

// Check if room can detect occupancy (flag 2 in RoomCapabilities)
const canDetectRoomOccupancy = computed(() => {
  return (props.roomCapabilities & 2) !== 0
})

// Day bit flag values matching backend enum
const daysOfWeek = [
  { value: 1, short: 'M', full: 'Monday' },
  { value: 2, short: 'T', full: 'Tuesday' },
  { value: 4, short: 'W', full: 'Wednesday' },
  { value: 8, short: 'T', full: 'Thursday' },
  { value: 16, short: 'F', full: 'Friday' },
  { value: 32, short: 'S', full: 'Saturday' },
  { value: 64, short: 'S', full: 'Sunday' }
]

const formData = ref({
  time: '',
  temperature: 20,  // Will be in display unit
  days: 0,          // Bit flags for days
  conditions: 0     // Bit flags for conditions
})

const isEditing = computed(() => props.schedule !== null)

onMounted(() => {
  if (props.schedule) {
    // Editing existing schedule - load all values
    formData.value.time = props.schedule.time
    formData.value.temperature = convertToDisplay(props.schedule.temperature)
    formData.value.days = props.schedule.days || 0
    formData.value.conditions = props.schedule.conditions || 0
  } else {
    // New schedule - set defaults
    formData.value.temperature = convertToDisplay(20)
    formData.value.days = 0

    // Set house occupancy flag based on current filter (flags 1 and 2)
    // Room occupancy is left at 0 (user can set it if needed)
    if (props.occupancyFilter === 'occupied') {
      formData.value.conditions = 1 // HouseOccupied
    } else if (props.occupancyFilter === 'vacant') {
      formData.value.conditions = 2 // HouseUnoccupied
    } else {
      formData.value.conditions = 0 // No filter
    }
  }
})

const isDaySelected = (dayValue) => {
  return (formData.value.days & dayValue) !== 0
}

const toggleDay = (dayValue) => {
  if (isDaySelected(dayValue)) {
    // Remove the day
    formData.value.days = formData.value.days & ~dayValue
  } else {
    // Add the day
    formData.value.days = formData.value.days | dayValue
  }
}

// Room occupancy selection (flags 4 and 8)
const isRoomOccupancySelected = (conditionValue) => {
  if (conditionValue === 0) {
    // "Any" is selected if NEITHER RoomInUse nor RoomNotInUse flags are set
    return (formData.value.conditions & 12) === 0 // 12 = 4 | 8
  }
  // For RoomInUse (4) or RoomNotInUse (8), check if that specific flag is set
  // but not both (to distinguish from having both set)
  const hasBoth = (formData.value.conditions & 12) === 12
  if (hasBoth) return false
  return (formData.value.conditions & conditionValue) !== 0
}

const setRoomOccupancy = (conditionValue) => {
  // Clear both room occupancy flags first (4 = RoomInUse, 8 = RoomNotInUse)
  formData.value.conditions = formData.value.conditions & ~12 // 12 = 4 | 8

  if (conditionValue !== 0) {
    // Set the specific room occupancy flag (either 4 or 8)
    formData.value.conditions = formData.value.conditions | conditionValue
  }
  // If conditionValue is 0, we just cleared both flags (which means "Any")
}

const increaseTemp = () => {
  const limits = getDisplayLimits()
  const step = getTempStep()
  const newTemp = formData.value.temperature + step
  if (newTemp <= limits.max) {
    formData.value.temperature = roundTemp(newTemp)
  }
}

const decreaseTemp = () => {
  const limits = getDisplayLimits()
  const step = getTempStep()
  const newTemp = formData.value.temperature - step
  if (newTemp >= limits.min) {
    formData.value.temperature = roundTemp(newTemp)
  }
}

const handleSubmit = () => {
  // Get the current conditions value
  let conditions = formData.value.conditions

  // If creating a new schedule or editing, ensure house occupancy flag is set
  // based on current filter (preserve existing house flags if editing)
  if (!props.schedule) {
    // New schedule - set house occupancy based on filter
    // Clear any existing house occupancy flags first
    conditions = conditions & ~3 // Clear flags 1 and 2

    if (props.occupancyFilter === 'occupied') {
      conditions = conditions | 1 // Set HouseOccupied
    } else if (props.occupancyFilter === 'vacant') {
      conditions = conditions | 2 // Set HouseUnoccupied
    }
  }
  // If editing existing schedule, keep the house occupancy flags as they are

  const scheduleData = {
    time: formData.value.time,
    temperature: convertToInternal(formData.value.temperature),
    days: formData.value.days,
    conditions: conditions,
    rampUpMinutes: 30,
    conditionOperator: 1
  }

  if (props.schedule) {
    scheduleData.id = props.schedule.id
  } else {
    scheduleData.id = 0
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
  font-size: 1.25rem;
  text-align: center;
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

.temperature-selector {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 1.5rem;
  padding: 1rem 0;
}

.temp-btn {
  background: var(--color-primary);
  border: none;
  color: white;
  width: 48px;
  height: 48px;
  border-radius: 50%;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.2s;
}

.temp-btn:hover {
  background: var(--color-primary-hover);
  transform: scale(1.05);
}

.temp-btn:active {
  transform: scale(0.95);
}

.temperature-display {
  display: flex;
  align-items: baseline;
  justify-content: center;
  gap: 0.25rem;
  min-width: 140px;
}

.temp-value {
  font-size: 3rem;
  font-weight: 700;
  color: var(--color-primary);
  line-height: 1;
}

.temp-unit {
  font-size: 1.5rem;
  font-weight: 500;
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
  padding: 0.5rem 0.75rem;
  border: 2px solid var(--border-color);
  background: var(--bg-secondary);
  color: var(--text-primary);
  border-radius: 6px;
  cursor: pointer;
  font-weight: 600;
  font-size: 0.9rem;
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

  .temperature-display {
    min-width: 120px;
  }

  .temp-value {
    font-size: 2.5rem;
  }

  .temp-unit {
    font-size: 1.25rem;
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

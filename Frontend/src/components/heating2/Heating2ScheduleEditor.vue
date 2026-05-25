<template>
  <div class="modal-overlay" @click.self="$emit('close')">
    <div class="modal-panel">
      <div class="modal-header">
        <h3>{{ schedule ? 'Edit schedule' : 'Add schedule' }}</h3>
        <button class="btn-close" @click="$emit('close')">
          <svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor">
            <path d="M2.146 2.854a.5.5 0 1 1 .708-.708L8 7.293l5.146-5.147a.5.5 0 0 1 .708.708L8.707 8l5.147 5.146a.5.5 0 0 1-.708.708L8 8.707l-5.146 5.147a.5.5 0 0 1-.708-.708L7.293 8z"/>
          </svg>
        </button>
      </div>

      <div class="modal-body">
        <div class="form-row">
          <div class="form-group">
            <label>Time</label>
            <input v-model="form.time" type="time" class="form-input" />
          </div>
          <div class="form-group">
            <label>Temperature (°C)</label>
            <input v-model.number="form.temperature" type="number" class="form-input" min="5" max="35" step="0.5" />
          </div>
        </div>

        <div class="form-group">
          <label>Ramp up (minutes)</label>
          <input v-model.number="form.rampUpMinutes" type="number" class="form-input" min="0" max="120" />
        </div>

        <div class="form-group">
          <label>Days</label>
          <div class="days-selector">
            <button
              v-for="day in dayOptions"
              :key="day.flag"
              class="day-btn"
              :class="{ active: (form.days & day.flag) !== 0 }"
              @click="toggleDay(day.flag)"
            >{{ day.label }}</button>
          </div>
          <div class="day-presets">
            <button class="preset-btn" @click="form.days = 31">Weekdays</button>
            <button class="preset-btn" @click="form.days = 96">Weekends</button>
            <button class="preset-btn" @click="form.days = 127">Every day</button>
          </div>
        </div>

        <div class="form-group">
          <label>Conditions</label>
          <div class="conditions-selector">
            <label class="checkbox-label" v-for="cond in conditionOptions" :key="cond.flag">
              <input
                type="checkbox"
                :checked="(form.conditions & cond.flag) !== 0"
                @change="toggleCondition(cond.flag)"
              />
              <span>{{ cond.label }}</span>
            </label>
          </div>
        </div>
      </div>

      <div class="modal-footer">
        <button class="btn btn-secondary" @click="$emit('close')">Cancel</button>
        <button class="btn btn-primary" :disabled="!isValid" @click="handleSave">
          {{ schedule ? 'Save' : 'Add' }}
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { reactive, computed } from 'vue'

const props = defineProps({
  schedule: {
    type: Object,
    default: null
  },
  roomId: {
    type: String,
    required: true
  }
})

const emit = defineEmits(['save', 'close'])

const dayOptions = [
  { flag: 1, label: 'M' },
  { flag: 2, label: 'T' },
  { flag: 4, label: 'W' },
  { flag: 8, label: 'Th' },
  { flag: 16, label: 'F' },
  { flag: 32, label: 'Sa' },
  { flag: 64, label: 'Su' }
]

const conditionOptions = [
  { flag: 1, label: 'House occupied' },
  { flag: 2, label: 'House unoccupied' },
  { flag: 4, label: 'Room in use' },
  { flag: 8, label: 'Room not in use' }
]

const form = reactive({
  id: props.schedule?.id || null,
  time: props.schedule?.time || '08:00',
  temperature: props.schedule?.temperature ?? 20,
  rampUpMinutes: props.schedule?.rampUpMinutes ?? 0,
  days: props.schedule?.days ?? 127,
  conditions: props.schedule?.conditions ?? 0
})

const isValid = computed(() => form.time && form.temperature >= 5 && form.days > 0)

function toggleDay(flag) {
  form.days ^= flag
}

function toggleCondition(flag) {
  form.conditions ^= flag
}

function handleSave() {
  if (!isValid.value) return
  emit('save', {
    ...(form.id ? { id: form.id } : {}),
    time: form.time,
    temperature: form.temperature,
    rampUpMinutes: form.rampUpMinutes,
    days: form.days,
    conditions: form.conditions
  })
}
</script>

<style scoped>
.modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.5);
  z-index: 1000;
  display: flex;
  align-items: center;
  justify-content: center;
  animation: fadeIn 0.2s;
}

.modal-panel {
  background: var(--bg-primary);
  border-radius: 12px;
  width: 90%;
  max-width: 420px;
  animation: slideUp 0.2s;
}

.modal-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1rem 1.25rem;
  border-bottom: 1px solid var(--border-color);
}

.modal-header h3 {
  margin: 0;
  font-size: 1.1rem;
  font-weight: 600;
  color: var(--text-primary);
}

.btn-close {
  background: none;
  border: none;
  color: var(--text-tertiary);
  cursor: pointer;
  padding: 0.25rem;
  border-radius: 4px;
}

.btn-close:hover {
  color: var(--text-primary);
}

.modal-body {
  padding: 1.25rem;
}

.form-group {
  margin-bottom: 1rem;
}

.form-group:last-child {
  margin-bottom: 0;
}

.form-group label {
  display: block;
  font-size: 0.85rem;
  font-weight: 500;
  color: var(--text-secondary);
  margin-bottom: 0.4rem;
}

.form-input {
  width: 100%;
  padding: 0.6rem 0.75rem;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  color: var(--text-primary);
  font-size: 0.9rem;
  box-sizing: border-box;
}

.form-input:focus {
  outline: none;
  border-color: var(--color-primary);
}

.form-row {
  display: flex;
  gap: 0.75rem;
}

.form-row .form-group {
  flex: 1;
}

.days-selector {
  display: flex;
  gap: 0.3rem;
  margin-bottom: 0.4rem;
}

.day-btn {
  flex: 1;
  padding: 0.4rem 0;
  background: var(--bg-secondary);
  border: 2px solid var(--border-color);
  border-radius: 6px;
  color: var(--text-secondary);
  font-size: 0.8rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.15s;
}

.day-btn.active {
  border-color: var(--color-primary);
  background: var(--color-primary);
  color: white;
}

.day-presets {
  display: flex;
  gap: 0.4rem;
}

.preset-btn {
  padding: 0.2rem 0.5rem;
  background: none;
  border: 1px dashed var(--border-color);
  border-radius: 4px;
  color: var(--text-tertiary);
  font-size: 0.75rem;
  cursor: pointer;
}

.preset-btn:hover {
  border-color: var(--color-primary);
  color: var(--color-primary);
}

.conditions-selector {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
}

.checkbox-label {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.85rem;
  color: var(--text-primary);
  cursor: pointer;
}

.checkbox-label input[type="checkbox"] {
  accent-color: var(--color-primary);
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 0.5rem;
  padding: 1rem 1.25rem;
  border-top: 1px solid var(--border-color);
}

.btn {
  padding: 0.5rem 1rem;
  border: none;
  border-radius: 6px;
  font-size: 0.9rem;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.15s;
}

.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.btn-primary {
  background: var(--color-primary);
  color: white;
}

.btn-primary:hover:not(:disabled) {
  background: var(--color-primary-hover);
}

.btn-secondary {
  background: var(--bg-tertiary);
  color: var(--text-primary);
}

@keyframes fadeIn {
  from { opacity: 0; }
  to { opacity: 1; }
}

@keyframes slideUp {
  from { transform: translateY(20px); opacity: 0; }
  to { transform: translateY(0); opacity: 1; }
}
</style>

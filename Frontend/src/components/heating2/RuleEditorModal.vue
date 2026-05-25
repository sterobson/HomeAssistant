<template>
  <div class="modal-overlay" @click.self="$emit('close')">
    <div class="modal-panel">
      <div class="modal-header">
        <h3>{{ rule ? 'Edit rule' : 'Add rule' }}</h3>
        <button class="btn-close" @click="$emit('close')">
          <svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor">
            <path d="M2.146 2.854a.5.5 0 1 1 .708-.708L8 7.293l5.146-5.147a.5.5 0 0 1 .708.708L8.707 8l5.147 5.146a.5.5 0 0 1-.708.708L8 8.707l-5.146 5.147a.5.5 0 0 1-.708-.708L7.293 8z"/>
          </svg>
        </button>
      </div>

      <div class="modal-body">
        <div class="form-group">
          <label>Name</label>
          <input
            v-model="form.name"
            type="text"
            class="form-input"
            placeholder="e.g. Lounge motion"
            ref="nameInput"
          />
        </div>

        <div class="form-group">
          <label>Type</label>
          <div class="type-selector">
            <button
              v-for="rt in ruleTypes"
              :key="rt.value"
              class="type-btn"
              :class="{ active: form.type === rt.value, ['type-' + rt.value]: true }"
              @click="form.type = rt.value"
            >
              {{ rt.label }}
            </button>
          </div>
        </div>

        <!-- Presence rule fields -->
        <template v-if="form.type === 'presence'">
          <div class="form-group">
            <label>Motion sensor entity</label>
            <SearchableSelect
              :options="sensorEntities"
              v-model="form.sensorEntityId"
              placeholder="Select motion sensor..."
                          />
          </div>
          <div class="form-group">
            <label>Motion within (minutes)</label>
            <input
              v-model.number="form.motionWithinMinutes"
              type="number"
              class="form-input"
              min="1"
              max="1440"
            />
          </div>
        </template>

        <!-- Power rule fields -->
        <template v-if="form.type === 'power'">
          <div class="form-group">
            <label>Power sensor entity</label>
            <SearchableSelect
              :options="sensorEntities"
              v-model="form.sensorEntityId"
              placeholder="Select power sensor..."
                          />
          </div>
          <div class="form-group">
            <label>Power threshold (watts)</label>
            <input
              v-model.number="form.powerThresholdWatts"
              type="number"
              class="form-input"
              min="0"
              step="0.1"
            />
          </div>
        </template>

        <!-- Time rule fields -->
        <template v-if="form.type === 'time'">
          <div class="form-row">
            <div class="form-group">
              <label>Start time</label>
              <input
                v-model="form.startTime"
                type="time"
                class="form-input"
              />
            </div>
            <div class="form-group">
              <label>End time</label>
              <input
                v-model="form.endTime"
                type="time"
                class="form-input"
              />
            </div>
          </div>
        </template>
      </div>

      <div class="modal-footer">
        <button class="btn btn-secondary" @click="$emit('close')">Cancel</button>
        <button class="btn btn-primary" :disabled="!isValid" @click="handleSave">
          {{ rule ? 'Save' : 'Add' }}
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, reactive, computed, onMounted, nextTick } from 'vue'
import SearchableSelect from '../SearchableSelect.vue'
import { useDeviceSettings } from '../../composables/useDeviceSettings.js'

const props = defineProps({
  rule: {
    type: Object,
    default: null
  }
})

const emit = defineEmits(['save', 'close'])

const { loadEntities, entitiesByDomain } = useDeviceSettings()
const nameInput = ref(null)

const ruleTypes = [
  { value: 'presence', label: 'Presence' },
  { value: 'power', label: 'Power' },
  { value: 'time', label: 'Time' }
]

const sensorEntities = entitiesByDomain('sensor')

const form = reactive({
  id: props.rule?.id || null,
  name: props.rule?.name || '',
  type: props.rule?.type || 'presence',
  sensorEntityId: props.rule?.sensorEntityId || null,
  motionWithinMinutes: props.rule?.motionWithinMinutes ?? 15,
  powerThresholdWatts: props.rule?.powerThresholdWatts ?? 10,
  startTime: props.rule?.startTime || '08:00',
  endTime: props.rule?.endTime || '22:00'
})

const isValid = computed(() => {
  if (!form.name.trim()) return false
  if (form.type === 'presence' && !form.sensorEntityId) return false
  if (form.type === 'power' && !form.sensorEntityId) return false
  if (form.type === 'time' && (!form.startTime || !form.endTime)) return false
  return true
})

onMounted(async () => {
  await loadEntities()
  await nextTick()
  nameInput.value?.focus()
})

function handleSave() {
  if (!isValid.value) return

  const data = {
    ...(form.id ? { id: form.id } : {}),
    name: form.name.trim(),
    type: form.type
  }

  if (form.type === 'presence') {
    data.sensorEntityId = form.sensorEntityId
    data.motionWithinMinutes = form.motionWithinMinutes
  } else if (form.type === 'power') {
    data.sensorEntityId = form.sensorEntityId
    data.powerThresholdWatts = form.powerThresholdWatts
  } else if (form.type === 'time') {
    data.startTime = form.startTime
    data.endTime = form.endTime
  }

  emit('save', data)
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
  z-index: 1100;
  display: flex;
  align-items: center;
  justify-content: center;
  animation: fadeIn 0.2s;
}

.modal-panel {
  background: var(--bg-primary);
  border-radius: 12px;
  width: 90%;
  max-width: 450px;
  max-height: 85vh;
  overflow-y: auto;
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

.type-selector {
  display: flex;
  gap: 0.5rem;
}

.type-btn {
  flex: 1;
  padding: 0.5rem;
  background: var(--bg-secondary);
  border: 2px solid var(--border-color);
  border-radius: 8px;
  color: var(--text-secondary);
  font-size: 0.85rem;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.15s;
}

.type-btn.active.type-presence {
  border-color: #2d6a4f;
  background: #2d6a4f15;
  color: #2d6a4f;
}

.type-btn.active.type-power {
  border-color: #e85907;
  background: #e8590715;
  color: #e85907;
}

.type-btn.active.type-time {
  border-color: #1971c2;
  background: #1971c215;
  color: #1971c2;
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

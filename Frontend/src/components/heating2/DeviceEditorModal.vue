<template>
  <div class="modal-overlay" @click.self="$emit('close')">
    <div class="modal-panel">
      <div class="modal-header">
        <h3>{{ device ? 'Edit device' : 'Add device' }}</h3>
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
            placeholder="e.g. Living room valve"
            ref="nameInput"
          />
        </div>

        <div class="form-group">
          <label>Type</label>
          <select v-model.number="form.type" class="form-input">
            <option v-for="dt in deviceTypes" :key="dt.value" :value="dt.value">{{ dt.label }}</option>
          </select>
        </div>

        <!-- Sensor entity (valve, temp sensor, humidity sensor, presence sensor) -->
        <div v-if="showSensorEntity" class="form-group">
          <label>Sensor entity</label>
          <SearchableSelect
            :options="sensorEntities"
            v-model="form.sensorEntityId"
            placeholder="Select sensor entity..."
          />
        </div>

        <!-- Climate entity (valve) -->
        <div v-if="showClimateEntity" class="form-group">
          <label>Climate entity</label>
          <SearchableSelect
            :options="climateEntities"
            v-model="form.targetEntityId"
            placeholder="Select climate entity..."
          />
        </div>

        <!-- Switch entity (boiler, electric heater, plug socket) -->
        <div v-if="showSwitchEntity" class="form-group">
          <label>Switch entity</label>
          <SearchableSelect
            :options="switchEntities"
            v-model="form.switchEntityId"
            placeholder="Select switch entity..."
          />
        </div>

        <!-- Power sensor (plug socket) -->
        <div v-if="showPowerSensor" class="form-group">
          <label>Power sensor entity</label>
          <SearchableSelect
            :options="sensorEntities"
            v-model="form.powerSensorEntityId"
            placeholder="Select power sensor..."
          />
        </div>

        <!-- Control mode (boiler) -->
        <div v-if="showControlMode" class="form-group">
          <label>Control mode</label>
          <select v-model.number="form.controlMode" class="form-input">
            <option :value="0">On when on</option>
            <option :value="1">Off when on</option>
            <option :value="2">Toggle</option>
          </select>
        </div>
      </div>

      <div class="modal-footer">
        <button class="btn btn-secondary" @click="$emit('close')">Cancel</button>
        <button class="btn btn-primary" :disabled="!isValid" @click="handleSave">
          {{ device ? 'Save' : 'Add' }}
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
  device: {
    type: Object,
    default: null
  },
  roomId: {
    type: String,
    required: true
  }
})

const emit = defineEmits(['save', 'close'])

const { entities, loadEntities, entitiesByDomain } = useDeviceSettings()
const nameInput = ref(null)

const deviceTypes = [
  { value: 0, label: 'Radiator valve' },
  { value: 1, label: 'Gas boiler' },
  { value: 2, label: 'Temperature sensor' },
  { value: 3, label: 'Humidity sensor' },
  { value: 4, label: 'Electric heater' },
  { value: 5, label: 'Presence sensor' },
  { value: 6, label: 'Plug socket' }
]

const form = reactive({
  id: props.device?.id || null,
  name: props.device?.name || '',
  type: props.device?.type ?? 0,
  sensorEntityId: props.device?.sensorEntityId || null,
  targetEntityId: props.device?.targetEntityId || null,
  switchEntityId: props.device?.switchEntityId || null,
  powerSensorEntityId: props.device?.powerSensorEntityId || null,
  controlMode: props.device?.controlMode ?? null
})

const sensorEntities = entitiesByDomain('sensor')
const climateEntities = entitiesByDomain('climate')
const switchEntities = computed(() => {
  const switches = entitiesByDomain('switch').value
  const inputs = entitiesByDomain('input_boolean').value
  return [...switches, ...inputs]
})

const showSensorEntity = computed(() => [2, 3, 5].includes(form.type))
const showClimateEntity = computed(() => form.type === 0)
const showSwitchEntity = computed(() => [1, 4, 6].includes(form.type))
const showPowerSensor = computed(() => form.type === 6)
const showControlMode = computed(() => form.type === 1)

const isValid = computed(() => form.name.trim().length > 0)

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
    type: form.type,
    sensorEntityId: showSensorEntity.value ? form.sensorEntityId : null,
    targetEntityId: showClimateEntity.value ? form.targetEntityId : null,
    switchEntityId: showSwitchEntity.value ? form.switchEntityId : null,
    powerSensorEntityId: showPowerSensor.value ? form.powerSensorEntityId : null,
    controlMode: showControlMode.value ? form.controlMode : null,
    ruleIds: props.device?.ruleIds || [],
    ruleCombinator: props.device?.ruleCombinator ?? 0
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

select.form-input {
  cursor: pointer;
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

.btn-secondary:hover {
  background: var(--border-color);
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

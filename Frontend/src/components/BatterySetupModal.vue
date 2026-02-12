<template>
  <div class="setup-overlay" @click.self="handleCancel">
    <div class="setup-modal">
      <div class="setup-header">
        <h3>Battery setup</h3>
        <button class="close-btn" @click="handleCancel">
          <svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor">
            <path d="M4.646 4.646a.5.5 0 01.708 0L10 9.293l4.646-4.647a.5.5 0 01.708.708L10.707 10l4.647 4.646a.5.5 0 01-.708.708L10 10.707l-4.646 4.647a.5.5 0 01-.708-.708L9.293 10 4.646 5.354a.5.5 0 010-.708z"/>
          </svg>
        </button>
      </div>

      <div v-if="loading" class="setup-loading">Loading...</div>

      <template v-else>
        <div class="tab-bar">
          <button
            v-for="tab in tabs"
            :key="tab.key"
            class="tab-btn"
            :class="{ active: activeTab === tab.key }"
            @click="activeTab = tab.key"
          >
            {{ tab.label }}
          </button>
        </div>

        <div class="setup-form">
          <!-- Battery tab -->
          <template v-if="activeTab === 'battery'">
            <div class="form-group">
              <label>Charge % sensor</label>
              <SearchableSelect
                v-model="form.batteryChargePercentSensorId"
                :options="sensorEntities"
                placeholder="Select sensor..."
              />
            </div>

            <div class="form-group">
              <label>Power charge sensor</label>
              <SearchableSelect
                v-model="form.totalBatteryPowerChargeSensorId"
                :options="sensorEntities"
                placeholder="Select sensor..."
              />
            </div>

            <div class="form-group">
              <label>Charger use mode</label>
              <SearchableSelect
                v-model="form.chargerUseModeSelectorId"
                :options="selectEntities"
                placeholder="Select entity..."
              />
            </div>

            <div class="form-group">
              <label>Manual mode</label>
              <SearchableSelect
                v-model="form.manualModeSelectorId"
                :options="selectEntities"
                placeholder="Select entity..."
              />
            </div>

            <div class="form-group">
              <label>Max charge current entity</label>
              <SearchableSelect
                v-model="form.batteryChargeMaxCurrentNumberId"
                :options="numberEntities"
                placeholder="Select entity..."
              />
            </div>

            <div class="form-group">
              <label>Max charge current (A)</label>
              <input
                type="number"
                v-model.number="form.maxChargeCurrentAmps"
                class="text-input"
                step="1"
                min="1"
                placeholder="e.g. 50"
              />
            </div>

            <div class="form-group">
              <label>Capacity (kWh)</label>
              <input
                type="number"
                v-model.number="form.batteryCapacityKwh"
                class="text-input"
                step="0.1"
                min="0"
                placeholder="e.g. 20.4"
              />
            </div>

            <div class="form-group">
              <label>Export control limit (W)</label>
              <SearchableSelect
                v-model="form.exportLimitNumberId"
                :options="numberEntities"
                placeholder="Select entity..."
              />
            </div>

          </template>

          <!-- Solar tab -->
          <template v-if="activeTab === 'solar'">
            <div class="form-group">
              <label>PV power sensor</label>
              <SearchableSelect
                v-model="form.totalPvPowerSensorId"
                :options="sensorEntities"
                placeholder="Select sensor..."
              />
            </div>
          </template>

          <!-- Grid tab -->
          <template v-if="activeTab === 'grid'">
            <div class="form-group">
              <label>Import rate sensor</label>
              <SearchableSelect
                v-model="form.electricityRateSensorId"
                :options="sensorEntities"
                placeholder="Select sensor..."
              />
            </div>

            <div class="form-group">
              <label>Export rate sensor</label>
              <SearchableSelect
                v-model="form.exportRateSensorId"
                :options="sensorEntities"
                placeholder="Select sensor..."
              />
            </div>
          </template>

          <!-- Car charger tab -->
          <template v-if="activeTab === 'carCharger'">
            <div class="form-group">
              <label>Charger current sensor</label>
              <SearchableSelect
                v-model="form.chargerCurrentSensorId"
                :options="sensorEntities"
                placeholder="Select sensor..."
              />
            </div>
          </template>
        </div>
      </template>

      <div v-if="saveError" class="error-message">{{ saveError }}</div>

      <div class="setup-footer">
        <button class="btn btn-cancel" @click="handleCancel">Cancel</button>
        <button class="btn btn-save" @click="handleSave" :disabled="saving">
          {{ saving ? 'Saving...' : 'Save' }}
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, reactive, onMounted } from 'vue'
import SearchableSelect from './SearchableSelect.vue'
import { useDeviceSettings } from '../composables/useDeviceSettings.js'

const emit = defineEmits(['save', 'cancel'])

const { loadEntities, loadSettings, saveSettings, settings, entitiesByDomain, loading: settingsLoading } = useDeviceSettings()

const loading = ref(true)
const saving = ref(false)
const saveError = ref(null)
const activeTab = ref('battery')

const tabs = [
  { key: 'battery', label: 'Battery' },
  { key: 'solar', label: 'Solar' },
  { key: 'grid', label: 'Grid' },
  { key: 'carCharger', label: 'Car charger' }
]

const sensorEntities = entitiesByDomain('sensor')
const selectEntities = entitiesByDomain('select')
const numberEntities = entitiesByDomain('number')

const form = reactive({
  batteryChargePercentSensorId: '',
  chargerUseModeSelectorId: '',
  manualModeSelectorId: '',
  batteryChargeMaxCurrentNumberId: '',
  totalBatteryPowerChargeSensorId: '',
  exportLimitNumberId: '',
  totalPvPowerSensorId: '',
  electricityRateSensorId: '',
  exportRateSensorId: '',
  chargerCurrentSensorId: '',
  batteryCapacityKwh: null,
  maxChargeCurrentAmps: null
})

onMounted(async () => {
  try {
    await Promise.all([loadEntities(), loadSettings()])

    if (settings.value?.battery) {
      const b = settings.value.battery
      form.batteryChargePercentSensorId = b.batteryChargePercentSensorId || ''
      form.chargerUseModeSelectorId = b.chargerUseModeSelectorId || ''
      form.manualModeSelectorId = b.manualModeSelectorId || ''
      form.batteryChargeMaxCurrentNumberId = b.batteryChargeMaxCurrentNumberId || ''
      form.totalBatteryPowerChargeSensorId = b.totalBatteryPowerChargeSensorId || ''
      form.exportLimitNumberId = b.exportLimitNumberId || ''
      form.totalPvPowerSensorId = b.totalPvPowerSensorId || ''
      form.electricityRateSensorId = b.electricityRateSensorId || ''
      form.exportRateSensorId = b.exportRateSensorId || ''
      form.batteryCapacityKwh = b.batteryCapacityKwh ?? null
      form.maxChargeCurrentAmps = b.maxChargeCurrentAmps ?? null
    }

    if (settings.value?.carCharger) {
      const c = settings.value.carCharger
      form.chargerCurrentSensorId = c.chargerCurrentSensorId || ''
    }
  } finally {
    loading.value = false
  }
})

function handleCancel() {
  emit('cancel')
}

async function handleSave() {
  saving.value = true
  saveError.value = null

  try {
    const payload = {
      battery: {
        batteryChargePercentSensorId: form.batteryChargePercentSensorId || null,
        chargerUseModeSelectorId: form.chargerUseModeSelectorId || null,
        manualModeSelectorId: form.manualModeSelectorId || null,
        batteryChargeMaxCurrentNumberId: form.batteryChargeMaxCurrentNumberId || null,
        totalBatteryPowerChargeSensorId: form.totalBatteryPowerChargeSensorId || null,
        exportLimitNumberId: form.exportLimitNumberId || null,
        totalPvPowerSensorId: form.totalPvPowerSensorId || null,
        electricityRateSensorId: form.electricityRateSensorId || null,
        exportRateSensorId: form.exportRateSensorId || null,
        batteryCapacityKwh: form.batteryCapacityKwh || null,
        maxChargeCurrentAmps: form.maxChargeCurrentAmps || null
      },
      carCharger: {
        chargerCurrentSensorId: form.chargerCurrentSensorId || null
      }
    }

    await saveSettings(payload)
    emit('save')
  } catch (err) {
    saveError.value = err.message || 'Failed to save settings'
  } finally {
    saving.value = false
  }
}
</script>

<style scoped>
.setup-overlay {
  position: fixed;
  inset: 0;
  background-color: var(--overlay);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
  animation: fadeIn 0.2s;
}

.setup-modal {
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 12px;
  width: 90%;
  max-width: 520px;
  max-height: 90vh;
  display: flex;
  flex-direction: column;
  box-shadow: 0 8px 32px var(--shadow-md);
  animation: slideUp 0.25s;
}

.setup-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1rem 1.25rem;
  border-bottom: 1px solid var(--border-color);
}

.setup-header h3 {
  margin: 0;
  font-size: 1.1rem;
  font-weight: 600;
  color: var(--text-primary);
}

.close-btn {
  background: none;
  border: none;
  color: var(--icon-color);
  cursor: pointer;
  padding: 4px;
  border-radius: 4px;
  transition: background-color 0.15s, color 0.15s;
}

.close-btn:hover {
  color: var(--text-primary);
  background-color: var(--hover-bg);
}

.tab-bar {
  display: flex;
  border-bottom: 1px solid var(--border-color);
  padding: 0 1.25rem;
  gap: 0.25rem;
}

.tab-btn {
  background: none;
  border: none;
  border-bottom: 2px solid transparent;
  padding: 0.65rem 0.75rem;
  font-size: 0.85rem;
  font-weight: 500;
  color: var(--text-secondary);
  cursor: pointer;
  transition: color 0.15s, border-color 0.15s;
}

.tab-btn:hover {
  color: var(--text-primary);
}

.tab-btn.active {
  color: var(--color-primary);
  border-bottom-color: var(--color-primary);
}

.setup-loading {
  padding: 2rem;
  text-align: center;
  color: var(--text-secondary);
}

.setup-form {
  padding: 1rem 1.25rem;
  overflow-y: auto;
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 0.85rem;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}

.form-group label {
  font-size: 0.8rem;
  font-weight: 500;
  color: var(--text-secondary);
}

.text-input {
  padding: 0.5rem 0.75rem;
  background: var(--bg-tertiary);
  color: var(--text-primary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  font-size: 0.85rem;
  outline: none;
  width: 100%;
  box-sizing: border-box;
}

.text-input:focus {
  border-color: var(--color-primary);
}

.error-message {
  padding: 0.5rem 1.25rem;
  color: var(--color-danger);
  font-size: 0.8rem;
}

.setup-footer {
  display: flex;
  justify-content: flex-end;
  gap: 0.5rem;
  padding: 1rem 1.25rem;
  border-top: 1px solid var(--border-color);
}

.btn {
  padding: 0.5rem 1.25rem;
  border: none;
  border-radius: 6px;
  font-size: 0.85rem;
  font-weight: 500;
  cursor: pointer;
  transition: background-color 0.15s;
}

.btn-cancel {
  background-color: var(--btn-secondary-bg);
  color: var(--text-primary);
}

.btn-cancel:hover {
  background-color: var(--btn-secondary-hover);
}

.btn-save {
  background-color: var(--color-primary);
  color: white;
}

.btn-save:hover:not(:disabled) {
  background-color: var(--color-primary-hover);
}

.btn-save:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

@keyframes fadeIn {
  from { opacity: 0; }
  to { opacity: 1; }
}

@keyframes slideUp {
  from { opacity: 0; transform: translateY(20px); }
  to { opacity: 1; transform: translateY(0); }
}
</style>

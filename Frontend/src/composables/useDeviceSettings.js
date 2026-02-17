import { ref, computed } from 'vue'
import { deviceSettingsApi } from '../services/deviceSettingsApi.js'

// Singleton reactive state
const settings = ref(null)
const entities = ref([])
const loading = ref(false)
const error = ref(null)

let entitiesLoaded = false
let settingsLoaded = false

async function loadEntities() {
  if (entitiesLoaded) return
  try {
    loading.value = true
    error.value = null
    const response = await deviceSettingsApi.getEntities()
    entities.value = response.entities || []
    entitiesLoaded = true
  } catch (err) {
    console.error('Failed to load entities:', err)
    error.value = err.message
  } finally {
    loading.value = false
  }
}

async function loadSettings() {
  if (settingsLoaded) return
  try {
    loading.value = true
    error.value = null
    const response = await deviceSettingsApi.getSettings()
    settings.value = response
    settingsLoaded = true
  } catch (err) {
    console.error('Failed to load device settings:', err)
    error.value = err.message
  } finally {
    loading.value = false
  }
}

async function saveSettings(newSettings) {
  try {
    loading.value = true
    error.value = null
    await deviceSettingsApi.saveSettings(newSettings)
    settings.value = newSettings
  } catch (err) {
    console.error('Failed to save device settings:', err)
    error.value = err.message
    throw err
  } finally {
    loading.value = false
  }
}

function entitiesByDomain(domain) {
  return computed(() => entities.value.filter(e => e.domain === domain))
}

const isPowerMonitorConfigured = computed(() => {
  const rp = settings.value?.realtimePower
  if (!rp) return false
  return !!(
    rp.gridImportPowerSensorId &&
    rp.gridExportPowerSensorId &&
    rp.solarPowerSensorId &&
    rp.batteryPowerSensorId &&
    rp.housePowerSensorId
  )
})

export function useDeviceSettings() {
  return {
    settings,
    entities,
    loading,
    error,
    loadEntities,
    loadSettings,
    saveSettings,
    entitiesByDomain,
    isPowerMonitorConfigured
  }
}

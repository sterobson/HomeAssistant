import { ref, computed } from 'vue'
import { deviceSettingsApi } from '../services/deviceSettingsApi.js'

// Which control path the boiler is physically wired to. A switch at the boiler selects between
// the fingerbot and the house's own scheduler, and nothing can read it, so this is a statement of
// what someone saw at the boiler. Values match the BoilerControlPath enum in the API.
export const BOILER_CONTROL_PATHS = {
  EXTERNAL_SCHEDULER: 0,
  FINGERBOT: 1
}

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

async function reloadSettings() {
  settingsLoaded = false
  await loadSettings()
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

const boilerControlPath = computed(() => {
  const path = settings.value?.heating?.boilerControlPath
  return path === BOILER_CONTROL_PATHS.FINGERBOT
    ? BOILER_CONTROL_PATHS.FINGERBOT
    : BOILER_CONTROL_PATHS.EXTERNAL_SCHEDULER
})

// Saves only the boiler control path, keeping everything else in the device settings blob.
async function saveBoilerControlPath(path) {
  await loadSettings()

  await saveSettings({
    ...(settings.value || {}),
    heating: {
      ...(settings.value?.heating || {}),
      boilerControlPath: path
    }
  })
}

const isPowerMonitorConfigured = computed(() => {
  const rp = settings.value?.realtimePower
  if (!rp) return false
  return !!(
    rp.gridImportPowerSensorId &&
    rp.gridExportPowerSensorId &&
    rp.solarPowerSensorId &&
    rp.batteryPowerSensorId
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
    reloadSettings,
    saveSettings,
    entitiesByDomain,
    isPowerMonitorConfigured,
    boilerControlPath,
    saveBoilerControlPath,
    BOILER_CONTROL_PATHS
  }
}

import { ref, computed } from 'vue'
import { deviceSettingsApi } from '../services/deviceSettingsApi.js'
import { firstSaveMap } from '../utils/deviceRules.js'
import { occupancyFirstSaveMap } from '../utils/zoneOccupancy.js'

// Which control path the boiler is physically wired to. A switch at the boiler selects between
// the fingerbot and the boiler thermostat, and nothing can read it, so this is a statement of
// what someone saw at the boiler. Values match the BoilerControlPath enum in the API.
export const BOILER_CONTROL_PATHS = {
  BOILER_THERMOSTAT: 0,
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
    : BOILER_CONTROL_PATHS.BOILER_THERMOSTAT
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

// The per-device heating rules blob: null until someone first saves, then entity ID -> rules.
const deviceRules = computed(() => settings.value?.heating?.deviceRules ?? null)

// Saves one device's rules, keeping everything else in the device settings blob. While no rules have
// ever been saved the app is running on built-in defaults, so the first save writes every drivable
// device's current default alongside the new rules instead of wiping them.
async function saveDeviceRules(entityId, rules, allDrivableDevices) {
  await loadSettings()

  // A failed load leaves nothing to merge into, and saving then would replace the whole blob.
  if (!settings.value) {
    throw new Error('Device settings have not loaded, so the rules were not saved.')
  }

  const current = settings.value.heating?.deviceRules ?? null
  const nextRules = current === null
    ? firstSaveMap(allDrivableDevices, entityId, rules)
    : { ...current, [entityId]: rules }

  await saveSettings({
    ...settings.value,
    heating: {
      ...(settings.value.heating || {}),
      deviceRules: nextRules
    }
  })
}

// Which presence detectors count for each zone: null until someone first saves, then zone name -> detector ids.
const zoneOccupancy = computed(() => settings.value?.heating?.zoneOccupancy ?? null)

// Saves one zone's detectors, keeping everything else in the device settings blob. While nothing has been saved
// the app runs on its built-in defaults, so the first save writes every zone's current detectors too.
async function saveZoneOccupancy(zoneName, detectorIds, allZones) {
  await loadSettings()

  if (!settings.value) {
    throw new Error('Device settings have not loaded, so the occupancy was not saved.')
  }

  const current = settings.value.heating?.zoneOccupancy ?? null
  const next = current === null
    ? occupancyFirstSaveMap(allZones, zoneName, detectorIds)
    : { ...Object.fromEntries(Object.entries(current).filter(([key]) => key.trim().toLowerCase() !== zoneName.trim().toLowerCase())), [zoneName]: [...detectorIds] }

  await saveSettings({
    ...settings.value,
    heating: {
      ...(settings.value.heating || {}),
      zoneOccupancy: next
    }
  })
}

// Where battery devices outside any zone's heating are shown, by HA area name: 'room:{id}' or 'group:{id}'.
// Empty until someone saves; an area with no entry shows its devices at the top of the heating page.
const areaPlacement = computed(() => settings.value?.heating?.areaPlacement ?? {})

// Saves the whole area -> place map (the batteries dialog edits every area at once), keeping everything else.
async function saveAreaPlacement(placement) {
  await loadSettings()

  if (!settings.value) {
    throw new Error('Device settings have not loaded, so the areas were not saved.')
  }

  await saveSettings({
    ...settings.value,
    heating: {
      ...(settings.value.heating || {}),
      areaPlacement: Object.fromEntries(Object.entries(placement).filter(([, place]) => !!place))
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
    deviceRules,
    saveDeviceRules,
    zoneOccupancy,
    saveZoneOccupancy,
    areaPlacement,
    saveAreaPlacement,
    BOILER_CONTROL_PATHS
  }
}

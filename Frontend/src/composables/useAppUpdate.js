import { ref, computed } from 'vue'
import { appReleaseApi } from '../services/appReleaseApi.js'
import { getHouseId } from '../utils/cookies.js'

const runningVersion = ref(null)
const runningReportedAt = ref(null)
const latestVersion = ref(null)
const loading = ref(false)
const updateRequested = ref(false)

const status = computed(() => {
  if (!runningVersion.value || !latestVersion.value) return 'unknown'
  if (updateRequested.value) return 'update-pending'
  if (runningVersion.value === latestVersion.value) return 'up-to-date'
  return 'update-available'
})

function formatVersion(version) {
  if (!version || version.length !== 14) return version || 'Unknown'

  const year = version.substring(0, 4)
  const month = version.substring(4, 6)
  const day = version.substring(6, 8)
  const hour = version.substring(8, 10)
  const minute = version.substring(10, 12)
  const second = version.substring(12, 14)

  const date = new Date(`${year}-${month}-${day}T${hour}:${minute}:${second}Z`)
  return date.toLocaleString()
}

async function refresh() {
  if (!getHouseId()) {
    runningVersion.value = null
    latestVersion.value = null
    runningReportedAt.value = null
    updateRequested.value = false
    return
  }

  loading.value = true
  try {
    const [latestResult, runningResult] = await Promise.all([
      appReleaseApi.getLatestVersion(),
      appReleaseApi.getRunningVersion()
    ])

    latestVersion.value = latestResult.version || null
    runningVersion.value = runningResult.version || null
    runningReportedAt.value = runningResult.reportedAt || null

    // If running matches latest, clear any pending update state
    if (runningVersion.value === latestVersion.value) {
      updateRequested.value = false
    }
  } catch (error) {
    console.error('Failed to refresh app update state:', error)
  } finally {
    loading.value = false
  }
}

async function requestUpdate() {
  if (!latestVersion.value) return

  try {
    await appReleaseApi.requestUpdate(latestVersion.value)
    updateRequested.value = true
  } catch (error) {
    console.error('Failed to request update:', error)
  }
}

function handleVersionReport(data) {
  if (data?.version) {
    runningVersion.value = data.version
    runningReportedAt.value = new Date().toISOString()

    if (data.version === latestVersion.value) {
      updateRequested.value = false
    }
  }
}

export function useAppUpdate() {
  return {
    runningVersion,
    runningReportedAt,
    latestVersion,
    loading,
    status,
    updateRequested,
    formatVersion,
    refresh,
    requestUpdate,
    handleVersionReport
  }
}

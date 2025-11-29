<template>
  <div class="app">
    <header class="app-header">
      <h1>Heating Control</h1>
      <SettingsMenu
        @show-house-id-modal="showHouseIdModal = true"
        @disconnect="handleDisconnect"
      />
    </header>
    <main class="app-main">
      <HeatingView />
    </main>

    <!-- House ID Modal -->
    <HouseIdModal
      v-model="showHouseIdModal"
      :allow-cancel="hasHouseId"
      @submit="handleHouseIdSubmit"
    />
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import HeatingView from './views/HeatingView.vue'
import SettingsMenu from './components/SettingsMenu.vue'
import HouseIdModal from './components/HouseIdModal.vue'
import { hasHouseId as checkHouseId, setHouseId, clearHouseId } from './utils/cookies.js'

const showHouseIdModal = ref(false)
const hasHouseId = ref(false)

// Check for house ID on mount
onMounted(() => {
  hasHouseId.value = checkHouseId()
  if (!hasHouseId.value) {
    // Show modal if no house ID is set
    showHouseIdModal.value = true
  }
})

// Handle house ID submission
async function handleHouseIdSubmit(houseId) {
  // Temporarily set the house ID to test it
  setHouseId(houseId)

  try {
    // Validate by attempting to fetch schedules
    const { heatingApi } = await import('./services/heatingApi.js')
    const schedulesResponse = await heatingApi.getSchedules()

    // Check if we got valid data
    if (!schedulesResponse || !schedulesResponse.rooms || schedulesResponse.rooms.length === 0) {
      // Invalid house ID - no schedules found
      throw new Error('No schedules found for this House ID')
    }

    // Valid house ID - keep it and reload
    hasHouseId.value = true
    console.log('House ID validated and set:', houseId)
    window.location.reload()
  } catch (error) {
    // Invalid house ID - clear it and show error
    clearHouseId()
    hasHouseId.value = false

    // Re-show the modal with error
    // We'll pass the error back to the modal
    showHouseIdModal.value = true

    console.error('Failed to validate house ID:', error)
    // The modal will be shown again automatically since hasHouseId is false
    throw error // Re-throw so modal can catch and display
  }
}

// Handle disconnect from house
function handleDisconnect() {
  clearHouseId()
  hasHouseId.value = false
  showHouseIdModal.value = true
  // Reload to clear all data
  window.location.reload()
}
</script>

<style>
.app {
  min-height: 100vh;
  display: flex;
  flex-direction: column;
}

.app-header {
  background-color: var(--bg-header);
  color: var(--text-header);
  padding: 1rem;
  box-shadow: 0 2px 4px var(--shadow);
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.app-header h1 {
  font-size: 1.5rem;
  font-weight: 600;
}

.app-main {
  flex: 1;
  padding: 1rem;
  max-width: 800px;
  margin: 0 auto;
  width: 100%;
}

@media (max-width: 600px) {
  .app-main {
    padding: 0.5rem;
  }

  .app-header h1 {
    font-size: 1.25rem;
  }
}
</style>

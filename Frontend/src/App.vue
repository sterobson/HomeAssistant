<template>
  <div class="app">
    <header class="app-header">
      <h1>Heating Control</h1>
      <div class="header-controls">
        <div class="occupancy-filter">
          <button
            class="filter-btn"
            :class="{ active: currentHouseState === 0 }"
            @click="setHouseState(0)"
            title="Set house to Occupied (Home)"
          >
            <svg width="24" height="24" viewBox="0 0 16 16" fill="currentColor">
              <path d="M8.707 1.5a1 1 0 0 0-1.414 0L.646 8.146a.5.5 0 0 0 .708.708L2 8.207V13.5A1.5 1.5 0 0 0 3.5 15h9a1.5 1.5 0 0 0 1.5-1.5V8.207l.646.647a.5.5 0 0 0 .708-.708L13 5.793V2.5a.5.5 0 0 0-.5-.5h-1a.5.5 0 0 0-.5.5v1.293L8.707 1.5ZM13 7.207V13.5a.5.5 0 0 1-.5.5h-9a.5.5 0 0 1-.5-.5V7.207l5-5 5 5Z"/>
            </svg>
          </button>
          <button
            class="filter-btn"
            :class="{ active: currentHouseState === 1 }"
            @click="setHouseState(1)"
            title="Set house to Vacant (Away)"
          >
            <svg width="24" height="24" viewBox="0 0 16 16" fill="currentColor">
              <path d="M8.707 1.5a1 1 0 0 0-1.414 0L.646 8.146a.5.5 0 0 0 .708.708L2 8.207V13.5A1.5 1.5 0 0 0 3.5 15h9a1.5 1.5 0 0 0 1.5-1.5V8.207l.646.647a.5.5 0 0 0 .708-.708L13 5.793V2.5a.5.5 0 0 0-.5-.5h-1a.5.5 0 0 0-.5.5v1.293L8.707 1.5ZM13 7.207V13.5a.5.5 0 0 1-.5.5h-9a.5.5 0 0 1-.5-.5V7.207l5-5 5 5Z"/>
              <path d="M1 15l14-14" stroke="currentColor" stroke-width="1.5"/>
            </svg>
          </button>
        </div>
        <SettingsMenu
          @disconnect="handleDisconnect"
        />
      </div>
    </header>
    <main class="app-main">
      <HeatingView
        :occupancy-filter="occupancyFilter"
        @house-state-changed="handleHouseStateChanged"
      />
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
const occupancyFilter = ref('occupied') // 'occupied', 'vacant', or null for all
const currentHouseState = ref(0) // 0 = Home, 1 = Away

// Cookie helpers
const OCCUPANCY_FILTER_COOKIE = 'heating-app-occupancy-filter'

const setCookie = (name, value, days = 365) => {
  const expires = new Date()
  expires.setTime(expires.getTime() + days * 24 * 60 * 60 * 1000)
  document.cookie = `${name}=${encodeURIComponent(value)};expires=${expires.toUTCString()};path=/`
}

const getCookie = (name) => {
  const nameEQ = name + "="
  const ca = document.cookie.split(';')
  for (let i = 0; i < ca.length; i++) {
    let c = ca[i]
    while (c.charAt(0) === ' ') c = c.substring(1, c.length)
    if (c.indexOf(nameEQ) === 0) return decodeURIComponent(c.substring(nameEQ.length, c.length))
  }
  return null
}

// Handle house state changes from HeatingView (when loaded from API)
function handleHouseStateChanged(newState) {
  currentHouseState.value = newState

  // Update filter to match the new house state
  if (newState === 0) {
    // Home state - show occupied schedules
    occupancyFilter.value = 'occupied'
    setCookie(OCCUPANCY_FILTER_COOKIE, 'occupied')
  } else if (newState === 1) {
    // Away state - show vacant schedules
    occupancyFilter.value = 'vacant'
    setCookie(OCCUPANCY_FILTER_COOKIE, 'vacant')
  }
}

// Set house state when user clicks the icons
function setHouseState(state) {
  // Don't do anything if clicking the already active state
  if (currentHouseState.value === state) {
    return
  }

  // Update local state
  currentHouseState.value = state

  // Update filter to match
  if (state === 0) {
    occupancyFilter.value = 'occupied'
    setCookie(OCCUPANCY_FILTER_COOKIE, 'occupied')
  } else if (state === 1) {
    occupancyFilter.value = 'vacant'
    setCookie(OCCUPANCY_FILTER_COOKIE, 'vacant')
  }
}

// Check for house ID on mount
onMounted(() => {
  hasHouseId.value = checkHouseId()
  if (!hasHouseId.value) {
    // Show modal if no house ID is set
    showHouseIdModal.value = true
  }

  // Load occupancy filter from cookie
  const savedFilter = getCookie(OCCUPANCY_FILTER_COOKIE)
  if (savedFilter === 'occupied' || savedFilter === 'vacant') {
    occupancyFilter.value = savedFilter

    // Initialize house state to match the filter
    // This will be overwritten when actual state loads from API
    if (savedFilter === 'occupied') {
      currentHouseState.value = 0 // Home
    } else if (savedFilter === 'vacant') {
      currentHouseState.value = 1 // Away
    }
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

.header-controls {
  display: flex;
  align-items: center;
  gap: 1rem;
}

.occupancy-filter {
  display: flex;
  gap: 0.5rem;
  align-items: center;
}

.filter-btn {
  background: none;
  border: 2px solid transparent;
  color: var(--text-header);
  cursor: pointer;
  padding: 0.5rem;
  border-radius: 6px;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.2s;
  opacity: 0.6;
}

.filter-btn:hover {
  opacity: 1;
  background-color: rgba(255, 255, 255, 0.1);
}

.filter-btn.active {
  opacity: 1;
  border-color: var(--color-primary);
  background-color: rgba(52, 152, 219, 0.2);
}

.filter-btn:active {
  transform: scale(0.95);
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

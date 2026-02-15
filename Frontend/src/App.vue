<template>
  <div class="app">
    <header class="app-header">
      <div class="header-left">
        <router-link
          v-if="route.name !== 'home'"
          to="/"
          class="home-btn"
          title="Home"
        >
          <svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor">
            <path fill-rule="evenodd" d="M15 8a.5.5 0 0 0-.5-.5H2.707l3.147-3.146a.5.5 0 1 0-.708-.708l-4 4a.5.5 0 0 0 0 .708l4 4a.5.5 0 0 0 .708-.708L2.707 8.5H14.5A.5.5 0 0 0 15 8z"/>
          </svg>
        </router-link>
        <h1>{{ pageTitle }}</h1>
      </div>
      <div class="header-controls">
        <div v-if="route.name === 'heating'" class="occupancy-filter">
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
              <path d="M6.428 1.151C6.708.591 7.213 0 8 0s1.292.592 1.572 1.151C9.861 1.73 10 2.431 10 3v3.691l5.17 2.585a1.5 1.5 0 0 1 .83 1.342V12a.5.5 0 0 1-.582.493l-5.507-.918-.375 2.253 1.318 1.318A.5.5 0 0 1 10.5 16h-5a.5.5 0 0 1-.354-.854l1.319-1.318-.376-2.253-5.507.918A.5.5 0 0 1 0 12v-1.382a1.5 1.5 0 0 1 .83-1.342L6 6.691V3c0-.568.14-1.271.428-1.849Zm.894.448C7.111 2.02 7 2.569 7 3v4a.5.5 0 0 1-.276.447l-5.448 2.724a.5.5 0 0 0-.276.447v.792l5.418-.903a.5.5 0 0 1 .575.41l.5 3a.5.5 0 0 1-.14.437L6.708 15h2.586l-.647-.646a.5.5 0 0 1-.14-.436l.5-3a.5.5 0 0 1 .576-.411L15 11.41v-.792a.5.5 0 0 0-.276-.447L9.276 7.447A.5.5 0 0 1 9 7V3c0-.432-.11-.979-.322-1.401C8.458 1.159 8.213 1 8 1c-.213 0-.458.158-.678.599Z"/>
            </svg>
          </button>
        </div>
        <SettingsMenu
          @disconnect="handleDisconnect"
          @open-entity-settings="handleOpenEntitySettings"
        />
      </div>
    </header>
    <div v-if="apiErrors.length > 0" class="error-bar">
      <div v-for="error in apiErrors" :key="error.id" class="error-bar-item">
        <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" class="error-bar-icon">
          <path d="M8.982 1.566a1.13 1.13 0 0 0-1.96 0L.165 13.233c-.457.778.091 1.767.98 1.767h13.713c.889 0 1.438-.99.98-1.767L8.982 1.566zM8 5c.535 0 .954.462.9.995l-.35 3.507a.552.552 0 0 1-1.1 0L7.1 5.995A.905.905 0 0 1 8 5zm.002 6a1 1 0 1 1 0 2 1 1 0 0 1 0-2z"/>
        </svg>
        <span>{{ error.message }}</span>
        <button class="error-bar-dismiss" @click="dismissApiError(error.id)" title="Dismiss">&times;</button>
      </div>
    </div>

    <main class="app-main" :class="{ 'app-main--full-width': route.name === 'battery' }">
      <router-view
        :occupancy-filter="occupancyFilter"
        :show-entity-settings="showEntitySettings"
        @house-state-changed="handleHouseStateChanged"
        @entity-settings-closed="showEntitySettings = false"
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
import { ref, computed, onMounted, watchEffect } from 'vue'
import { useRoute } from 'vue-router'
import SettingsMenu from './components/SettingsMenu.vue'
import HouseIdModal from './components/HouseIdModal.vue'
import { hasHouseId as checkHouseId, setHouseId, clearHouseId } from './utils/cookies.js'
import { useApiErrors } from './composables/useApiErrors.js'

const route = useRoute()
const { errors: apiErrors, dismissError: dismissApiError } = useApiErrors()

const showHouseIdModal = ref(false)
const hasHouseId = ref(false)
const occupancyFilter = ref('occupied') // 'occupied', 'vacant', or null for all
const currentHouseState = ref(0) // 0 = Home, 1 = Away
const showEntitySettings = ref(false)

const pageTitle = computed(() => {
  switch (route.name) {
    case 'heating': return 'Heating Control'
    case 'battery': return 'Battery Management'
    default: return 'Home Assistant'
  }
})

// Dynamic favicon based on current route
watchEffect(() => {
  const link = document.querySelector("link[rel~='icon']")
  if (!link) return
  switch (route.name) {
    case 'heating': link.href = '/favicon-heating.svg'; break
    case 'battery': link.href = '/favicon-battery.svg'; break
    default: link.href = '/favicon.svg'; break
  }
})

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

    // Delay before showing the error to prevent enumeration attacks
    await new Promise(resolve => setTimeout(resolve, 1500))

    // Re-show the modal with error
    showHouseIdModal.value = true

    console.error('Failed to validate house ID:', error)

    // Provide a user-friendly message for 404s
    const message = error.message?.includes('Not Found')
      ? 'House not found. Please check your House ID and try again.'
      : error.message || 'Unable to connect. Please try again.'
    throw new Error(message)
  }
}

// Handle entity settings request from hamburger menu
function handleOpenEntitySettings() {
  showEntitySettings.value = true
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

.header-left {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.home-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--text-header);
  padding: 0.375rem;
  border-radius: 6px;
  transition: all 0.2s;
  text-decoration: none;
  opacity: 0.8;
}

.home-btn:hover {
  opacity: 1;
  background-color: rgba(255, 255, 255, 0.1);
}

.home-btn:active {
  transform: scale(0.95);
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

.app-main--full-width {
  max-width: none;
  padding: 0.5rem;
}

.error-bar {
  display: flex;
  flex-direction: column;
  gap: 0;
}

.error-bar-item {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.5rem 1rem;
  background: var(--color-danger, #e74c3c);
  color: white;
  font-size: 0.85rem;
  font-weight: 500;
  animation: slideDown 0.25s;
}

.error-bar-icon {
  flex-shrink: 0;
  opacity: 0.9;
}

.error-bar-item span {
  flex: 1;
}

.error-bar-dismiss {
  background: none;
  border: none;
  color: white;
  font-size: 1.2rem;
  cursor: pointer;
  padding: 0 0.25rem;
  opacity: 0.7;
  transition: opacity 0.15s;
  line-height: 1;
}

.error-bar-dismiss:hover {
  opacity: 1;
}

@keyframes slideDown {
  from { opacity: 0; transform: translateY(-100%); }
  to { opacity: 1; transform: translateY(0); }
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

<template>
  <div class="settings-menu">
    <!-- Hamburger Button -->
    <button class="hamburger-btn" @click="toggleMenu" :aria-label="isOpen ? 'Close menu' : 'Open menu'">
      <svg width="24" height="24" viewBox="0 0 16 16" fill="currentColor">
        <path v-if="!isOpen" d="M0 3h16v2H0V3zm0 4h16v2H0V7zm0 4h16v2H0v-2z"/>
        <path v-else d="M2.146 2.854a.5.5 0 11.708-.708L8 7.293l5.146-5.147a.5.5 0 01.708.708L8.707 8l5.147 5.146a.5.5 0 01-.708.708L8 8.707l-5.146 5.147a.5.5 0 01-.708-.708L7.293 8 2.146 2.854z"/>
      </svg>
    </button>

    <!-- Menu Overlay -->
    <transition name="fade">
      <div v-if="isOpen" class="menu-overlay" @click="closeMenu"></div>
    </transition>

    <!-- Menu Panel -->
    <transition name="slide">
      <div v-if="isOpen" class="menu-panel">
        <div class="menu-header">
          <h2>Settings</h2>
          <button class="close-btn" @click="closeMenu">
            <svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor">
              <path d="M2.146 2.854a.5.5 0 11.708-.708L8 7.293l5.146-5.147a.5.5 0 01.708.708L8.707 8l5.147 5.146a.5.5 0 01-.708.708L8 8.707l-5.146 5.147a.5.5 0 01-.708-.708L7.293 8 2.146 2.854z"/>
            </svg>
          </button>
        </div>

        <div class="menu-content">
          <!-- Theme Setting -->
          <div class="setting-group">
            <button class="accordion-header" :class="{ expanded: expandedSection === 'theme' }" @click="toggleSection('theme')">
              <span>Theme</span>
              <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" class="chevron">
                <path d="M4 6l4 4 4-4H4z"/>
              </svg>
            </button>
            <transition name="accordion">
              <div v-show="expandedSection === 'theme'" class="accordion-content">
                <div class="theme-section">
                  <label class="theme-label">Appearance</label>
                  <div class="mode-grid">
                    <button
                      v-for="mode in modeOptions"
                      :key="mode.value"
                      class="setting-btn"
                      :class="{ active: currentMode === mode.value }"
                      @click="setMode(mode.value)"
                    >
                      <component :is="mode.icon" />
                      <span>{{ mode.label }}</span>
                    </button>
                  </div>
                </div>
                <div class="theme-section">
                  <label class="theme-label">Color</label>
                  <div class="color-grid">
                    <button
                      v-for="color in colorOptions"
                      :key="color.value"
                      class="color-btn"
                      :class="{ active: currentColor === color.value }"
                      @click="setColor(color.value)"
                      :title="color.label"
                    >
                      <div class="color-circle" :style="{ backgroundColor: color.color }"></div>
                    </button>
                  </div>
                </div>
              </div>
            </transition>
          </div>

          <!-- Temperature Unit Setting -->
          <div class="setting-group">
            <button class="accordion-header" :class="{ expanded: expandedSection === 'temperature' }" @click="toggleSection('temperature')">
              <span>Temperature Unit</span>
              <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" class="chevron">
                <path d="M4 6l4 4 4-4H4z"/>
              </svg>
            </button>
            <transition name="accordion">
              <div v-show="expandedSection === 'temperature'" class="accordion-content">
                <div class="setting-options">
                  <button
                    class="setting-btn"
                    :class="{ active: settings.temperatureUnit === TEMP_UNITS.CELSIUS }"
                    @click="setTemperatureUnit(TEMP_UNITS.CELSIUS)"
                  >
                    <span class="unit-icon">°C</span>
                    <span>Celsius</span>
                  </button>
                  <button
                    class="setting-btn"
                    :class="{ active: settings.temperatureUnit === TEMP_UNITS.FAHRENHEIT }"
                    @click="setTemperatureUnit(TEMP_UNITS.FAHRENHEIT)"
                  >
                    <span class="unit-icon">°F</span>
                    <span>Fahrenheit</span>
                  </button>
                </div>
              </div>
            </transition>
          </div>

          <!-- Time Format Setting -->
          <div class="setting-group">
            <button class="accordion-header" :class="{ expanded: expandedSection === 'time' }" @click="toggleSection('time')">
              <span>Time Format</span>
              <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" class="chevron">
                <path d="M4 6l4 4 4-4H4z"/>
              </svg>
            </button>
            <transition name="accordion">
              <div v-show="expandedSection === 'time'" class="accordion-content">
                <div class="setting-options">
                  <button
                    class="setting-btn"
                    :class="{ active: settings.timeFormat === TIME_FORMATS.HOUR_24 }"
                    @click="setTimeFormat(TIME_FORMATS.HOUR_24)"
                  >
                    <span class="time-icon">24</span>
                    <span>24-hour</span>
                  </button>
                  <button
                    class="setting-btn"
                    :class="{ active: settings.timeFormat === TIME_FORMATS.HOUR_12 }"
                    @click="setTimeFormat(TIME_FORMATS.HOUR_12)"
                  >
                    <span class="time-icon">12</span>
                    <span>12-hour (AM/PM)</span>
                  </button>
                </div>
              </div>
            </transition>
          </div>

          <!-- House Details -->
          <div class="setting-group">
            <button class="accordion-header" :class="{ expanded: expandedSection === 'house' }" @click="toggleSection('house')">
              <span>House Details</span>
              <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" class="chevron">
                <path d="M4 6l4 4 4-4H4z"/>
              </svg>
            </button>
            <transition name="accordion">
              <div v-show="expandedSection === 'house'" class="accordion-content">
                <div class="house-details">
                  <div class="detail-row">
                    <label>House Name</label>
                    <div class="detail-value">{{ houseName || 'Not set' }}</div>
                  </div>
                  <div class="detail-row">
                    <label>House ID</label>
                    <div class="detail-value house-id-display">{{ houseId || 'Not set' }}</div>
                  </div>
                  <button class="disconnect-button" @click="handleDisconnectClick">
                    <svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor">
                      <path d="M6 12.5a.5.5 0 01.5-.5h3a.5.5 0 010 1h-3a.5.5 0 01-.5-.5zM3 8.062C3 6.76 4.235 5.765 5.53 5.886a26.58 26.58 0 004.94 0C11.765 5.765 13 6.76 13 8.062v1.157a.933.933 0 01-.765.935c-.845.147-2.34.346-4.235.346-1.895 0-3.39-.2-4.235-.346A.933.933 0 013 9.219V8.062zm4.542-.827a.25.25 0 00-.217.068l-.92.9a.25.25 0 00-.073.177V13a.5.5 0 00.5.5h.128a.5.5 0 00.5-.485l.048-2.515h.144l.048 2.515a.5.5 0 00.5.485h.128a.5.5 0 00.5-.5V8.38a.25.25 0 00-.073-.177l-.92-.9a.25.25 0 00-.217-.068h-.048zM6.5 4.5a.5.5 0 01.5.5v.354a12.42 12.42 0 002 0V5a.5.5 0 011 0v.354a1.5 1.5 0 01-.436 1.06c-.318.32-.75.544-1.216.63a12.07 12.07 0 01-3.696 0 2.486 2.486 0 01-1.216-.63A1.5 1.5 0 013 5.354V5a.5.5 0 011 0v.354a12.42 12.42 0 002 0V5a.5.5 0 01.5-.5z"/>
                    </svg>
                    <span>Disconnect from House</span>
                  </button>
                </div>
              </div>
            </transition>
          </div>
        </div>
      </div>
    </transition>

    <!-- Disconnect Confirmation Modal -->
    <ConfirmModal
      v-if="showDisconnectConfirm"
      title="Disconnect from House?"
      message="Are you sure you want to disconnect from this house? All current data will be cleared and you'll need to enter a House ID again."
      confirm-text="Disconnect"
      cancel-text="Cancel"
      @confirm="handleDisconnectConfirm"
      @cancel="showDisconnectConfirm = false"
    />
  </div>
</template>

<script setup>
import { ref, h, onMounted, watch, computed } from 'vue'
import { useSettings } from '../composables/useSettings.js'
import ConfirmModal from './ConfirmModal.vue'
import { getHouseId } from '../utils/cookies.js'
import { heatingApi } from '../services/heatingApi.js'

const { settings, setTheme, setTemperatureUnit, setTimeFormat, THEMES, TEMP_UNITS, TIME_FORMATS } = useSettings()

const emit = defineEmits(['disconnect'])

const isOpen = ref(false)
const expandedSection = ref(null)
const showDisconnectConfirm = ref(false)
const houseId = ref('')
const houseName = ref('')

// Load house details when menu opens
const loadHouseDetails = async () => {
  houseId.value = getHouseId() || ''

  if (houseId.value) {
    try {
      const details = await heatingApi.getHouseDetails()
      houseName.value = details.name || ''
    } catch (error) {
      console.error('Failed to load house details:', error)
      houseName.value = ''
    }
  }
}

// Load house details when component mounts
onMounted(() => {
  loadHouseDetails()
})

// Reload house details when menu opens
watch(isOpen, (newValue) => {
  if (newValue) {
    loadHouseDetails()
  }
})

const toggleMenu = () => {
  isOpen.value = !isOpen.value
  // Always default to all sections collapsed when opening
  if (isOpen.value) {
    expandedSection.value = null
  }
}

const closeMenu = () => {
  isOpen.value = false
}

const toggleSection = (section) => {
  if (expandedSection.value === section) {
    expandedSection.value = null
  } else {
    expandedSection.value = section
  }
}

const handleDisconnectClick = () => {
  closeMenu()
  showDisconnectConfirm.value = true
}

const handleDisconnectConfirm = () => {
  showDisconnectConfirm.value = false
  emit('disconnect')
}

// Theme icon components
const SystemIcon = () => h('svg', { width: 20, height: 20, viewBox: '0 0 16 16', fill: 'currentColor' }, [
  h('path', { d: 'M0 2a2 2 0 012-2h12a2 2 0 012 2v12a2 2 0 01-2 2H2a2 2 0 01-2-2V2zm2-1a1 1 0 00-1 1v12a1 1 0 001 1h12a1 1 0 001-1V2a1 1 0 00-1-1H2z' }),
  h('path', { d: 'M2 3h12v10H2V3z' })
])

const LightIcon = () => h('svg', { width: 20, height: 20, viewBox: '0 0 16 16', fill: 'currentColor' }, [
  h('path', { d: 'M8 11a3 3 0 110-6 3 3 0 010 6zm0 1a4 4 0 100-8 4 4 0 000 8zM8 0a.5.5 0 01.5.5v2a.5.5 0 01-1 0v-2A.5.5 0 018 0zm0 13a.5.5 0 01.5.5v2a.5.5 0 01-1 0v-2A.5.5 0 018 13zm8-5a.5.5 0 01-.5.5h-2a.5.5 0 010-1h2a.5.5 0 01.5.5zM3 8a.5.5 0 01-.5.5h-2a.5.5 0 010-1h2A.5.5 0 013 8zm10.657-5.657a.5.5 0 010 .707l-1.414 1.415a.5.5 0 11-.707-.708l1.414-1.414a.5.5 0 01.707 0zm-9.193 9.193a.5.5 0 010 .707L3.05 13.657a.5.5 0 01-.707-.707l1.414-1.414a.5.5 0 01.707 0zm9.193 2.121a.5.5 0 01-.707 0l-1.414-1.414a.5.5 0 01.707-.707l1.414 1.414a.5.5 0 010 .707zM4.464 4.465a.5.5 0 01-.707 0L2.343 3.05a.5.5 0 11.707-.707l1.414 1.414a.5.5 0 010 .708z' })
])

const DarkIcon = () => h('svg', { width: 20, height: 20, viewBox: '0 0 16 16', fill: 'currentColor' }, [
  h('path', { d: 'M6 .278a.768.768 0 01.08.858 7.208 7.208 0 00-.878 3.46c0 4.021 3.278 7.277 7.318 7.277.527 0 1.04-.055 1.533-.16a.787.787 0 01.81.316.733.733 0 01-.031.893A8.349 8.349 0 018.344 16C3.734 16 0 12.286 0 7.71 0 4.266 2.114 1.312 5.124.06A.752.752 0 016 .278z' })
])

// Mode options
const modeOptions = [
  { value: 'system', label: 'System', icon: SystemIcon },
  { value: 'light', label: 'Light', icon: LightIcon },
  { value: 'dark', label: 'Dark', icon: DarkIcon }
]

// Color options with light and dark variants
const colorDefinitions = {
  green: { light: '#43a047', dark: '#2e5d2e' },
  blue: { light: '#42a5f5', dark: '#42a5f5' },
  purple: { light: '#8e24aa', dark: '#5d3a6e' },
  pink: { light: '#d81b60', dark: '#6e3a52' },
  gray: { light: '#757575', dark: '#424242' },
  red: { light: '#e53935', dark: '#6e2e2e' },
  orange: { light: '#fb8c00', dark: '#6e4a2e' },
  yellow: { light: '#fdd835', dark: '#6e5d2e' },
  brown: { light: '#8d6e63', dark: '#4e3a33' }
}

// Parse current theme to extract mode and color
const currentMode = computed(() => {
  const theme = settings.value.theme
  if (theme === THEMES.SYSTEM || theme === THEMES.LIGHT || theme === THEMES.DARK) {
    return theme
  }
  // Extract mode from compound theme (e.g., "light-green" -> "light")
  if (theme.startsWith('light-')) return 'light'
  if (theme.startsWith('dark-')) return 'dark'
  return 'system'
})

const currentColor = computed(() => {
  const theme = settings.value.theme
  // If it's just system/light/dark, no color is selected (null means use base theme)
  if (theme === THEMES.SYSTEM || theme === THEMES.LIGHT || theme === THEMES.DARK) {
    return null
  }
  // Extract color from compound theme (e.g., "light-green" -> "green")
  const parts = theme.split('-')
  if (parts.length > 1) {
    return parts.slice(1).join('-') // Handle multi-word colors if needed
  }
  return null
})

// Determine which color variant to show based on current mode
const getColorVariant = computed(() => {
  const mode = currentMode.value
  if (mode === 'system') {
    // Use system preference
    const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches
    return prefersDark ? 'dark' : 'light'
  }
  return mode === 'dark' ? 'dark' : 'light'
})

// Color options to display (with appropriate light/dark variant)
const colorOptions = computed(() => {
  const variant = getColorVariant.value
  return [
    { value: 'green', label: 'Green', color: colorDefinitions.green[variant] },
    { value: 'blue', label: 'Blue', color: colorDefinitions.blue[variant] },
    { value: 'purple', label: 'Purple', color: colorDefinitions.purple[variant] },
    { value: 'pink', label: 'Pink', color: colorDefinitions.pink[variant] },
    { value: 'gray', label: 'Gray', color: colorDefinitions.gray[variant] },
    { value: 'red', label: 'Red', color: colorDefinitions.red[variant] },
    { value: 'orange', label: 'Orange', color: colorDefinitions.orange[variant] },
    { value: 'yellow', label: 'Yellow', color: colorDefinitions.yellow[variant] },
    { value: 'brown', label: 'Brown', color: colorDefinitions.brown[variant] }
  ]
})

// Set mode
const setMode = (mode) => {
  const color = currentColor.value
  if (!color) {
    // No color selected - just set the base mode
    setTheme(mode)
  } else {
    // Combine mode and color
    if (mode === 'system') {
      // For system mode, use the system preference to determine which variant
      const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches
      setTheme(`${prefersDark ? 'dark' : 'light'}-${color}`)
    } else {
      setTheme(`${mode}-${color}`)
    }
  }
}

// Set color
const setColor = (color) => {
  const mode = currentMode.value
  // Always combine mode and color
  if (mode === 'system') {
    // For system mode, use the system preference to determine which variant
    const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches
    setTheme(`${prefersDark ? 'dark' : 'light'}-${color}`)
  } else {
    setTheme(`${mode}-${color}`)
  }
}

// Close menu on escape key
const handleEscape = (event) => {
  if (event.key === 'Escape' && isOpen.value) {
    closeMenu()
  }
}

// Add event listener for escape key only
if (typeof window !== 'undefined') {
  window.addEventListener('keydown', handleEscape)
}
</script>

<style scoped>
.settings-menu {
  position: relative;
}

.hamburger-btn {
  background: none;
  border: none;
  color: var(--text-header);
  cursor: pointer;
  padding: 0.5rem;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 6px;
  transition: all 0.2s;
}

.hamburger-btn:hover {
  background-color: rgba(255, 255, 255, 0.1);
}

.hamburger-btn:active {
  transform: scale(0.95);
}

.menu-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background-color: var(--overlay);
  z-index: 1000;
}

.menu-panel {
  position: fixed;
  top: 0;
  right: 0;
  bottom: 0;
  width: 100%;
  max-width: 350px;
  background: var(--bg-secondary);
  box-shadow: -4px 0 20px var(--shadow-md);
  z-index: 1001;
  display: flex;
  flex-direction: column;
  overflow-y: auto;
}

.menu-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1.5rem;
  border-bottom: 1px solid var(--border-color);
  background: var(--bg-header);
  color: var(--text-header);
}

.menu-header h2 {
  font-size: 1.25rem;
  font-weight: 600;
  margin: 0;
}

.close-btn {
  background: none;
  border: none;
  color: var(--text-header);
  cursor: pointer;
  padding: 0.5rem;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 4px;
  transition: all 0.2s;
}

.close-btn:hover {
  background-color: rgba(255, 255, 255, 0.1);
}

.menu-content {
  flex: 1;
  padding: 1.5rem;
  overflow-y: auto;
}

.setting-group {
  border-bottom: 1px solid var(--border-color);
}

.setting-group:last-child {
  border-bottom: none;
}

.accordion-header {
  width: 100%;
  background: none;
  border: none;
  padding: 1rem 0;
  display: flex;
  align-items: center;
  justify-content: space-between;
  cursor: pointer;
  font-size: 1rem;
  font-weight: 600;
  color: var(--text-primary);
  transition: all 0.2s;
}

.accordion-header:hover {
  color: var(--color-primary);
}

.accordion-header .chevron {
  transition: transform 0.3s;
  color: var(--icon-color);
}

.accordion-header.expanded .chevron {
  transform: rotate(180deg);
}

.accordion-content {
  padding-bottom: 1rem;
}

.setting-options {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 0.75rem;
}

.theme-section {
  margin-bottom: 1.5rem;
}

.theme-section:last-child {
  margin-bottom: 0;
}

.theme-label {
  display: block;
  margin-bottom: 0.75rem;
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.mode-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 0.75rem;
}

.color-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 0.75rem;
}

.setting-btn {
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  color: var(--text-primary);
  padding: 1rem;
  border-radius: 8px;
  cursor: pointer;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.9rem;
  font-weight: 500;
  transition: all 0.2s;
}

.setting-btn:hover {
  border-color: var(--color-primary);
  background-color: var(--hover-bg);
}

.setting-btn.active {
  background-color: var(--color-primary);
  border-color: var(--color-primary);
  color: white;
}

.setting-btn:active {
  transform: scale(0.97);
}

.setting-btn.full-width {
  grid-column: 1 / -1;
}

.color-btn {
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  padding: 0.75rem;
  border-radius: 8px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.2s;
  min-height: 60px;
}

.color-btn:hover {
  border-color: var(--color-primary);
  background-color: var(--hover-bg);
}

.color-btn.active {
  border-color: var(--color-primary);
  background-color: var(--hover-bg);
  box-shadow: 0 0 0 2px var(--color-primary);
}

.color-btn:active {
  transform: scale(0.97);
}

.color-circle {
  width: 32px;
  height: 32px;
  border-radius: 50%;
  border: 2px solid rgba(255, 255, 255, 0.3);
  box-shadow: 0 2px 4px rgba(0, 0, 0, 0.2);
}

.unit-icon,
.time-icon {
  font-size: 1.5rem;
  font-weight: 700;
}

.action-button {
  width: 100%;
  background: none;
  border: none;
  padding: 1rem 0;
  display: flex;
  align-items: center;
  gap: 0.75rem;
  cursor: pointer;
  font-size: 1rem;
  color: var(--text-primary);
  transition: all 0.2s;
}

.action-button:hover {
  color: var(--color-primary);
}

.action-button .arrow {
  margin-left: auto;
  color: var(--icon-color);
}

.action-button.danger {
  color: var(--color-danger, #e74c3c);
}

.action-button.danger:hover {
  color: var(--color-danger-hover, #c0392b);
}

.house-details {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.detail-row {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.detail-row label {
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.detail-value {
  font-size: 1rem;
  color: var(--text-primary);
  padding: 0.75rem;
  background: var(--bg-tertiary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
}

.house-id-display {
  font-family: 'Courier New', monospace;
  word-break: break-all;
}

.disconnect-button {
  width: 100%;
  margin-top: 1rem;
  padding: 0.75rem 1rem;
  background: var(--color-danger, #e74c3c);
  color: white;
  border: none;
  border-radius: 8px;
  cursor: pointer;
  font-size: 1rem;
  font-weight: 500;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.5rem;
  transition: all 0.2s;
}

.disconnect-button:hover {
  background: var(--color-danger-hover, #c0392b);
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(231, 76, 60, 0.3);
}

.disconnect-button:active {
  transform: translateY(0);
}

/* Transitions */
.fade-enter-active, .fade-leave-active {
  transition: opacity 0.3s;
}

.fade-enter-from, .fade-leave-to {
  opacity: 0;
}

.slide-enter-active, .slide-leave-active {
  transition: transform 0.3s;
}

.slide-enter-from, .slide-leave-to {
  transform: translateX(100%);
}

.accordion-enter-active, .accordion-leave-active {
  transition: all 0.3s ease;
  max-height: 500px;
  overflow: hidden;
}

.accordion-enter-from, .accordion-leave-to {
  max-height: 0;
  opacity: 0;
}

@media (max-width: 600px) {
  .menu-panel {
    max-width: 100%;
  }

  .setting-options {
    grid-template-columns: 1fr;
  }
}
</style>

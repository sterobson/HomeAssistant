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
          <!-- Entity Settings (Battery page only) -->
          <div v-if="isBatteryPage" class="setting-group">
            <button class="action-button" @click="handleEntitySettingsClick">
              <svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor">
                <path fill-rule="evenodd" d="M11.49 3.17c-.38-1.56-2.6-1.56-2.98 0a1.532 1.532 0 01-2.286.948c-1.372-.836-2.942.734-2.106 2.106.54.886.061 2.042-.947 2.287-1.561.379-1.561 2.6 0 2.978a1.532 1.532 0 01.947 2.287c-.836 1.372.734 2.942 2.106 2.106a1.532 1.532 0 012.287.947c.379 1.561 2.6 1.561 2.978 0a1.533 1.533 0 012.287-.947c1.372.836 2.942-.734 2.106-2.106a1.533 1.533 0 01.947-2.287c1.561-.379 1.561-2.6 0-2.978a1.532 1.532 0 01-.947-2.287c.836-1.372-.734-2.942-2.106-2.106a1.532 1.532 0 01-2.287-.947zM10 13a3 3 0 100-6 3 3 0 000 6z" clip-rule="evenodd"/>
              </svg>
              <span>Entity settings</span>
              <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" class="arrow">
                <path d="M6 12.796V3.204L11.481 8 6 12.796zm.659.753l5.48-4.796a1 1 0 000-1.506L6.66 2.451C6.011 1.885 5 2.345 5 3.204v9.592a1 1 0 001.659.753z"/>
              </svg>
            </button>
          </div>

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

          <!-- Temperature Unit Setting (Heating page only) -->
          <div v-if="route.name === 'heating'" class="setting-group">
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

          <!-- Houses -->
          <div class="setting-group">
            <button class="accordion-header" :class="{ expanded: expandedSection === 'house' }" @click="toggleSection('house')">
              <span>Houses</span>
              <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" class="chevron">
                <path d="M4 6l4 4 4-4H4z"/>
              </svg>
            </button>
            <transition name="accordion">
              <div v-show="expandedSection === 'house'" class="accordion-content">
                <div class="house-list">
                  <div
                    v-for="house in savedHouses"
                    :key="house.id"
                    class="house-item-wrapper"
                  >
                    <!-- Inline rename editor -->
                    <div v-if="editingHouseId === house.id" class="house-rename-row">
                      <input
                        ref="renameInput"
                        v-model="editingHouseName"
                        class="house-rename-input"
                        placeholder="House name"
                        @keyup.enter="saveHouseName"
                        @keyup.escape="cancelRename"
                      />
                      <button class="house-rename-btn save" @click="saveHouseName" title="Save" :disabled="isSavingName">
                        <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor">
                          <path d="M13.854 3.646a.5.5 0 010 .708l-7 7a.5.5 0 01-.708 0l-3.5-3.5a.5.5 0 11.708-.708L6.5 10.293l6.646-6.647a.5.5 0 01.708 0z"/>
                        </svg>
                      </button>
                      <button class="house-rename-btn cancel" @click="cancelRename" title="Cancel">
                        <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor">
                          <path d="M4.646 4.646a.5.5 0 01.708 0L8 7.293l2.646-2.647a.5.5 0 01.708.708L8.707 8l2.647 2.646a.5.5 0 01-.708.708L8 8.707l-2.646 2.647a.5.5 0 01-.708-.708L7.293 8 4.646 5.354a.5.5 0 010-.708z"/>
                        </svg>
                      </button>
                    </div>
                    <!-- Normal house item -->
                    <button
                      v-else
                      class="house-item"
                      :class="{ active: house.id === houseId }"
                      @click="house.id !== houseId && handleSwitchHouse(house.id)"
                    >
                      <div class="house-item-info">
                        <span class="house-item-name">{{ house.name || house.id }}</span>
                        <span v-if="house.id === houseId" class="house-active-badge">Active</span>
                      </div>
                      <button
                        v-if="house.id === houseId"
                        class="house-edit-btn"
                        title="Rename house"
                        @click.stop="startRename(house)"
                      >
                        <svg width="14" height="14" viewBox="0 0 16 16" fill="currentColor">
                          <path d="M12.146.146a.5.5 0 01.708 0l3 3a.5.5 0 010 .708l-10 10a.5.5 0 01-.168.11l-5 2a.5.5 0 01-.65-.65l2-5a.5.5 0 01.11-.168l10-10zM11.207 2.5L13.5 4.793 14.793 3.5 12.5 1.207 11.207 2.5zm1.586 3L10.5 3.207 4 9.707V10h.5a.5.5 0 01.5.5v.5h.5a.5.5 0 01.5.5v.5h.293l6.5-6.5zm-9.761 5.175l-.106.106-1.528 3.821 3.821-1.528.106-.106A.5.5 0 015 12.5V12h-.5a.5.5 0 01-.5-.5V11h-.5a.5.5 0 01-.468-.325z"/>
                        </svg>
                      </button>
                      <svg v-if="house.id !== houseId" width="16" height="16" viewBox="0 0 16 16" fill="currentColor" class="house-item-arrow">
                        <path d="M6 12.796V3.204L11.481 8 6 12.796zm.659.753l5.48-4.796a1 1 0 000-1.506L6.66 2.451C6.011 1.885 5 2.345 5 3.204v9.592a1 1 0 001.659.753z"/>
                      </svg>
                    </button>
                  </div>
                </div>
                <div class="house-actions">
                  <button class="add-house-button" @click="handleAddHouse">
                    <svg width="18" height="18" viewBox="0 0 16 16" fill="currentColor">
                      <path d="M8 4a.5.5 0 01.5.5v3h3a.5.5 0 010 1h-3v3a.5.5 0 01-1 0v-3h-3a.5.5 0 010-1h3v-3A.5.5 0 018 4z"/>
                    </svg>
                    <span>Add house</span>
                  </button>
                  <button class="disconnect-button" @click="handleDisconnectClick">
                    <svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor">
                      <path d="M6 12.5a.5.5 0 01.5-.5h3a.5.5 0 010 1h-3a.5.5 0 01-.5-.5zM3 8.062C3 6.76 4.235 5.765 5.53 5.886a26.58 26.58 0 004.94 0C11.765 5.765 13 6.76 13 8.062v1.157a.933.933 0 01-.765.935c-.845.147-2.34.346-4.235.346-1.895 0-3.39-.2-4.235-.346A.933.933 0 013 9.219V8.062zm4.542-.827a.25.25 0 00-.217.068l-.92.9a.25.25 0 00-.073.177V13a.5.5 0 00.5.5h.128a.5.5 0 00.5-.485l.048-2.515h.144l.048 2.515a.5.5 0 00.5.485h.128a.5.5 0 00.5-.5V8.38a.25.25 0 00-.073-.177l-.92-.9a.25.25 0 00-.217-.068h-.048zM6.5 4.5a.5.5 0 01.5.5v.354a12.42 12.42 0 002 0V5a.5.5 0 011 0v.354a1.5 1.5 0 01-.436 1.06c-.318.32-.75.544-1.216.63a12.07 12.07 0 01-3.696 0 2.486 2.486 0 01-1.216-.63A1.5 1.5 0 013 5.354V5a.5.5 0 011 0v.354a12.42 12.42 0 002 0V5a.5.5 0 01.5-.5z"/>
                    </svg>
                    <span>Disconnect</span>
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
      message="Are you sure you want to disconnect from this house? It will be removed from your saved houses."
      confirm-text="Disconnect"
      cancel-text="Cancel"
      @confirm="handleDisconnectConfirm"
      @cancel="showDisconnectConfirm = false"
    />
  </div>
</template>

<script setup>
import { ref, h, onMounted, watch, computed, nextTick } from 'vue'
import { useRoute } from 'vue-router'
import { useSettings } from '../composables/useSettings.js'
import ConfirmModal from './ConfirmModal.vue'
import { getHouseId, getSavedHouses, updateSavedHouseName } from '../utils/cookies.js'
import { heatingApi } from '../services/heatingApi.js'

const { settings, setTheme, setTemperatureUnit, setTimeFormat, THEMES, TEMP_UNITS, TIME_FORMATS } = useSettings()

const route = useRoute()
const isBatteryPage = computed(() => route.name === 'battery')

const emit = defineEmits(['disconnect', 'switch-house', 'add-house', 'open-entity-settings'])

const isOpen = ref(false)
const expandedSection = ref(null)
const showDisconnectConfirm = ref(false)
const houseId = ref('')
const houseName = ref('')
const savedHouses = ref([])

// Load house details when menu opens
const loadHouseDetails = async () => {
  houseId.value = getHouseId() || ''
  savedHouses.value = getSavedHouses()

  if (houseId.value) {
    try {
      const details = await heatingApi.getHouseDetails()
      houseName.value = details.name || ''
      // Update saved house name if it changed
      if (houseName.value) {
        updateSavedHouseName(houseId.value, houseName.value)
        savedHouses.value = getSavedHouses()
      }
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

const handleEntitySettingsClick = () => {
  closeMenu()
  emit('open-entity-settings')
}

const editingHouseId = ref(null)
const editingHouseName = ref('')
const isSavingName = ref(false)
const renameInput = ref(null)

const startRename = (house) => {
  editingHouseId.value = house.id
  editingHouseName.value = house.name || ''
  nextTick(() => {
    if (renameInput.value) {
      const input = Array.isArray(renameInput.value) ? renameInput.value[0] : renameInput.value
      input?.focus()
      input?.select()
    }
  })
}

const cancelRename = () => {
  editingHouseId.value = null
  editingHouseName.value = ''
}

const saveHouseName = async () => {
  const name = editingHouseName.value.trim()
  if (!name || isSavingName.value) return

  isSavingName.value = true
  try {
    await heatingApi.setHouseDetails({ name })
    updateSavedHouseName(editingHouseId.value, name)
    houseName.value = name
    savedHouses.value = getSavedHouses()
    editingHouseId.value = null
    editingHouseName.value = ''
  } catch (error) {
    console.error('Failed to save house name:', error)
  } finally {
    isSavingName.value = false
  }
}

const handleSwitchHouse = (id) => {
  closeMenu()
  emit('switch-house', id)
}

const handleAddHouse = () => {
  closeMenu()
  emit('add-house')
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

.house-list {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  margin-bottom: 1rem;
}

.house-item {
  width: 100%;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.75rem;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: 8px;
  cursor: pointer;
  transition: all 0.2s;
  text-align: left;
  color: var(--text-primary);
  font-size: 0.95rem;
}

.house-item:hover {
  border-color: var(--color-primary);
  background-color: var(--hover-bg);
}

.house-item.active {
  border-color: var(--color-primary);
  background-color: var(--hover-bg);
  cursor: default;
}

.house-item-info {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  min-width: 0;
}

.house-item-name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.house-active-badge {
  flex-shrink: 0;
  font-size: 0.7rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  padding: 0.15rem 0.5rem;
  border-radius: 999px;
  background: var(--color-primary);
  color: white;
}

.house-edit-btn {
  flex-shrink: 0;
  background: none;
  border: none;
  color: var(--icon-color);
  cursor: pointer;
  padding: 0.25rem;
  border-radius: 4px;
  display: flex;
  align-items: center;
  transition: all 0.2s;
}

.house-edit-btn:hover {
  color: var(--color-primary);
  background: rgba(0, 0, 0, 0.05);
}

.house-rename-row {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.5rem;
  background: var(--bg-tertiary);
  border: 2px solid var(--color-primary);
  border-radius: 8px;
}

.house-rename-input {
  flex: 1;
  min-width: 0;
  padding: 0.5rem 0.75rem;
  font-size: 0.95rem;
  border: 1px solid var(--border-color);
  border-radius: 6px;
  background: var(--bg-primary);
  color: var(--text-primary);
  outline: none;
}

.house-rename-input:focus {
  border-color: var(--color-primary);
}

.house-rename-btn {
  flex-shrink: 0;
  background: none;
  border: none;
  cursor: pointer;
  padding: 0.375rem;
  border-radius: 4px;
  display: flex;
  align-items: center;
  transition: all 0.2s;
}

.house-rename-btn.save {
  color: var(--color-success, #43a047);
}

.house-rename-btn.save:hover {
  background: rgba(67, 160, 71, 0.1);
}

.house-rename-btn.save:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.house-rename-btn.cancel {
  color: var(--color-danger, #e74c3c);
}

.house-rename-btn.cancel:hover {
  background: rgba(231, 76, 60, 0.1);
}

.house-item-arrow {
  flex-shrink: 0;
  color: var(--icon-color);
}

.house-actions {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.add-house-button {
  width: 100%;
  padding: 0.75rem 1rem;
  background: var(--bg-tertiary);
  color: var(--text-primary);
  border: 2px dashed var(--border-color);
  border-radius: 8px;
  cursor: pointer;
  font-size: 0.95rem;
  font-weight: 500;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.5rem;
  transition: all 0.2s;
}

.add-house-button:hover {
  border-color: var(--color-primary);
  color: var(--color-primary);
  background-color: var(--hover-bg);
}

.disconnect-button {
  width: 100%;
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

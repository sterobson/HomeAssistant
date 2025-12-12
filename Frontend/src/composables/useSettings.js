// User settings management with cookie persistence
import { ref, watch, computed } from 'vue'

const SETTINGS_COOKIE = 'heating-app-settings'

// Available themes
export const THEMES = {
  SYSTEM: 'system',
  LIGHT: 'light',
  DARK: 'dark',
  LIGHT_GREEN: 'light-green',
  DARK_GREEN: 'dark-green',
  LIGHT_BLUE: 'light-blue',
  DARK_BLUE: 'dark-blue',
  LIGHT_PURPLE: 'light-purple',
  DARK_PURPLE: 'dark-purple',
  LIGHT_PINK: 'light-pink',
  DARK_PINK: 'dark-pink',
  LIGHT_GRAY: 'light-gray',
  DARK_GRAY: 'dark-gray',
  LIGHT_RED: 'light-red',
  DARK_RED: 'dark-red',
  LIGHT_ORANGE: 'light-orange',
  DARK_ORANGE: 'dark-orange',
  LIGHT_YELLOW: 'light-yellow',
  DARK_YELLOW: 'dark-yellow',
  LIGHT_BROWN: 'light-brown',
  DARK_BROWN: 'dark-brown'
}

// Temperature units
export const TEMP_UNITS = {
  CELSIUS: 'C',
  FAHRENHEIT: 'F'
}

// Time formats
export const TIME_FORMATS = {
  HOUR_24: '24h',
  HOUR_12: '12h'
}

// Default settings
const defaultSettings = {
  theme: THEMES.SYSTEM,
  temperatureUnit: TEMP_UNITS.CELSIUS,
  timeFormat: TIME_FORMATS.HOUR_24
}

// Cookie helper functions
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

// Load settings from cookies
const loadSettings = () => {
  try {
    const stored = getCookie(SETTINGS_COOKIE)
    if (stored) {
      return { ...defaultSettings, ...JSON.parse(stored) }
    }
  } catch (error) {
    console.error('Failed to load settings:', error)
  }
  return { ...defaultSettings }
}

// Save settings to cookies
const saveSettings = (settings) => {
  try {
    setCookie(SETTINGS_COOKIE, JSON.stringify(settings))
  } catch (error) {
    console.error('Failed to save settings:', error)
  }
}

// Reactive settings
const settings = ref(loadSettings())

// Watch for changes and save
watch(settings, (newSettings) => {
  saveSettings(newSettings)
}, { deep: true })

// Apply theme to document
const applyTheme = (theme) => {
  const root = document.documentElement

  if (theme === THEMES.SYSTEM) {
    // Use system preference
    const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches
    root.setAttribute('data-theme', prefersDark ? 'dark' : 'light')
  } else {
    root.setAttribute('data-theme', theme)
  }
}

// Initialize theme
applyTheme(settings.value.theme)

// Watch for theme changes
watch(() => settings.value.theme, (newTheme) => {
  applyTheme(newTheme)
})

// Listen for system theme changes when using system theme
if (window.matchMedia) {
  window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
    if (settings.value.theme === THEMES.SYSTEM) {
      applyTheme(THEMES.SYSTEM)
    }
  })
}

export function useSettings() {
  return {
    settings,
    THEMES,
    TEMP_UNITS,
    TIME_FORMATS,

    // Getters
    theme: computed(() => settings.value.theme),
    temperatureUnit: computed(() => settings.value.temperatureUnit),
    timeFormat: computed(() => settings.value.timeFormat),

    // Setters
    setTheme: (theme) => {
      settings.value.theme = theme
    },
    setTemperatureUnit: (unit) => {
      settings.value.temperatureUnit = unit
    },
    setTimeFormat: (format) => {
      settings.value.timeFormat = format
    },

    // Reset to defaults
    resetSettings: () => {
      settings.value = { ...defaultSettings }
    }
  }
}

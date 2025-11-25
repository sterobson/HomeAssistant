import { ref, watch, onMounted } from 'vue'

const theme = ref('light')
const isDark = ref(false)

// Cookie helper functions
const setCookie = (name, value, days = 365) => {
  const expires = new Date()
  expires.setTime(expires.getTime() + days * 24 * 60 * 60 * 1000)
  document.cookie = `${name}=${value};expires=${expires.toUTCString()};path=/`
}

const getCookie = (name) => {
  const nameEQ = name + "="
  const ca = document.cookie.split(';')
  for (let i = 0; i < ca.length; i++) {
    let c = ca[i]
    while (c.charAt(0) === ' ') c = c.substring(1, c.length)
    if (c.indexOf(nameEQ) === 0) return c.substring(nameEQ.length, c.length)
  }
  return null
}

const getSystemPreference = () => {
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
}

export function useTheme() {
  const setTheme = (newTheme, systemPrefAtTimeOfChoice = null) => {
    theme.value = newTheme
    setCookie('userTheme', newTheme)

    // Store the system preference at the time the user made their choice
    const currentSystemPref = systemPrefAtTimeOfChoice || getSystemPreference()
    setCookie('systemPrefAtChoice', currentSystemPref)

    applyTheme()
  }

  const applyTheme = () => {
    isDark.value = theme.value === 'dark'

    if (isDark.value) {
      document.documentElement.classList.add('dark')
    } else {
      document.documentElement.classList.remove('dark')
    }
  }

  const toggleTheme = () => {
    const newTheme = theme.value === 'light' ? 'dark' : 'light'
    setTheme(newTheme)
  }

  onMounted(() => {
    const savedTheme = getCookie('userTheme')
    const savedSystemPref = getCookie('systemPrefAtChoice')
    const currentSystemPref = getSystemPreference()

    if (savedTheme && savedSystemPref) {
      // User has a saved preference
      // Check if system preference has changed since they made their choice
      if (savedSystemPref !== currentSystemPref) {
        // System preference has changed, update user's preference to match
        console.log('System preference changed, updating theme to match')
        setTheme(currentSystemPref, currentSystemPref)
      } else {
        // System preference hasn't changed, use saved theme
        theme.value = savedTheme
        applyTheme()
      }
    } else {
      // No saved preference, use system default
      setTheme(currentSystemPref, currentSystemPref)
    }

    // Listen for system theme changes
    const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)')
    const handleChange = (e) => {
      const newSystemPref = e.matches ? 'dark' : 'light'
      const savedSystemPrefAtChoice = getCookie('systemPrefAtChoice')

      // If system preference changes from what it was when user made their choice,
      // update the user's preference to match the new system preference
      if (savedSystemPrefAtChoice && savedSystemPrefAtChoice !== newSystemPref) {
        console.log('System preference changed, updating theme to match')
        setTheme(newSystemPref, newSystemPref)
      }
    }
    mediaQuery.addEventListener('change', handleChange)

    // Cleanup
    return () => mediaQuery.removeEventListener('change', handleChange)
  })

  watch(theme, applyTheme)

  return {
    theme,
    isDark,
    setTheme,
    toggleTheme
  }
}

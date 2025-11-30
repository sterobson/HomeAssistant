// Formatting composable that uses user settings
import { computed } from 'vue'
import { useSettings } from './useSettings.js'
import {
  formatTemperature,
  formatTemperatureWithUnit,
  toInternalTemperature,
  toDisplayTemperature,
  roundToHalf,
  clampToHalf,
  getTemperatureLimits,
  getTemperatureStep
} from '../utils/temperature.js'
import {
  formatTime,
  formatDateTime,
  to24Hour,
  isValidTime
} from '../utils/time.js'

export function useFormatting() {
  const { temperatureUnit, timeFormat } = useSettings()

  // Temperature formatting
  const formatTemp = (celsius) => {
    return formatTemperature(celsius, temperatureUnit.value)
  }

  const formatTempWithUnit = (celsius) => {
    return formatTemperatureWithUnit(celsius, temperatureUnit.value)
  }

  const convertToInternal = (value) => {
    return toInternalTemperature(value, temperatureUnit.value)
  }

  const convertToDisplay = (celsius) => {
    return toDisplayTemperature(celsius, temperatureUnit.value)
  }

  const getDisplayLimits = () => {
    return getTemperatureLimits(temperatureUnit.value)
  }

  const getTempStep = () => {
    return getTemperatureStep()
  }

  const roundTemp = (value) => {
    return clampToHalf(value)
  }

  // Time formatting
  const formatTimeDisplay = (time24) => {
    return formatTime(time24, timeFormat.value)
  }

  const formatDateTimeDisplay = (isoDateTime) => {
    return formatDateTime(isoDateTime, timeFormat.value)
  }

  const convertToInternal24H = (time, period = null) => {
    return to24Hour(time, period)
  }

  const validateTime = (time) => {
    return isValidTime(time)
  }

  // Condition formatting
  const formatConditions = (conditionValue) => {
    if (!conditionValue || conditionValue === 0) {
      return null
    }

    const conditions = []

    // Check each flag (these are bit flags)
    if (conditionValue & 1) conditions.push('Plenty of power available')
    if (conditionValue & 2) conditions.push('Low power available')
    if (conditionValue & 4) conditions.push('Room in use')
    if (conditionValue & 8) conditions.push('Room not in use')

    return conditions.length > 0 ? conditions.join(' & ') : null
  }

  // Days formatting
  const formatDays = (daysValue) => {
    if (!daysValue || daysValue === 0) {
      return 'Every day'
    }

    // Check for special combinations first
    if (daysValue === 127) return 'Every day' // All 7 days
    if (daysValue === 31) return 'Weekdays' // Mon-Fri
    if (daysValue === 96) return 'Weekends' // Sat-Sun
    if (daysValue === 95) return 'Not Sunday' // Mon-Sat

    const days = []
    if (daysValue & 1) days.push('Mon')
    if (daysValue & 2) days.push('Tue')
    if (daysValue & 4) days.push('Wed')
    if (daysValue & 8) days.push('Thu')
    if (daysValue & 16) days.push('Fri')
    if (daysValue & 32) days.push('Sat')
    if (daysValue & 64) days.push('Sun')

    return days.length > 0 ? days.join(', ') : 'Every day'
  }

  // Current unit and format
  const currentTempUnit = computed(() => temperatureUnit.value)
  const currentTimeFormat = computed(() => timeFormat.value)

  return {
    // Temperature
    formatTemp,
    formatTempWithUnit,
    convertToInternal,
    convertToDisplay,
    getDisplayLimits,
    getTempStep,
    roundTemp,
    currentTempUnit,

    // Time
    formatTimeDisplay,
    formatDateTimeDisplay,
    convertToInternal24H,
    validateTime,
    currentTimeFormat,

    // Conditions
    formatConditions,

    // Days
    formatDays
  }
}

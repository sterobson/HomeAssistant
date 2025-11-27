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
    currentTimeFormat
  }
}

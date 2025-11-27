// Temperature conversion and formatting utilities
// All temperatures are stored internally in Celsius

/**
 * Convert Celsius to Fahrenheit
 * @param {number} celsius - Temperature in Celsius
 * @returns {number} Temperature in Fahrenheit
 */
export function celsiusToFahrenheit(celsius) {
  return (celsius * 9/5) + 32
}

/**
 * Convert Fahrenheit to Celsius
 * @param {number} fahrenheit - Temperature in Fahrenheit
 * @returns {number} Temperature in Celsius
 */
export function fahrenheitToCelsius(fahrenheit) {
  return (fahrenheit - 32) * 5/9
}

/**
 * Format temperature for display based on unit
 * Always shows 1 decimal place
 * @param {number} celsius - Temperature in Celsius (internal format)
 * @param {string} unit - Target unit ('C' or 'F')
 * @returns {string} Formatted temperature (e.g., "21.0")
 */
export function formatTemperature(celsius, unit = 'C') {
  if (celsius === null || celsius === undefined) {
    return '--'
  }

  const temp = unit === 'F' ? celsiusToFahrenheit(celsius) : celsius
  return temp.toFixed(1)
}

/**
 * Format temperature with unit symbol for display
 * @param {number} celsius - Temperature in Celsius (internal format)
 * @param {string} unit - Target unit ('C' or 'F')
 * @returns {string} Formatted temperature with unit (e.g., "21.0°C")
 */
export function formatTemperatureWithUnit(celsius, unit = 'C') {
  const temp = formatTemperature(celsius, unit)
  return `${temp}°${unit}`
}

/**
 * Convert user input temperature to Celsius for storage
 * @param {number} value - Temperature value in user's unit
 * @param {string} unit - User's unit ('C' or 'F')
 * @returns {number} Temperature in Celsius
 */
export function toInternalTemperature(value, unit = 'C') {
  return unit === 'F' ? fahrenheitToCelsius(value) : value
}

/**
 * Convert internal temperature to user's unit for editing
 * @param {number} celsius - Temperature in Celsius (internal format)
 * @param {string} unit - Target unit ('C' or 'F')
 * @returns {number} Temperature in user's unit
 */
export function toDisplayTemperature(celsius, unit = 'C') {
  return unit === 'F' ? celsiusToFahrenheit(celsius) : celsius
}

/**
 * Round temperature to nearest 0.5 increment in the given unit
 * @param {number} value - Temperature value
 * @returns {number} Rounded temperature
 */
export function roundToHalf(value) {
  return Math.round(value * 2) / 2
}

/**
 * Get temperature increment/decrement step for current unit
 * Always 0.5 in the current unit
 * @returns {number} Step value (always 0.5)
 */
export function getTemperatureStep() {
  return 0.5
}

/**
 * Clamp temperature value to nearest 0.5 (.0 or .5)
 * @param {number} value - Temperature value
 * @returns {number} Clamped temperature
 */
export function clampToHalf(value) {
  const rounded = roundToHalf(value)
  // Ensure it ends in .0 or .5
  const decimal = rounded % 1
  if (Math.abs(decimal - 0.5) < 0.01) {
    return Math.floor(rounded) + 0.5
  } else {
    return Math.floor(rounded)
  }
}

/**
 * Get min/max temperature limits in user's unit
 * Internal limits: 5°C to 30°C
 * @param {string} unit - Target unit ('C' or 'F')
 * @returns {object} { min, max } in user's unit
 */
export function getTemperatureLimits(unit = 'C') {
  const minC = 5
  const maxC = 30

  if (unit === 'F') {
    return {
      min: celsiusToFahrenheit(minC),
      max: celsiusToFahrenheit(maxC)
    }
  }

  return { min: minC, max: maxC }
}

// Time formatting utilities
// All times are stored internally in 24-hour format (e.g., "18:00")

/**
 * Format time for display based on user preference
 * @param {string} time24 - Time in 24-hour format (e.g., "18:00")
 * @param {string} format - Target format ('24h' or '12h')
 * @returns {string} Formatted time
 */
export function formatTime(time24, format = '24h') {
  if (!time24) return ''

  if (format === '24h') {
    return time24
  }

  // Convert to 12-hour format
  const [hours, minutes] = time24.split(':').map(Number)

  if (isNaN(hours) || isNaN(minutes)) {
    return time24
  }

  const period = hours >= 12 ? 'PM' : 'AM'
  const hours12 = hours === 0 ? 12 : hours > 12 ? hours - 12 : hours

  return `${hours12}:${minutes.toString().padStart(2, '0')} ${period}`
}

/**
 * Convert 12-hour time to 24-hour format for storage
 * @param {string} time12 - Time in 12-hour format (e.g., "6:00 PM")
 * @param {string} period - AM or PM (if not included in time12)
 * @returns {string} Time in 24-hour format (e.g., "18:00")
 */
export function to24Hour(time12, period = null) {
  if (!time12) return ''

  // Extract period if included in string
  let timePart = time12.trim()
  let actualPeriod = period

  const timeWithPeriod = time12.match(/^(\d{1,2}):(\d{2})\s*(AM|PM)$/i)
  if (timeWithPeriod) {
    const [, hours, minutes, extractedPeriod] = timeWithPeriod
    actualPeriod = extractedPeriod.toUpperCase()
    timePart = `${hours}:${minutes}`
  }

  const [hours, minutes] = timePart.split(':').map(Number)

  if (isNaN(hours) || isNaN(minutes)) {
    return time12
  }

  let hours24 = hours

  if (actualPeriod) {
    if (actualPeriod === 'PM' && hours !== 12) {
      hours24 = hours + 12
    } else if (actualPeriod === 'AM' && hours === 12) {
      hours24 = 0
    }
  }

  return `${hours24.toString().padStart(2, '0')}:${minutes.toString().padStart(2, '0')}`
}

/**
 * Format datetime for display
 * @param {string} isoDateTime - ISO datetime string
 * @param {string} timeFormat - Time format ('24h' or '12h')
 * @returns {string} Formatted datetime
 */
export function formatDateTime(isoDateTime, timeFormat = '24h') {
  if (!isoDateTime) return ''

  const date = new Date(isoDateTime)
  const hours = date.getHours()
  const minutes = date.getMinutes()

  const time24 = `${hours.toString().padStart(2, '0')}:${minutes.toString().padStart(2, '0')}`

  return formatTime(time24, timeFormat)
}

/**
 * Validate time string format
 * @param {string} time - Time string to validate
 * @returns {boolean} True if valid
 */
export function isValidTime(time) {
  if (!time) return false

  const time24Pattern = /^([01]?\d|2[0-3]):([0-5]\d)$/
  const time12Pattern = /^(1[0-2]|0?[1-9]):([0-5]\d)\s*(AM|PM)$/i

  return time24Pattern.test(time) || time12Pattern.test(time)
}

<template>
  <Teleport to="body">
    <Transition name="modal">
      <div v-if="show" class="modal-overlay" @click="handleOverlayClick">
        <div class="modal-content" @click.stop>
          <div class="modal-header">
            <h2>History - {{ name }}</h2>
            <button class="close-btn" @click="emit('close')" title="Close">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor">
                <path d="M18 6L6 18M6 6l12 12" stroke-width="2" stroke-linecap="round"/>
              </svg>
            </button>
          </div>

          <div class="date-navigation">
            <button class="nav-btn" @click="navigateDate(-1)" title="Previous">
              <svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor">
                <path d="M11.354 1.646a.5.5 0 0 1 0 .708L5.707 8l5.647 5.646a.5.5 0 0 1-.708.708l-6-6a.5.5 0 0 1 0-.708l6-6a.5.5 0 0 1 .708 0z"/>
              </svg>
            </button>

            <div class="date-controls">
              <div class="period-selector">
                <button
                  v-for="p in periods"
                  :key="p"
                  :class="['period-btn', { active: period === p }]"
                  @click="changePeriod(p)"
                >
                  {{ p }}
                </button>
              </div>
              <div class="date-display" :class="{ clickable: period === 'Day' }" @click="handleDateClick">
                {{ formattedDateRange }}
              </div>

              <!-- Date picker for Day mode -->
              <div v-if="showDatePicker" class="date-picker-overlay" @click="showDatePicker = false">
                <div class="date-picker-modal" @click.stop>
                  <h3>Select Date</h3>
                  <input
                    type="date"
                    :value="selectedDateString"
                    :max="todayString"
                    @change="handleDateSelect"
                    class="date-input"
                  />
                  <div class="date-picker-actions">
                    <button class="date-picker-btn today-btn" @click="handleTodayClick">Today</button>
                    <button class="date-picker-btn cancel-btn" @click="showDatePicker = false">Cancel</button>
                  </div>
                </div>
              </div>
            </div>

            <button class="nav-btn" @click="navigateDate(1)" :disabled="!canNavigateForward" title="Next">
              <svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor">
                <path d="M4.646 1.646a.5.5 0 0 1 .708 0l6 6a.5.5 0 0 1 0 .708l-6 6a.5.5 0 0 1-.708-.708L10.293 8 4.646 2.354a.5.5 0 0 1 0-.708z"/>
              </svg>
            </button>
          </div>

          <div class="modal-body">
            <div class="chart-section">
              <div class="chart-wrapper">
                <div v-if="loading" class="chart-overlay loading-overlay">
                  <div class="spinner"></div>
                  <p>Loading temperature history...</p>
                </div>

                <div v-else-if="error" class="chart-overlay error-overlay">
                  <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor">
                    <circle cx="12" cy="12" r="10" stroke-width="2"/>
                    <path d="M12 8v4m0 4h.01" stroke-width="2" stroke-linecap="round"/>
                  </svg>
                  <p>{{ error }}</p>
                  <button class="retry-btn" @click="reload">Try again</button>
                </div>

                <div v-else-if="points.length === 0" class="chart-overlay empty-overlay">
                  <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor">
                    <path d="M3 3h18v18H3V3z" stroke-width="2"/>
                    <path d="M8 12h8M12 8v8" stroke-width="2" stroke-linecap="round"/>
                  </svg>
                  <p>No temperature history available for this period</p>
                </div>

                <TemperatureChart
                  v-show="!loading && !error && points.length > 0"
                  :history-data="points"
                  :room-name="name"
                  :period="period"
                  :heating-stretches="heatingStretches"
                  :uses-boiler-history="usesBoilerHistory"
                  :range="isGroup"
                />
              </div>

              <div class="summary-section">
                <div class="summary-card">
                    <div class="summary-icon heating-icon">
                      <svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor">
                        <path d="M8 16c3.314 0 6-2 6-5.5 0-1.5-.5-4-2.5-6 .25 1.5-1.25 2-1.25 2C11 4 9 .5 6 0c.357 2 .5 4-2 6-1.25 1-2 2.729-2 4.5C2 14 4.686 16 8 16m0-1c-1.657 0-3-1-3-2.75 0-.75.25-2 1.25-3C6.125 10 7 10.5 7 10.5c-.375-1.25.5-3.25 2-3.5-.179 1-.25 2 1 3 .625.5 1 1.364 1 2.25C11 14 9.657 15 8 15"/>
                      </svg>
                    </div>
                    <div class="summary-info">
                      <div class="summary-label">{{ isGroup ? 'Anything heating' : 'Heating' }}</div>
                      <div class="summary-value">
                        <div v-if="loading" class="value-spinner"></div>
                        <span v-else>{{ formatDuration(heatingMinutes) }}</span>
                      </div>
                      <!-- By source, only where there is more than one. Both can run together, so these can add up to
                           more than the total above. -->
                      <template v-if="!loading && showSourceBreakdown">
                        <ul class="source-list">
                          <li v-for="source in heatingSources" :key="source.key">
                            <span class="source-name">{{ source.label }}</span>
                            <span class="source-time">{{ formatDuration(totalMinutes(source.stretches)) }}</span>
                          </li>
                        </ul>
                        <p class="summary-note">Sources can run together, so they may add up to more than the total.</p>
                        <p v-if="splitIncomplete" class="summary-note">Earlier heating can't be split by source, so it counts in the total only.</p>
                      </template>
                      <p v-else-if="!loading && usesBoilerHistory" class="summary-note">Boiler on and calling for heat.</p>
                    </div>
                  </div>

                <div class="summary-card">
                  <div class="summary-icon temp-icon">
                    <svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor">
                      <path d="M8 14a2 2 0 1 0 0-4 2 2 0 0 0 0 4"/>
                      <path d="M9.5 2v7.5a2.5 2.5 0 1 1-3 0V2a1.5 1.5 0 1 1 3 0z"/>
                    </svg>
                  </div>
                  <div class="summary-info">
                    <div class="summary-label">Average Temperature</div>
                    <div class="summary-value">
                      <div v-if="loading" class="value-spinner"></div>
                      <span v-else>{{ averageTemperature }}</span>
                    </div>
                  </div>
                </div>

                <div class="summary-card">
                  <div class="summary-icon range-icon">
                    <svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor">
                      <path d="M5 1a1 1 0 0 1 1 1v1h4V2a1 1 0 1 1 2 0v1h1a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h1V2a1 1 0 0 1 1-1zm9 4H2v10a1 1 0 0 0 1 1h10a1 1 0 0 0 1-1V5z"/>
                      <path d="M2.5 7.5A.5.5 0 0 1 3 7h10a.5.5 0 0 1 0 1H3a.5.5 0 0 1-.5-.5zm0 3A.5.5 0 0 1 3 10h4a.5.5 0 0 1 0 1H3a.5.5 0 0 1-.5-.5z"/>
                    </svg>
                  </div>
                  <div class="summary-info">
                    <div class="summary-label">Temperature Range</div>
                    <div class="summary-value">
                      <div v-if="loading" class="value-spinner"></div>
                      <span v-else>{{ temperatureRange }}</span>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<script setup>
import { computed, ref, watch } from 'vue'
import TemperatureChart from './TemperatureChart.vue'
import { useFormatting } from '../composables/useFormatting.js'
import { useZoneHistory } from '../composables/useZoneHistory.js'
import { totalMinutes } from '../utils/heatingStretches.js'

const { convertToDisplay, currentTempUnit } = useFormatting()

// One history view for a zone and for a group alike: give it the zones to show, and it loads and draws them. A group
// shows the range across its zones and everything in it that was heating.
const props = defineProps({
  show: {
    type: Boolean,
    required: true
  },
  // The zone's or group's name, for the title.
  name: {
    type: String,
    default: 'Room'
  },
  // The zones to show, as room states (`id`, `devices`): the one zone, or every zone in the group.
  rooms: {
    type: Array,
    required: true
  },
  isGroup: {
    type: Boolean,
    default: false
  }
})

const emit = defineEmits(['close'])

const { points, heatingStretches, heatingSources, splitIncomplete, usesBoilerHistory, loading, error, load } = useZoneHistory()

const periods = ['Day', 'Week', 'Month']
const period = ref('Day')
const selectedDate = ref(new Date())
const showDatePicker = ref(false)

// Computed values for date picker
const selectedDateString = computed(() => {
  const d = selectedDate.value
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
})

const todayString = computed(() => {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
})

// Calculate start and end dates based on period
const dateRange = computed(() => {
  const date = new Date(selectedDate.value)
  let startDate, endDate

  if (period.value === 'Day') {
    startDate = new Date(date.getFullYear(), date.getMonth(), date.getDate(), 0, 0, 0, 0)
    endDate = new Date(date.getFullYear(), date.getMonth(), date.getDate(), 23, 59, 59, 999)
  } else if (period.value === 'Week') {
    // Find Monday of the week
    const dayOfWeek = date.getDay()
    const diff = dayOfWeek === 0 ? -6 : 1 - dayOfWeek // Adjust for Monday start
    startDate = new Date(date)
    startDate.setDate(date.getDate() + diff)
    startDate.setHours(0, 0, 0, 0)
    // Sunday of the week
    endDate = new Date(startDate)
    endDate.setDate(startDate.getDate() + 6)
    endDate.setHours(23, 59, 59, 999)
  } else { // Month
    startDate = new Date(date.getFullYear(), date.getMonth(), 1, 0, 0, 0, 0)
    endDate = new Date(date.getFullYear(), date.getMonth() + 1, 0, 23, 59, 59, 999)
  }

  return { startDate, endDate }
})

// Format the date range for display
const formattedDateRange = computed(() => {
  const { startDate, endDate } = dateRange.value
  const options = { year: 'numeric', month: 'short', day: 'numeric' }

  if (period.value === 'Day') {
    return startDate.toLocaleDateString(undefined, options)
  } else if (period.value === 'Week') {
    return `${startDate.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })} - ${endDate.toLocaleDateString(undefined, options)}`
  } else {
    return startDate.toLocaleDateString(undefined, { year: 'numeric', month: 'long' })
  }
})

// Check if we can navigate forward (not beyond today)
const canNavigateForward = computed(() => {
  const { endDate } = dateRange.value
  return endDate < new Date()
})

// Loads the zones' history for the period shown.
const reload = () => load(props.rooms, props.isGroup, dateRange.value.startDate, dateRange.value.endDate)

const changePeriod = (newPeriod) => {
  period.value = newPeriod
  reload()
}

const navigateDate = (direction) => {
  const date = new Date(selectedDate.value)

  if (period.value === 'Day') {
    date.setDate(date.getDate() + direction)
  } else if (period.value === 'Week') {
    date.setDate(date.getDate() + (direction * 7))
  } else { // Month
    date.setMonth(date.getMonth() + direction)
  }

  selectedDate.value = date
  reload()
}

// Watch for modal open to reset to current date
watch(() => props.show, (newShow) => {
  if (newShow) {
    selectedDate.value = new Date()
    period.value = 'Day'
    reload()
  }
})

// Minutes as "2 hours, 5 minutes", up to days.
const formatDuration = (minutes) => {
  const roundedMinutes = Math.round(minutes)

  if (roundedMinutes < 60) {
    return `${roundedMinutes} minute${roundedMinutes !== 1 ? 's' : ''}`
  }

  const days = Math.floor(roundedMinutes / (24 * 60))
  const hours = Math.floor((roundedMinutes % (24 * 60)) / 60)
  const mins = roundedMinutes % 60

  const parts = []
  if (days > 0) parts.push(`${days} day${days !== 1 ? 's' : ''}`)
  if (hours > 0) parts.push(`${hours} hour${hours !== 1 ? 's' : ''}`)
  if (mins > 0) parts.push(`${mins} minute${mins !== 1 ? 's' : ''}`)

  return parts.join(', ')
}

// The time the zone was genuinely heating (a group: the time anything in it was), however many sources were running.
const heatingMinutes = computed(() => totalMinutes(heatingStretches.value))

// Broken down by source only where there is more than one kind of heat to tell apart.
const showSourceBreakdown = computed(() => heatingSources.value.length > 1)

// A zone's temperatures at each point; a group's, the mean across its zones.
const currentTemperatures = computed(() =>
  points.value
    .map(point => point.currentTemperatureMean ?? point.currentTemperature)
    .filter(temperature => temperature != null)
    .map(temperature => convertToDisplay(temperature)))

// Calculate average temperature
const averageTemperature = computed(() => {
  if (currentTemperatures.value.length === 0) return '0.0'

  const avg = currentTemperatures.value.reduce((sum, temp) => sum + temp, 0) / currentTemperatures.value.length
  return avg.toFixed(1) + '°' + currentTempUnit.value
})

// Calculate temperature range: for a group, from its coolest zone's lowest to its warmest zone's highest.
const temperatureRange = computed(() => {
  const lows = points.value.map(point => point.currentTemperature).filter(temperature => temperature != null)
  const highs = points.value.map(point => point.currentTemperatureMax ?? point.currentTemperature).filter(temperature => temperature != null)
  if (lows.length === 0) return '0.0°' + currentTempUnit.value

  const min = convertToDisplay(Math.min(...lows))
  const max = convertToDisplay(Math.max(...highs))
  return `${min.toFixed(1)}°${currentTempUnit.value} - ${max.toFixed(1)}°${currentTempUnit.value}`
})

const handleOverlayClick = () => {
  emit('close')
}

const handleDateClick = () => {
  if (period.value === 'Day') {
    showDatePicker.value = true
  }
}

const handleDateSelect = (event) => {
  const dateStr = event.target.value
  const [year, month, day] = dateStr.split('-').map(Number)
  selectedDate.value = new Date(year, month - 1, day)
  showDatePicker.value = false
  reload()
}

const handleTodayClick = () => {
  selectedDate.value = new Date()
  showDatePicker.value = false
  reload()
}
</script>

<style scoped>
.modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background-color: rgba(0, 0, 0, 0.7);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
  padding: 20px;
}

.modal-content {
  background-color: var(--bg-primary);
  border-radius: 12px;
  width: 100%;
  max-width: 900px;
  max-height: 90vh;
  overflow-y: auto;
  box-shadow: 0 10px 40px rgba(0, 0, 0, 0.3);
}

.modal-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 16px 20px;
  border-bottom: 1px solid var(--border-color);
}

.modal-header h2 {
  margin: 0;
  font-size: 1.25rem;
  color: var(--text-primary);
}

.date-navigation {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 12px 20px;
  border-bottom: 1px solid var(--border-color);
}

.date-controls {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
}

.period-selector {
  display: flex;
  gap: 6px;
}

.period-btn {
  padding: 4px 12px;
  border: 1px solid var(--border-color);
  background: var(--bg-secondary);
  color: var(--text-secondary);
  border-radius: 6px;
  cursor: pointer;
  font-size: 0.8rem;
  font-weight: 500;
  transition: all 0.2s;
}

.period-btn:hover {
  background: var(--bg-tertiary);
  color: var(--text-primary);
}

.period-btn.active {
  background: var(--color-primary);
  color: white;
  border-color: var(--color-primary);
}

.date-display {
  font-size: 0.95rem;
  font-weight: 600;
  color: var(--text-primary);
  text-align: center;
}

.date-display.clickable {
  cursor: pointer;
  padding: 4px 8px;
  border-radius: 4px;
  transition: background-color 0.2s;
}

.date-display.clickable:hover {
  background-color: var(--bg-tertiary);
}

.date-picker-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background-color: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 2000;
}

.date-picker-modal {
  background-color: var(--bg-secondary);
  border-radius: 12px;
  padding: 24px;
  box-shadow: 0 10px 40px rgba(0, 0, 0, 0.3);
  min-width: 300px;
}

.date-picker-modal h3 {
  margin: 0 0 16px 0;
  font-size: 1.1rem;
  color: var(--text-primary);
}

.date-input {
  width: 100%;
  padding: 10px;
  border: 1px solid var(--border-color);
  border-radius: 6px;
  font-size: 1rem;
  background-color: var(--bg-primary);
  color: var(--text-primary);
  margin-bottom: 16px;
}

.date-picker-actions {
  display: flex;
  gap: 8px;
  justify-content: flex-end;
}

.date-picker-btn {
  padding: 8px 16px;
  border: none;
  border-radius: 6px;
  font-size: 0.9rem;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s;
}

.today-btn {
  background-color: var(--color-primary);
  color: white;
}

.today-btn:hover {
  opacity: 0.9;
}

.cancel-btn {
  background-color: var(--bg-tertiary);
  color: var(--text-primary);
}

.cancel-btn:hover {
  background-color: var(--border-color);
}

.nav-btn {
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  color: var(--text-primary);
  cursor: pointer;
  padding: 8px;
  border-radius: 6px;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.2s;
  flex-shrink: 0;
}

.nav-btn:hover:not(:disabled) {
  background: var(--bg-tertiary);
}

.nav-btn:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

.close-btn {
  background: none;
  border: none;
  color: var(--text-secondary);
  cursor: pointer;
  padding: 8px;
  border-radius: 8px;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.2s;
}

.close-btn:hover {
  background-color: var(--bg-tertiary);
  color: var(--text-primary);
}

.modal-body {
  padding: 16px 20px;
}

.loading-state,
.error-state,
.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 60px 20px;
  text-align: center;
  color: var(--text-secondary);
}

.spinner {
  width: 48px;
  height: 48px;
  border: 4px solid var(--border-color);
  border-top-color: var(--color-primary);
  border-radius: 50%;
  animation: spin 1s linear infinite;
  margin-bottom: 20px;
}

@keyframes spin {
  to { transform: rotate(360deg); }
}

.error-state svg,
.empty-state svg {
  color: var(--text-secondary);
  margin-bottom: 16px;
  opacity: 0.5;
}

.retry-btn {
  margin-top: 16px;
  padding: 10px 20px;
  background-color: var(--color-primary);
  color: white;
  border: none;
  border-radius: 8px;
  cursor: pointer;
  font-weight: 500;
  transition: opacity 0.2s;
}

.retry-btn:hover {
  opacity: 0.9;
}

.chart-section {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.chart-wrapper {
  position: relative;
  min-height: 360px;
  display: flex;
  align-items: center;
  justify-content: center;
  background-color: var(--bg-primary);
}

.chart-overlay {
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  text-align: center;
  color: var(--text-secondary);
  background-color: var(--bg-primary);
  z-index: 10;
}

.summary-section {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 12px;
}

.summary-card {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 16px;
  background-color: var(--bg-secondary);
  border-radius: 10px;
  border: 1px solid var(--border-color);
}

.summary-icon {
  width: 48px;
  height: 48px;
  border-radius: 10px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.heating-icon {
  background-color: rgba(251, 140, 0, 0.1);
  color: rgb(251, 140, 0);
}

.temp-icon {
  background-color: rgba(66, 165, 245, 0.1);
  color: rgb(66, 165, 245);
}

.range-icon {
  background-color: rgba(156, 39, 176, 0.1);
  color: rgb(156, 39, 176);
}

.summary-info {
  flex: 1;
  min-width: 0;
}

.summary-label {
  font-size: 0.875rem;
  color: var(--text-secondary);
  margin-bottom: 4px;
}

.summary-value {
  font-size: 1.25rem;
  font-weight: 600;
  color: var(--text-primary);
  min-height: 1.875rem;
  display: flex;
  align-items: center;
}

.source-list {
  list-style: none;
  margin: 4px 0 0;
  padding: 0;
  font-size: 0.85rem;
  color: var(--text-secondary);
}

.source-list li {
  display: flex;
  justify-content: space-between;
  gap: 12px;
}

.source-time {
  color: var(--text-primary);
  font-weight: 500;
}

.summary-note {
  margin: 4px 0 0;
  font-size: 0.75rem;
  color: var(--text-secondary);
}

.value-spinner {
  width: 20px;
  height: 20px;
  border: 2px solid var(--border-color);
  border-top-color: var(--color-primary);
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}

.modal-enter-active,
.modal-leave-active {
  transition: opacity 0.3s ease;
}

.modal-enter-active .modal-content,
.modal-leave-active .modal-content {
  transition: transform 0.3s ease;
}

.modal-enter-from,
.modal-leave-to {
  opacity: 0;
}

.modal-enter-from .modal-content {
  transform: scale(0.9);
}

.modal-leave-to .modal-content {
  transform: scale(0.9);
}

@media (max-width: 768px) {
  .modal-overlay {
    padding: 0;
  }

  .modal-content {
    max-width: 100%;
    max-height: 100vh;
    border-radius: 0;
  }

  .modal-header {
    padding: 12px 16px;
  }

  .modal-header h2 {
    font-size: 1.1rem;
  }

  .date-navigation {
    padding: 10px 16px;
    gap: 8px;
  }

  .date-display {
    font-size: 0.85rem;
  }

  .period-btn {
    padding: 3px 10px;
    font-size: 0.75rem;
  }

  .modal-body {
    padding: 12px 16px;
  }

  .chart-wrapper {
    min-height: 300px;
  }

  .summary-section {
    grid-template-columns: 1fr;
    gap: 10px;
  }

  .summary-card {
    padding: 12px;
  }
}
</style>

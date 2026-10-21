<template>
  <div class="boiler-overlay" :style="{ zIndex }" @click.self="emit('close')">
    <div class="boiler-modal" role="dialog" aria-modal="true" aria-label="Boiler history">
      <div class="boiler-header">
        <h3>Boiler history</h3>
        <button class="close-btn" aria-label="Close" @click="emit('close')">
          <svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor">
            <path d="M4.646 4.646a.5.5 0 01.708 0L10 9.293l4.646-4.647a.5.5 0 01.708.708L10.707 10l4.647 4.646a.5.5 0 01-.708.708L10 10.707l-4.646 4.647a.5.5 0 01-.708-.708L9.293 10 4.646 5.354a.5.5 0 010-.708z"/>
          </svg>
        </button>
      </div>

      <div class="boiler-body">
        <div class="period-bar" role="group" aria-label="Period">
          <button
            v-for="option in PERIODS"
            :key="option.hours"
            type="button"
            class="period-btn"
            :class="{ active: hours === option.hours }"
            :aria-pressed="hours === option.hours"
            @click="hours = option.hours"
          >
            {{ option.label }}
          </button>
        </div>

        <p v-if="loading" class="status-text">Loading…</p>
        <p v-else-if="error" class="error-text" role="alert">{{ error }}</p>

        <template v-else-if="segments.length">
          <p class="headline">
            <span class="headline-value">{{ formatMinutes(onMinutes) }}</span>
            <span class="headline-label">on in the last {{ periodLabel }}, turned on {{ onCount === 1 ? 'once' : `${onCount} times` }}</span>
          </p>

          <!-- The timeline: one stretch per state, positioned by time. Hovering reads out the stretch under the pointer. -->
          <div
            class="timeline"
            @mousemove="handleHover"
            @mouseleave="hovered = null"
          >
            <div
              v-for="(segment, index) in segments"
              :key="index"
              class="segment"
              :class="[segment.state, { hovered: hovered === segment, inferred: segment.inferred }]"
              :style="{ left: `${position(segment.start)}%`, width: `${Math.max(position(segment.end) - position(segment.start), 0.3)}%` }"
              :title="describeSegment(segment)"
            ></div>
          </div>
          <div class="axis" aria-hidden="true">
            <span v-for="tick in ticks" :key="tick.at" class="tick" :style="{ left: `${tick.position}%` }">{{ tick.label }}</span>
          </div>
          <p class="readout" aria-live="polite">{{ hovered ? describeSegment(hovered) : 'Point at the timeline for times.' }}</p>

          <ul class="legend">
            <li v-for="entry in legendShown" :key="entry.key" class="legend-item">
              <span class="swatch" :class="[entry.state, { inferred: entry.inferred }]" aria-hidden="true"></span>{{ entry.label }}
            </li>
          </ul>

          <h4 class="section-title">Changes</h4>
          <p v-if="changes.length === 0" class="status-text">No changes in this period.</p>
          <table v-else class="changes">
            <thead>
              <tr><th scope="col">When</th><th scope="col">Boiler</th></tr>
            </thead>
            <tbody>
              <tr v-for="change in changes" :key="change.at">
                <td>{{ formatWhen(new Date(change.at)) }}</td>
                <td>{{ stateLabel(change.state) }}<span v-if="change.inferred" class="inferred-tag"> · from the radiators</span></td>
              </tr>
            </tbody>
          </table>
        </template>

        <p class="belief-note">
          What the app believed from its own presses of the fingerbot. Nothing can read the boiler itself, so a press
          that did not take, or one made by hand at the boiler, is not shown. Recorded since 21 October 2026.
          Hatched stretches before that were worked out afterwards from the radiators warming and cooling: what the
          boiler evidently did, whoever ran it, and only roughly when.
        </p>
      </div>

      <div class="boiler-actions">
        <button type="button" class="btn btn-secondary" @click="emit('close')">Close</button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed, ref, watch } from 'vue'
import { heatingApi } from '../services/heatingApi.js'
import { useFormatting } from '../composables/useFormatting.js'
import { boilerSegments, minutesOn, stateLabel, timesOn } from '../utils/boilerHistory.js'
import { formatMinutes } from '../utils/warmUp.js'

defineProps({
  zIndex: {
    type: Number,
    default: 1000
  }
})

const emit = defineEmits(['close'])
const { formatTimeDisplay } = useFormatting()

const PERIODS = [
  { hours: 24, label: '24 hours' },
  { hours: 48, label: '48 hours' },
  { hours: 168, label: '7 days' }
]

const hours = ref(24)
const history = ref(null)
const loading = ref(false)
const error = ref('')
const hovered = ref(null)

const load = async () => {
  loading.value = true
  error.value = ''
  hovered.value = null
  try {
    history.value = await heatingApi.getBoilerHistory(hours.value)
  } catch (err) {
    error.value = 'The boiler history could not be loaded. Try again.'
  } finally {
    loading.value = false
  }
}
watch(hours, load, { immediate: true })

const from = computed(() => (history.value ? new Date(history.value.from) : null))
const to = computed(() => (history.value ? new Date(history.value.to) : null))
const segments = computed(() => (history.value ? boilerSegments(history.value, from.value, to.value) : []))
const onMinutes = computed(() => minutesOn(segments.value))
const onCount = computed(() => (from.value ? timesOn(segments.value, from.value) : 0))
const periodLabel = computed(() => PERIODS.find(p => p.hours === hours.value)?.label ?? `${hours.value} hours`)

// Only the entries that appear, in a fixed order, so the legend never lists one that is not on the chart.
const LEGEND = [
  { key: 'on', state: 'on', inferred: false, label: 'On' },
  { key: 'on-inferred', state: 'on', inferred: true, label: 'On (worked out from the radiators)' },
  { key: 'off', state: 'off', inferred: false, label: 'Off' },
  { key: 'off-inferred', state: 'off', inferred: true, label: 'Off (worked out from the radiators)' },
  { key: 'thermostat', state: 'thermostat', inferred: false, label: stateLabel('thermostat') },
  { key: 'unknown', state: 'unknown', inferred: false, label: stateLabel('unknown') }
]
const legendShown = computed(() =>
  LEGEND.filter(entry => segments.value.some(s => s.state === entry.state && (s.inferred === true) === entry.inferred)))

// Each stretch's start inside the window, newest first: the table view of the same data.
const changes = computed(() =>
  segments.value
    .filter(s => from.value && s.start > from.value)
    .map(s => ({ at: s.start.toISOString(), state: s.state, inferred: s.inferred === true }))
    .reverse())

const position = (date) => {
  const span = to.value - from.value
  return span > 0 ? ((date - from.value) / span) * 100 : 0
}

const clock = (date) =>
  formatTimeDisplay(`${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`)
const isToday = (date) => date.toDateString() === new Date().toDateString()
const formatWhen = (date) =>
  isToday(date) ? clock(date) : `${date.toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'short' })}, ${clock(date)}`

const describeSegment = (segment) =>
  `${stateLabel(segment.state)}${segment.inferred ? ' (worked out from the radiators)' : ''}: ${formatWhen(segment.start)} to ${formatWhen(segment.end)} (${formatMinutes((segment.end - segment.start) / 60000)})`

// Five evenly spaced labels; days for the week view, clock times otherwise.
const ticks = computed(() => {
  if (!from.value) return []
  const count = 4
  return Array.from({ length: count + 1 }, (_, i) => {
    const at = new Date(from.value.getTime() + ((to.value - from.value) * i) / count)
    const label = hours.value > 48
      ? at.toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric' })
      : clock(at)
    return { at: at.toISOString(), position: (i / count) * 100, label }
  })
})

const handleHover = (event) => {
  const rect = event.currentTarget.getBoundingClientRect()
  const fraction = Math.min(Math.max((event.clientX - rect.left) / rect.width, 0), 1)
  const at = new Date(from.value.getTime() + (to.value - from.value) * fraction)
  hovered.value = segments.value.find(s => s.start <= at && at <= s.end) ?? null
}
</script>

<style scoped>
.boiler-overlay {
  position: fixed;
  inset: 0;
  background-color: var(--overlay);
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 1rem;
  animation: fadeIn 0.2s;
  cursor: default;
}

@keyframes fadeIn {
  from { opacity: 0; }
  to { opacity: 1; }
}

.boiler-modal {
  background: var(--bg-secondary);
  border-radius: 12px;
  box-shadow: 0 8px 32px var(--shadow-md);
  width: 100%;
  max-width: 640px;
  max-height: 90vh;
  display: flex;
  flex-direction: column;
  animation: slideUp 0.3s;
}

@keyframes slideUp {
  from { transform: translateY(20px); opacity: 0; }
  to { transform: translateY(0); opacity: 1; }
}

.boiler-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 1.25rem 1.5rem;
  border-bottom: 1px solid var(--border-color);
}

.boiler-header h3 {
  margin: 0;
  font-size: 1.15rem;
  color: var(--text-primary);
}

.close-btn {
  background: none;
  border: none;
  color: var(--icon-color);
  cursor: pointer;
  padding: 0.25rem;
  border-radius: 4px;
  display: flex;
}

.close-btn:hover {
  background-color: var(--hover-bg);
}

.boiler-body {
  padding: 1.25rem 1.5rem;
  overflow-y: auto;
}

.period-bar {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
  margin-bottom: 1rem;
}

.period-btn {
  padding: 0.3rem 0.75rem;
  border: 1px solid var(--border-color);
  border-radius: 999px;
  background: var(--bg-secondary);
  color: var(--text-primary);
  font-size: 0.85rem;
  font-weight: 600;
  cursor: pointer;
}

.period-btn:hover {
  border-color: var(--color-primary);
}

.period-btn.active {
  background: var(--color-primary);
  border-color: var(--color-primary);
  color: white;
}

.headline {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 0.5rem;
  margin: 0 0 1rem;
}

.headline-value {
  font-size: 1.75rem;
  font-weight: 700;
  color: var(--text-primary);
}

.headline-label {
  color: var(--text-secondary);
  font-size: 0.9rem;
}

/* The strip. A 2px surface gap separates neighbouring stretches. */
.timeline {
  position: relative;
  height: 36px;
  border-radius: 6px;
  background: var(--bg-primary);
  overflow: hidden;
  cursor: crosshair;
}

.segment {
  position: absolute;
  top: 0;
  bottom: 0;
  box-shadow: inset -2px 0 0 var(--bg-secondary);
}

.segment.on,
.swatch.on {
  background: var(--color-warning, #f39c12);
}

/* Lighter than the strip's own background, so an "off" stretch reads as recorded rather than empty. */
.segment.off,
.swatch.off {
  background: color-mix(in srgb, var(--text-secondary) 30%, transparent);
}

/* Worked out from the radiators afterwards: hatched, so it never passes for the app's own record. */
.segment.on.inferred,
.swatch.on.inferred {
  background: repeating-linear-gradient(135deg, var(--color-warning, #f39c12) 0 5px, color-mix(in srgb, var(--color-warning, #f39c12) 45%, transparent) 5px 8px);
}

.segment.off.inferred,
.swatch.off.inferred {
  background: repeating-linear-gradient(135deg, color-mix(in srgb, var(--text-secondary) 30%, transparent) 0 5px, transparent 5px 8px);
}

.inferred-tag {
  color: var(--text-secondary);
}

.segment.thermostat,
.swatch.thermostat {
  background: repeating-linear-gradient(45deg, var(--bg-tertiary) 0 4px, var(--border-color) 4px 6px);
}

.segment.unknown,
.swatch.unknown {
  background: transparent;
  outline: 1px dashed var(--border-color);
  outline-offset: -1px;
}

.segment.hovered {
  filter: brightness(1.15);
}

.axis {
  position: relative;
  height: 1.4rem;
  margin-top: 0.25rem;
  font-size: 0.75rem;
  color: var(--text-secondary);
}

.tick {
  position: absolute;
  transform: translateX(-50%);
  white-space: nowrap;
}

.tick:first-child {
  transform: none;
}

.tick:last-child {
  transform: translateX(-100%);
}

.readout {
  margin: 0.25rem 0 0.75rem;
  min-height: 1.2em;
  font-size: 0.85rem;
  color: var(--text-primary);
}

.legend {
  list-style: none;
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem 1rem;
  margin: 0 0 1.25rem;
  padding: 0;
  font-size: 0.85rem;
  color: var(--text-secondary);
}

.legend-item {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
}

.swatch {
  width: 14px;
  height: 14px;
  border-radius: 3px;
  display: inline-block;
}

.section-title {
  margin: 0 0 0.5rem;
  font-size: 0.9rem;
  color: var(--text-primary);
}

.changes {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.85rem;
  margin-bottom: 1rem;
}

.changes th,
.changes td {
  text-align: left;
  padding: 0.35rem 0.5rem;
  border-bottom: 1px solid var(--border-color);
  color: var(--text-primary);
}

.changes th {
  color: var(--text-secondary);
  font-weight: 600;
}

.status-text {
  color: var(--text-secondary);
  font-size: 0.9rem;
}

.error-text {
  color: var(--color-danger, #e74c3c);
  font-size: 0.9rem;
}

.belief-note {
  margin: 0;
  font-size: 0.8rem;
  color: var(--text-secondary);
}

.boiler-actions {
  display: flex;
  padding: 1rem 1.5rem 1.25rem;
  border-top: 1px solid var(--border-color);
}

.btn {
  flex: 1;
  padding: 0.75rem;
  border: none;
  border-radius: 8px;
  font-size: 0.95rem;
  font-weight: 600;
  cursor: pointer;
}

.btn-secondary {
  background-color: var(--btn-secondary-bg);
  color: var(--text-primary);
}

@media (max-width: 600px) {
  .boiler-modal {
    border-radius: 0;
    max-height: 100vh;
    height: 100%;
    max-width: none;
  }

  .boiler-body {
    padding: 1rem;
  }
}
</style>

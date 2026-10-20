<template>
  <div class="rules-overlay" :style="{ zIndex }" @click.self="emit('cancel')">
    <div class="rules-modal" role="dialog" aria-modal="true" :aria-label="`Rules for ${deviceTitle}`">
      <div class="rules-header">
        <h3>Rules for {{ deviceTitle }}</h3>
        <button class="close-btn" aria-label="Close" :disabled="saving" @click="emit('cancel')">
          <svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor">
            <path d="M4.646 4.646a.5.5 0 01.708 0L10 9.293l4.646-4.647a.5.5 0 01.708.708L10.707 10l4.647 4.646a.5.5 0 01-.708.708L10 10.707l-4.646 4.647a.5.5 0 01-.708-.708L9.293 10 4.646 5.354a.5.5 0 010-.708z"/>
          </svg>
        </button>
      </div>

      <div class="rules-body">
        <p v-if="isDefault" class="rules-hint">
          These are the built-in defaults. Saving stores them, and your changes, as this house's own rules.
        </p>

        <section class="rules-section">
          <h4 class="section-title">Schedule</h4>
          <p class="section-help">The device may only be switched on within these times.</p>

          <p v-if="draft.timeWindows.length === 0" class="any-time">Any time</p>

          <div v-for="(timeWindow, index) in draft.timeWindows" :key="index" class="window">
            <div class="days-selector" role="group" :aria-label="`Days for window ${index + 1}`">
              <button
                v-for="day in DAYS"
                :key="day.value"
                type="button"
                class="day-btn"
                :class="{ active: (timeWindow.days & day.value) !== 0 }"
                :aria-pressed="(timeWindow.days & day.value) !== 0"
                :title="day.full"
                @click="toggleDay(timeWindow, day.value)"
              >
                {{ day.short }}
              </button>
            </div>

            <div class="window-row">
              <label class="time-field">
                <span>Start</span>
                <input v-model="timeWindow.start" type="time" required />
              </label>
              <label class="time-field">
                <span>End</span>
                <input v-model="timeWindow.end" type="time" required />
              </label>
              <button type="button" class="remove-btn" :aria-label="`Remove window ${index + 1}`" @click="removeWindow(index)">
                Remove
              </button>
            </div>

            <p v-if="wraps(timeWindow)" class="window-note">Ends the next day.</p>
            <p v-if="submitted && windowError(timeWindow)" class="error-text">{{ windowError(timeWindow) }}</p>
          </div>

          <button type="button" class="add-btn" @click="addWindow">Add time window</button>
        </section>

        <section class="rules-section">
          <h4 class="section-title">Presence</h4>
          <p class="section-help">Only switch on while someone is detected. Leave everything unticked to ignore presence.</p>

          <label v-for="option in presenceOptions" :key="option.value" class="check-row">
            <input
              type="checkbox"
              :checked="draft.presence.sources.includes(option.value)"
              @change="toggleSource(option.value)"
            />
            <span>{{ option.label }}</span>
          </label>

          <div v-if="draft.presence.sources.length >= 2" class="match-toggle" role="group" aria-label="How many must be in use">
            <button
              type="button"
              class="match-btn"
              :class="{ active: draft.presence.match === PRESENCE_MATCH.ANY }"
              @click="draft.presence.match = PRESENCE_MATCH.ANY"
            >
              Any of these
            </button>
            <button
              type="button"
              class="match-btn"
              :class="{ active: draft.presence.match === PRESENCE_MATCH.ALL }"
              @click="draft.presence.match = PRESENCE_MATCH.ALL"
            >
              All of these
            </button>
          </div>
        </section>

        <section class="rules-section">
          <h4 class="section-title">Pre-heat</h4>
          <p class="section-help">
            How long before a warmer schedule starts this device begins heating for it. Radiators warm a room
            slowly, so usually need longer than a heater.
          </p>
          <label class="pre-heat-field">
            <span class="visually-hidden">Pre-heat</span>
            <select v-model.number="draft.preHeatMinutes">
              <option v-if="isRadiator" :value="PRE_HEAT_AUTO">Auto (default)</option>
              <option v-for="minutes in PRE_HEAT_OPTIONS" :key="minutes" :value="minutes">
                {{ minutes === 0 ? 'None' : `${minutes} minutes` }}{{ !isRadiator && minutes === defaultPreHeat ? ' (default)' : '' }}
              </option>
            </select>
          </label>
          <p v-if="isRadiator && draft.preHeatMinutes === PRE_HEAT_AUTO" class="auto-help">
            {{ autoExplanation }}
          </p>
        </section>

        <p v-if="error" class="error-text" role="alert">{{ error }}</p>
      </div>

      <div class="rules-actions">
        <button type="button" class="btn btn-secondary" :disabled="saving" @click="emit('cancel')">Cancel</button>
        <button type="button" class="btn btn-primary" :disabled="saving" @click="handleSave">
          {{ saving ? 'Saving…' : 'Save' }}
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed, reactive, ref } from 'vue'
import {
  ALL_DAYS,
  DAYS,
  defaultPreHeatFor,
  HOUSE_SOURCE,
  isAutoPreHeat,
  PRE_HEAT_AUTO,
  PRE_HEAT_OPTIONS,
  PRESENCE_MATCH,
  normaliseRules,
  preHeatFor,
  validateWindow
} from '../utils/deviceRules.js'
import { useFormatting } from '../composables/useFormatting.js'
import { describeWarmUpRate, expectedWarmUp, formatMinutes, FALLBACK_WARM_UP_MINUTES } from '../utils/warmUp.js'

const { currentTempUnit } = useFormatting()

const props = defineProps({
  // Name to show in the title, already resolved from the device.
  deviceTitle: {
    type: String,
    required: true
  },
  // The rules to start from, or null for none.
  rules: {
    type: Object,
    default: null
  },
  // 'radiatorValve' or 'heaterPlug': decides the default pre-heat (Auto for a valve, none for a heater).
  deviceRole: {
    type: String,
    default: 'heaterPlug'
  },
  // The device as published in the room state, for what Auto amounts to: its measured warm-up rate and expected time.
  device: {
    type: Object,
    default: null
  },
  // True when `rules` are the built-in defaults rather than saved by the user.
  isDefault: {
    type: Boolean,
    default: false
  },
  // [{ name, label }] zones that can detect presence, already sorted.
  presenceZones: {
    type: Array,
    default: () => []
  },
  saving: {
    type: Boolean,
    default: false
  },
  error: {
    type: String,
    default: ''
  },
  // The parent modal's z-index plus 100.
  zIndex: {
    type: Number,
    default: 1100
  }
})

const emit = defineEmits(['save', 'cancel'])

const NEW_WINDOW = { days: ALL_DAYS, start: '08:00', end: '22:00' }

// A working copy, so cancelling leaves the saved rules untouched.
const draft = reactive({
  timeWindows: (props.rules?.timeWindows ?? []).map(w => ({ days: w.days, start: w.start, end: w.end })),
  presence: {
    match: props.rules?.presence?.match === PRESENCE_MATCH.ALL ? PRESENCE_MATCH.ALL : PRESENCE_MATCH.ANY,
    sources: [...(props.rules?.presence?.sources ?? [])]
  },
  // Auto for a radiator with none set; otherwise rounded to the 5-minute steps on offer, so a saved value always
  // appears selected.
  preHeatMinutes: isAutoPreHeat(props.rules, props.deviceRole)
    ? PRE_HEAT_AUTO
    : Math.round(preHeatFor(props.rules, props.deviceRole) / 5) * 5
})
const defaultPreHeat = computed(() => defaultPreHeatFor(props.deviceRole))
const isRadiator = computed(() => props.deviceRole === 'radiatorValve')

// What Auto means for this radiator right now: how it is worked out, what it has measured and the time that implies.
const autoExplanation = computed(() => {
  const rate = describeWarmUpRate(props.device, currentTempUnit.value)
  const warmUp = expectedWarmUp(props.device)
  const how = 'It starts early enough for the room to reach the new temperature as the schedule begins.'
  if (props.device?.heatingRateCelsiusPerHour == null) {
    return `${how} Nothing has been measured for this radiator yet, so it starts ${FALLBACK_WARM_UP_MINUTES} minutes ahead until it has.`
  }
  const expected = warmUp
    ? `That would take about ${formatMinutes(warmUp.minutes)} to reach the next warmer schedule from the room's temperature now.`
    : 'No warmer schedule is coming up, so there is nothing to warm up for.'
  return `${how} ${rate}. ${expected}`
})
const submitted = ref(false)

// House first, then each zone. A source the rules already name but that no zone offers any more is kept
// visible, so it can be unticked rather than silently carried along.
const presenceOptions = computed(() => {
  const known = new Set(props.presenceZones.map(z => z.name))
  const orphans = draft.presence.sources
    .filter(s => s !== HOUSE_SOURCE && !known.has(s))
    .map(s => ({ value: s, label: s }))
  return [
    { value: HOUSE_SOURCE, label: 'House' },
    ...props.presenceZones.map(z => ({ value: z.name, label: z.label })),
    ...orphans
  ]
})

const toggleDay = (timeWindow, dayValue) => {
  timeWindow.days = (timeWindow.days & dayValue) !== 0 ? timeWindow.days & ~dayValue : timeWindow.days | dayValue
}

const addWindow = () => {
  draft.timeWindows.push({ ...NEW_WINDOW })
}

const removeWindow = (index) => {
  draft.timeWindows.splice(index, 1)
}

const toggleSource = (source) => {
  const index = draft.presence.sources.indexOf(source)
  if (index === -1) {
    draft.presence.sources.push(source)
  } else {
    draft.presence.sources.splice(index, 1)
  }
}

// An end at or before the start runs past midnight; worth saying, since it is easy to get backwards.
const wraps = (timeWindow) => !!timeWindow.start && !!timeWindow.end && timeWindow.end <= timeWindow.start

const windowError = (timeWindow) => validateWindow(timeWindow)

const handleSave = () => {
  submitted.value = true
  if (draft.timeWindows.some(w => validateWindow(w) !== null)) return
  emit('save', normaliseRules(draft, props.deviceRole))
}
</script>

<style scoped>
.rules-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
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

.rules-modal {
  background: var(--bg-secondary);
  border-radius: 12px;
  box-shadow: 0 8px 32px var(--shadow-md);
  width: 100%;
  max-width: 480px;
  max-height: 90vh;
  display: flex;
  flex-direction: column;
  animation: slideUp 0.3s;
}

@keyframes slideUp {
  from { transform: translateY(20px); opacity: 0; }
  to { transform: translateY(0); opacity: 1; }
}

.rules-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 1.25rem 1.5rem;
  border-bottom: 1px solid var(--border-color);
}

.rules-header h3 {
  font-size: 1.25rem;
  font-weight: 600;
  color: var(--text-primary);
  margin: 0;
  overflow-wrap: anywhere;
}

.close-btn {
  background: none;
  border: none;
  cursor: pointer;
  color: var(--icon-color);
  padding: 0.25rem;
  display: flex;
  border-radius: 4px;
  flex: 0 0 auto;
}

.close-btn:hover {
  background-color: var(--hover-bg);
}

.rules-body {
  padding: 1rem 1.5rem;
  overflow-y: auto;
}

.rules-hint {
  margin: 0 0 1rem;
  padding: 0.5rem 0.75rem;
  font-size: 0.85rem;
  color: var(--text-secondary);
  background: var(--bg-primary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
}

.rules-section + .rules-section {
  margin-top: 1.25rem;
}

.section-title {
  margin: 0;
  font-size: 1rem;
  font-weight: 600;
  color: var(--text-primary);
}

.section-help {
  margin: 0.15rem 0 0.75rem;
  font-size: 0.85rem;
  color: var(--text-secondary);
}

.any-time {
  margin: 0 0 0.75rem;
  font-weight: 500;
  color: var(--text-primary);
}

.window {
  padding: 0.75rem;
  margin-bottom: 0.5rem;
  background: var(--bg-primary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
}

.days-selector {
  display: flex;
  gap: 0.35rem;
}

.day-btn {
  flex: 1;
  min-width: 0;
  padding: 0.5rem 0;
  border: 2px solid var(--border-color);
  background: var(--bg-secondary);
  color: var(--text-primary);
  border-radius: 6px;
  cursor: pointer;
  font-weight: 600;
  transition: all 0.2s;
}

.day-btn:hover {
  border-color: var(--color-primary);
  background-color: var(--hover-bg);
}

.day-btn.active {
  background-color: var(--color-primary);
  border-color: var(--color-primary);
  color: white;
}

.window-row {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: 0.75rem;
  margin-top: 0.75rem;
}

.time-field {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  flex: 1 1 7rem;
  font-size: 0.8rem;
  color: var(--text-secondary);
}

.time-field input {
  padding: 0.5rem;
  border: 1px solid var(--border-color);
  border-radius: 6px;
  background: var(--bg-secondary);
  color: var(--text-primary);
  font-size: 1rem;
}

.remove-btn,
.add-btn {
  padding: 0.5rem 0.75rem;
  border: 1px solid var(--border-color);
  border-radius: 6px;
  background: var(--bg-secondary);
  color: var(--text-primary);
  font-size: 0.9rem;
  cursor: pointer;
}

.remove-btn:hover,
.add-btn:hover {
  background-color: var(--hover-bg);
}

.window-note {
  margin: 0.5rem 0 0;
  font-size: 0.8rem;
  color: var(--text-secondary);
}

.error-text {
  margin: 0.5rem 0 0;
  font-size: 0.85rem;
  color: var(--color-danger, #e74c3c);
}

.check-row {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  padding: 0.4rem 0;
  color: var(--text-primary);
  cursor: pointer;
}

.check-row input {
  width: 1.1rem;
  height: 1.1rem;
  flex: 0 0 auto;
}

.match-toggle {
  display: flex;
  gap: 0.5rem;
  margin-top: 0.75rem;
}

.match-btn {
  flex: 1;
  padding: 0.6rem;
  border: 2px solid var(--border-color);
  background: var(--bg-secondary);
  color: var(--text-primary);
  border-radius: 6px;
  cursor: pointer;
  font-weight: 600;
}

.match-btn.active {
  background-color: var(--color-primary);
  border-color: var(--color-primary);
  color: white;
}

.auto-help {
  margin: 0.5rem 0 0;
  font-size: 0.85rem;
  color: var(--text-secondary);
}

.pre-heat-field select {
  width: 100%;
  padding: 0.5rem;
  border: 1px solid var(--border-color);
  border-radius: 6px;
  background: var(--bg-secondary);
  color: var(--text-primary);
  font-size: 1rem;
}

.visually-hidden {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip: rect(0 0 0 0);
  white-space: nowrap;
}

.rules-actions {
  display: flex;
  gap: 0.75rem;
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

.btn:disabled,
.close-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.btn-secondary {
  background-color: var(--btn-secondary-bg);
  color: var(--text-primary);
}

.btn-primary {
  background-color: var(--color-primary);
  color: white;
}

@media (max-width: 600px) {
  .rules-modal {
    border-radius: 0;
    max-height: 100vh;
    height: 100%;
    max-width: none;
  }

  .rules-body {
    padding: 1rem;
  }
}
</style>

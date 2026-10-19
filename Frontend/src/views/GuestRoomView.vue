<template>
  <main class="guest">
    <p v-if="loading" class="guest-message" role="status">Loading…</p>

    <section v-else-if="invalid" class="guest-card guest-invalid">
      <h1>This link is no longer valid</h1>
      <p>Ask whoever shared it for a new QR code.</p>
    </section>

    <section v-else-if="!room" class="guest-card guest-invalid">
      <h1>Couldn't load this page</h1>
      <p>Check your connection and try again.</p>
      <button type="button" class="btn btn-primary" @click="refresh">Try again</button>
    </section>

    <template v-else>
      <header class="guest-title">
        <span v-if="room.icon" class="guest-icon" aria-hidden="true">{{ room.icon }}</span>
        <h1>{{ room.name }}</h1>
      </header>

      <!-- One zone: the big numbers. A group: one row per zone it covers. -->
      <section v-if="zones.length === 1" class="guest-card" aria-label="Now">
        <dl class="facts">
          <div class="fact">
            <dt>Now</dt>
            <dd class="fact-value">{{ formatTemperature(zones[0].currentTemperature) }}</dd>
          </div>
          <div class="fact">
            <dt>Aiming for</dt>
            <dd class="fact-value" :class="{ overridden: zones[0].overridden }">{{ formatTemperature(zones[0].targetTemperature) }}</dd>
            <dd v-if="zones[0].overridden" class="target-source overridden">Overridden until {{ formatEndTime(zones[0].overrideEndTime) }}</dd>
            <dd v-else-if="zones[0].targetTemperature != null" class="target-source">Scheduled</dd>
          </div>
        </dl>
        <p class="heating-state" :class="{ on: zones[0].heatingActive }">
          <span class="heating-dot" aria-hidden="true"></span>
          {{ zones[0].heatingActive ? 'Heating is on' : 'Heating is off' }}
        </p>
      </section>

      <section v-else class="guest-card" aria-label="Zones in this group">
        <ul class="zone-list">
          <li v-for="zone in zones" :key="zone.name" class="zone-row">
            <span class="zone-name">
              <span v-if="zone.icon" aria-hidden="true">{{ zone.icon }} </span>{{ zone.name }}
            </span>
            <span class="zone-temps">
              {{ formatTemperature(zone.currentTemperature) }}
              <span class="zone-target" :class="{ overridden: zone.overridden }" :title="zone.overridden ? `Overridden until ${formatEndTime(zone.overrideEndTime)}` : 'Scheduled'">
                → {{ formatTemperature(zone.targetTemperature) }}<span v-if="zone.overridden" class="override-tag">overridden</span>
              </span>
            </span>
            <span class="heating-dot" :class="{ on: zone.heatingActive }" :aria-label="zone.heatingActive ? 'Heating' : 'Not heating'"></span>
          </li>
        </ul>
      </section>

      <!-- While a group around this zone or group is boosted, nothing inside it can be changed on its own. -->
      <section v-if="lockedBy" class="guest-card" aria-label="Boosted">
        <h2>Boosted with {{ lockedBy }}</h2>
        <p class="fine-print">
          The whole of {{ lockedBy }} is on a boost, so this can't be changed here until that boost ends.
        </p>
      </section>

      <section v-else-if="activeOverride" class="guest-card override-active" aria-label="Override">
        <h2>Override running</h2>
        <p class="override-summary">
          Holding <strong>{{ formatTemperature(activeOverride.temperature) }}</strong>
          until <strong>{{ formatEndTime(activeOverride.endTime) }}</strong>
        </p>
        <button type="button" class="btn btn-secondary btn-block" :disabled="busy" @click="cancel">
          {{ busy ? 'Working…' : 'Cancel override' }}
        </button>
      </section>

      <!-- Stays available while an override runs: starting another one changes it. -->
      <section v-if="!lockedBy" class="guest-card" :aria-label="activeOverride ? 'Change the override' : 'Start an override'">
        <h2>{{ activeOverride ? 'Change the override' : 'Change the temperature' }}</h2>

        <div class="field">
          <span id="temperature-label" class="field-label">Temperature</span>
          <div class="stepper" role="group" aria-labelledby="temperature-label">
            <button
              type="button"
              class="stepper-btn"
              aria-label="Lower the temperature"
              :disabled="temperature <= limits.minTemperature"
              @click="temperature = stepTemperature(temperature, -1, limits)"
            >−</button>
            <output class="stepper-value" aria-live="polite">{{ formatTemperature(temperature) }}</output>
            <button
              type="button"
              class="stepper-btn"
              aria-label="Raise the temperature"
              :disabled="temperature >= limits.maxTemperature"
              @click="temperature = stepTemperature(temperature, 1, limits)"
            >+</button>
          </div>
        </div>

        <div class="field">
          <label class="field-label" for="duration">For how long</label>
          <select id="duration" v-model.number="durationMinutes" class="duration-select">
            <option v-for="option in durations" :key="option.minutes" :value="option.minutes">{{ option.label }}</option>
          </select>
        </div>

        <button type="button" class="btn btn-primary btn-block" :disabled="busy" @click="start">
          {{ busy ? 'Working…' : (activeOverride ? 'Change override' : 'Start override') }}
        </button>
        <p class="fine-print">
          Between {{ limits.minTemperature }} and {{ limits.maxTemperature }} °C, for up to {{ formatDuration(limits.maxMinutes) }}{{ zones.length > 1 ? ', for every zone listed above' : '' }}.
          You can change or cancel it at any time, and the normal schedule carries on afterwards.
        </p>
      </section>

      <p v-if="actionError" class="guest-error" role="alert">{{ actionError }}</p>
    </template>
  </main>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { guestApi, InvalidLinkError } from '../services/guestApi.js'
import {
  DEFAULT_DURATION_MINUTES,
  DEFAULT_LIMITS,
  DEFAULT_TEMPERATURE,
  clampDuration,
  clampTemperature,
  durationOptions,
  formatDuration,
  formatEndTime,
  formatTemperature,
  isOverrideActive,
  stepTemperature,
  validateOverride
} from '../utils/guestOverride.js'

const REFRESH_MS = 30_000

const route = useRoute()
const token = String(route.params.token ?? '')

const room = ref(null)
const loading = ref(true)
const invalid = ref(false)
const busy = ref(false)
const actionError = ref('')

const temperature = ref(DEFAULT_TEMPERATURE)
const durationMinutes = ref(DEFAULT_DURATION_MINUTES)

const limits = computed(() => ({ ...DEFAULT_LIMITS, ...(room.value?.limits ?? {}) }))
const durations = computed(() => durationOptions(limits.value))
const activeOverride = computed(() => (isOverrideActive(room.value?.override) ? room.value.override : null))
// The group whose boost covers this zone or group, if any; the server refuses changes until it ends.
const lockedBy = computed(() => room.value?.lockedBy ?? null)
// The zones the link covers: one for a zone's QR code, several for a group's.
const zones = computed(() => room.value?.zones ?? [])

let timer = null
let formReady = false

// Fetches the zone. A failed refresh keeps showing the last good data rather than blanking the page.
const refresh = async () => {
  try {
    const next = await guestApi.getRoom(token)
    room.value = next
    invalid.value = false

    // Set the form up once, from the limits the server actually sent. Later refreshes leave it alone so
    // a half-edited form isn't reset every 30 seconds.
    if (!formReady) {
      // Start from a running override, so changing it is a nudge rather than a fresh entry.
      const running = isOverrideActive(next.override) ? next.override.temperature : DEFAULT_TEMPERATURE
      temperature.value = clampTemperature(running, limits.value)
      durationMinutes.value = clampDuration(DEFAULT_DURATION_MINUTES, limits.value)
      formReady = true
    }
    document.title = next.name
  } catch (error) {
    if (error instanceof InvalidLinkError) {
      invalid.value = true
      room.value = null
      stopRefreshing()
    } else {
      console.error('Error loading zone:', error)
    }
  } finally {
    loading.value = false
  }
}

const run = async (action) => {
  busy.value = true
  actionError.value = ''
  try {
    await action()
  } catch (error) {
    if (error instanceof InvalidLinkError) {
      invalid.value = true
      room.value = null
      stopRefreshing()
    } else {
      actionError.value = error?.message || 'Something went wrong. Try again.'
    }
    return
  } finally {
    busy.value = false
  }
  await refresh()
}

const start = () => {
  const problem = validateOverride(temperature.value, durationMinutes.value, limits.value)
  if (problem) {
    actionError.value = problem
    return
  }
  return run(() => guestApi.startOverride(token, temperature.value, durationMinutes.value))
}

const cancel = () => run(() => guestApi.cancelOverride(token))

const startRefreshing = () => {
  stopRefreshing()
  timer = setInterval(refresh, REFRESH_MS)
}

const stopRefreshing = () => {
  if (timer !== null) {
    clearInterval(timer)
    timer = null
  }
}

// A phone that has been in a pocket shows stale numbers; catch up as soon as the page is looked at again.
const onVisible = () => {
  if (document.visibilityState === 'visible' && !invalid.value) refresh()
}

onMounted(async () => {
  document.addEventListener('visibilitychange', onVisible)
  await refresh()
  if (!invalid.value) startRefreshing()
})

onBeforeUnmount(() => {
  stopRefreshing()
  document.removeEventListener('visibilitychange', onVisible)
})
</script>

<style scoped>
.guest {
  width: 100%;
  max-width: 28rem;
  margin: 0 auto;
  padding: 1.25rem 1rem 3rem;
  box-sizing: border-box;
  color: var(--text-primary);
}

.guest-message {
  margin: 4rem 0;
  text-align: center;
  color: var(--text-secondary);
}

.guest-title {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.5rem;
  margin: 0.5rem 0 1.25rem;
}

.guest-title h1 {
  margin: 0;
  font-size: 1.6rem;
  font-weight: 600;
  overflow-wrap: anywhere;
  text-align: center;
}

.guest-icon {
  font-size: 1.8rem;
}

.guest-card {
  background: var(--bg-secondary);
  border-radius: 12px;
  box-shadow: 0 2px 8px var(--shadow);
  padding: 1.25rem;
  margin-bottom: 1rem;
}

.guest-card h2 {
  margin: 0 0 1rem;
  font-size: 1.1rem;
  font-weight: 600;
}

.guest-invalid {
  margin-top: 3rem;
  text-align: center;
}

.guest-invalid h1 {
  margin: 0 0 0.5rem;
  font-size: 1.4rem;
}

.guest-invalid p {
  margin: 0 0 1rem;
  color: var(--text-secondary);
}

.facts {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1rem;
  margin: 0;
}

.fact dt {
  font-size: 0.85rem;
  color: var(--text-secondary);
}

.fact dd {
  margin: 0.15rem 0 0;
}

.fact-value {
  font-size: 2rem;
  font-weight: 600;
}

.heating-state {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin: 1rem 0 0;
  color: var(--text-secondary);
}

.heating-state.on {
  color: var(--color-warning);
  font-weight: 600;
}

.heating-dot {
  width: 0.6rem;
  height: 0.6rem;
  border-radius: 50%;
  background: currentColor;
}

/* A group: one row per zone, name left, temperature → target right, then whether it is heating. */
.zone-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.6rem;
}

.zone-row {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.zone-name {
  flex: 1 1 auto;
  min-width: 0;
  font-weight: 500;
  overflow-wrap: anywhere;
}

.zone-temps {
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}

.zone-target {
  color: var(--text-secondary);
}

/* An overridden target is the override's temperature, not the schedule's: shown in the boost colour and
   labelled, so nobody mistakes it for what the schedule asks for. */
.zone-target.overridden,
.fact-value.overridden {
  color: var(--color-warning);
}

.override-tag {
  margin-left: 0.35rem;
  padding: 0.05rem 0.4rem;
  border-radius: 999px;
  border: 1px solid var(--color-warning);
  font-size: 0.7rem;
  vertical-align: middle;
}

.target-source {
  margin: 0.15rem 0 0;
  font-size: 0.8rem;
  color: var(--text-secondary);
}

.target-source.overridden {
  color: var(--color-warning);
}

.zone-row .heating-dot {
  flex: 0 0 auto;
  color: var(--text-secondary);
  opacity: 0.5;
}

.zone-row .heating-dot.on {
  color: var(--color-warning);
  opacity: 1;
}

.field {
  margin-bottom: 1.25rem;
}

.field-label {
  display: block;
  margin-bottom: 0.5rem;
  font-size: 0.9rem;
  font-weight: 600;
  color: var(--text-secondary);
}

.stepper {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.stepper-value {
  flex: 1 1 auto;
  text-align: center;
  font-size: 2rem;
  font-weight: 600;
}

/* Big enough to hit with a thumb. */
.stepper-btn {
  flex: 0 0 auto;
  width: 3.5rem;
  height: 3.5rem;
  border: none;
  border-radius: 50%;
  background: var(--btn-secondary-bg);
  color: var(--text-primary);
  font-size: 1.75rem;
  line-height: 1;
  cursor: pointer;
}

.stepper-btn:active:not(:disabled) {
  background: var(--btn-secondary-hover);
}

.stepper-btn:disabled {
  opacity: 0.4;
  cursor: default;
}

.duration-select {
  width: 100%;
  min-height: 3rem;
  padding: 0 0.75rem;
  border: 1px solid var(--border-color);
  border-radius: 8px;
  background: var(--bg-primary);
  color: var(--text-primary);
  font-size: 1rem;
  font-family: inherit;
}

.override-summary {
  margin: 0 0 1rem;
  font-size: 1.05rem;
}

.btn {
  min-height: 3rem;
  padding: 0.75rem 1.25rem;
  border: none;
  border-radius: 8px;
  font-size: 1rem;
  font-weight: 600;
  font-family: inherit;
  cursor: pointer;
}

.btn:disabled {
  opacity: 0.6;
  cursor: default;
}

.btn-block {
  width: 100%;
}

.btn-primary {
  background: var(--color-primary);
  color: white;
}

.btn-primary:hover:not(:disabled) {
  background: var(--color-primary-hover);
}

.btn-secondary {
  background: var(--btn-secondary-bg);
  color: var(--text-primary);
}

.btn-secondary:hover:not(:disabled) {
  background: var(--btn-secondary-hover);
}

.fine-print {
  margin: 0.75rem 0 0;
  font-size: 0.8rem;
  color: var(--text-secondary);
  text-align: center;
}

.guest-error {
  margin: 0;
  padding: 0.75rem 1rem;
  border: 1px solid var(--color-danger);
  border-radius: 8px;
  color: var(--color-danger);
  font-size: 0.9rem;
}
</style>

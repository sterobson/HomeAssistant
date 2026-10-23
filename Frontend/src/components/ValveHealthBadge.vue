<template>
  <!-- A radiator valve the app has seen misbehaving (utils/valveHealth.js): a warning triangle and "Valve", or
       "2 valves". Opens the Devices list, where each finding is explained. -->
  <button
    type="button"
    class="valve-badge"
    :title="title"
    :aria-label="ariaLabel"
    @click.stop="emit('open')"
  >
    <svg width="14" height="14" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
      <path d="M8.982 1.566a1.13 1.13 0 00-1.96 0L.165 13.233c-.457.778.091 1.767.98 1.767h13.713c.889 0 1.438-.99.98-1.767L8.982 1.566zM8 5c.535 0 .954.462.9.995l-.35 3.507a.552.552 0 01-1.1 0L7.1 5.995A.905.905 0 018 5zm.002 6a1 1 0 110 2 1 1 0 010-2z"/>
    </svg>
    <span class="valve-badge-text">{{ text }}</span>
  </button>
</template>

<script setup>
import { computed } from 'vue'
import { deviceName } from '../utils/roomDevices.js'
import { badgeTitle } from '../utils/valveHealth.js'

const props = defineProps({
  // [{ device, issue }] from flaggedValveIssues() in utils/valveHealth.js; the badge is only shown when not empty.
  flagged: {
    type: Array,
    required: true
  }
})

const emit = defineEmits(['open'])

const valveCount = computed(() => new Set(props.flagged.map(({ device }) => device.entityId)).size)
const text = computed(() => (valveCount.value === 1 ? 'Valve' : `${valveCount.value} valves`))
const title = computed(() => `Radiator valve problem\n${badgeTitle(props.flagged, deviceName)}`)
const ariaLabel = computed(() => `Radiator valve problem: ${badgeTitle(props.flagged, deviceName).replaceAll('\n', '; ')}. Show devices`)
</script>

<style scoped>
.valve-badge {
  display: inline-flex;
  align-items: center;
  gap: 0.25rem;
  padding: 0.1rem 0.35rem;
  border: 1px solid currentColor;
  border-radius: 999px;
  background: none;
  color: var(--color-warning);
  font: inherit;
  font-size: 0.75rem;
  font-weight: 600;
  line-height: 1;
  white-space: nowrap;
  cursor: pointer;
}

.valve-badge:hover,
.valve-badge:focus-visible {
  background: color-mix(in srgb, var(--color-warning) 15%, transparent);
}
</style>

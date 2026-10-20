<template>
  <!-- A low battery: a battery outline filled to its level, and the percentage. Shown only for a battery at or
       below the warning level (utils/batteries.js); red at 10% or below. -->
  <span
    class="battery-badge"
    :class="{ critical }"
    :title="title"
    role="img"
    :aria-label="title"
  >
    <svg width="18" height="10" viewBox="0 0 18 10" aria-hidden="true">
      <rect x="0.5" y="0.5" width="15" height="9" rx="1.5" fill="none" stroke="currentColor" />
      <rect x="16" y="3" width="1.5" height="4" rx="0.5" fill="currentColor" />
      <rect x="2" y="2" :width="fillWidth" height="6" rx="0.5" fill="currentColor" />
    </svg>
    <span class="battery-percent">{{ percentText }}</span>
  </span>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({
  // { name, percent } from the app's battery list.
  battery: {
    type: Object,
    required: true
  },
  // Says how many more are low besides this one, for the tooltip.
  othersLow: {
    type: Number,
    default: 0
  }
})

const percent = computed(() => Math.max(0, Math.min(100, Number(props.battery.percent) || 0)))
const critical = computed(() => percent.value <= 10)
// At least a sliver, so an empty battery still reads as a battery.
const fillWidth = computed(() => Math.max(1, (percent.value / 100) * 12))
const percentText = computed(() => `${Math.round(percent.value)}%`)
const title = computed(() => {
  const more = props.othersLow > 0 ? ` (and ${props.othersLow} more low)` : ''
  return `Low battery: ${props.battery.name} at ${percentText.value}${more}`
})
</script>

<style scoped>
.battery-badge {
  display: inline-flex;
  align-items: center;
  gap: 0.25rem;
  color: var(--color-warning);
  font-size: 0.75rem;
  font-weight: 600;
  line-height: 1;
  white-space: nowrap;
}

.battery-badge.critical {
  color: var(--color-danger, #e74c3c);
}
</style>

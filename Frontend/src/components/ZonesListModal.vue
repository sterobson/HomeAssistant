<template>
  <div class="zones-list-overlay" :style="{ zIndex }" @click.self="emit('cancel')">
    <div class="zones-list-modal">
      <div class="zones-list-header">
        <h3>Zones</h3>
        <button class="close-btn" @click="emit('cancel')">
          <svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor">
            <path d="M4.646 4.646a.5.5 0 01.708 0L10 9.293l4.646-4.647a.5.5 0 01.708.708L10.707 10l4.647 4.646a.5.5 0 01-.708.708L10 10.707l-4.646 4.647a.5.5 0 01-.708-.708L9.293 10 4.646 5.354a.5.5 0 010-.708z"/>
          </svg>
        </button>
      </div>

      <div class="zones-list-body">
        <p v-if="items.length === 0" class="empty-state">No zones configured.</p>
        <button
          v-for="item in items"
          :key="item.rule.id"
          class="zone-item"
          :class="['action-' + item.rule.action]"
          @click="emit('edit', item.rule.id)"
        >
          <div class="zone-item-action">
            <span class="action-label">{{ actionLabel(item.rule.action) }} to {{ item.rule.targetPercent }}%</span>
            <span class="rule-type">{{ describeRuleType(item.rule) }}</span>
          </div>
          <div class="zone-item-times">
            <div v-if="item.times.length > 0" class="time-list">
              <span v-for="(t, i) in item.times" :key="i" class="time-chip">{{ t }}</span>
            </div>
            <div v-else class="not-active">Not active today</div>
          </div>
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed } from 'vue'
import { formatMinutesAsTime } from '../utils/time.js'

const props = defineProps({
  rules: {
    type: Array,
    required: true
  },
  resolvedZones: {
    type: Array,
    default: () => []
  },
  zIndex: {
    type: Number,
    default: 1000
  }
})

const emit = defineEmits(['cancel', 'edit'])

function actionLabel(action) {
  if (action === 'import') return 'Import'
  if (action === 'export') return 'Export'
  return action
}

const ruleTypeLabels = {
  'fixed-time': 'Fixed time',
  'export-exceeds-import': 'Export exceeds import',
  'import-exceeds-export': 'Import exceeds export',
  'local-extrema-boundary': 'Price extrema',
  'cheapest-import': 'Cheapest import',
  'cheapest-export': 'Cheapest export',
  'most-expensive-import': 'Most expensive import',
  'most-expensive-export': 'Most expensive export'
}

function describeEdge(time) {
  if (!time) return ''
  if (time.type === 'fixed-time') {
    return `Fixed (${formatMinutesAsTime(time.fixedTimeMinutes || 0)})`
  }
  return ruleTypeLabels[time.type] || time.type
}

function describeRuleType(rule) {
  const startDesc = describeEdge(rule.startTime)
  const endDesc = describeEdge(rule.endTime)
  if (startDesc === endDesc) return startDesc
  return `${startDesc} → ${endDesc}`
}

function formatRange(startMinutes, endMinutes) {
  const startStr = formatMinutesAsTime(startMinutes % 1440)
  const endStr = formatMinutesAsTime(endMinutes % 1440)
  return `${startStr} – ${endStr}`
}

const items = computed(() => {
  const zonesByRule = new Map()
  for (const zone of props.resolvedZones) {
    if (!zonesByRule.has(zone.ruleId)) zonesByRule.set(zone.ruleId, [])
    zonesByRule.get(zone.ruleId).push(zone)
  }
  return props.rules.map(rule => {
    const zones = (zonesByRule.get(rule.id) || []).slice().sort((a, b) => a.startMinutes - b.startMinutes)
    const times = zones.map(z => formatRange(z.startMinutes, z.endMinutes))
    return { rule, times }
  })
})
</script>

<style scoped>
.zones-list-overlay {
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
}

@keyframes fadeIn {
  from { opacity: 0; }
  to { opacity: 1; }
}

.zones-list-modal {
  background: var(--bg-secondary);
  border-radius: 12px;
  box-shadow: 0 8px 32px var(--shadow-md);
  width: 100%;
  max-width: 480px;
  max-height: 80vh;
  display: flex;
  flex-direction: column;
  animation: slideUp 0.2s;
}

@keyframes slideUp {
  from { transform: translateY(8px); opacity: 0; }
  to { transform: translateY(0); opacity: 1; }
}

.zones-list-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1rem 1.25rem;
  border-bottom: 1px solid var(--border-color);
}

.zones-list-header h3 {
  margin: 0;
  font-size: 1.1rem;
  font-weight: 600;
  color: var(--text-primary);
}

.close-btn {
  background: none;
  border: none;
  color: var(--text-secondary);
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 0.25rem;
  border-radius: 6px;
}

.close-btn:hover {
  background: var(--bg-tertiary);
  color: var(--text-primary);
}

.zones-list-body {
  overflow-y: auto;
  padding: 0.5rem;
}

.empty-state {
  text-align: center;
  color: var(--text-secondary);
  padding: 1.5rem;
  margin: 0;
  font-size: 0.9rem;
}

.zone-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
  width: 100%;
  background: var(--bg-tertiary);
  border: 1px solid var(--border-color);
  border-left: 4px solid var(--border-color);
  border-radius: 8px;
  padding: 0.6rem 0.9rem;
  margin: 0.35rem 0;
  cursor: pointer;
  text-align: left;
  color: var(--text-primary);
  transition: background 0.15s, border-color 0.15s, transform 0.05s;
}

.zone-item:hover {
  background: var(--bg-secondary);
}

.zone-item:active {
  transform: scale(0.99);
}

.zone-item.action-import {
  border-left-color: #e74c3c;
}

.zone-item.action-export {
  border-left-color: #27ae60;
}

.zone-item-action {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
  min-width: 0;
  flex: 1;
}

.action-label {
  font-weight: 600;
  font-size: 0.95rem;
}

.rule-type {
  color: var(--text-secondary);
  font-size: 0.78rem;
}

.zone-item-times {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 0.2rem;
  flex-shrink: 0;
}

.time-list {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 0.2rem;
}

.time-chip {
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 999px;
  padding: 0.1rem 0.55rem;
  font-size: 0.78rem;
  font-variant-numeric: tabular-nums;
  color: var(--text-primary);
}

.not-active {
  color: var(--text-secondary);
  font-style: italic;
  font-size: 0.8rem;
}
</style>

<template>
  <div class="batteries-overlay" :style="{ zIndex }" @click.self="emit('cancel')">
    <div class="batteries-modal" role="dialog" aria-modal="true" aria-label="Batteries">
      <div class="batteries-header">
        <h3>Batteries</h3>
        <button class="close-btn" aria-label="Close" @click="emit('cancel')">
          <svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor">
            <path d="M4.646 4.646a.5.5 0 01.708 0L10 9.293l4.646-4.647a.5.5 0 01.708.708L10.707 10l4.647 4.646a.5.5 0 01-.708.708L10 10.707l-4.646 4.647a.5.5 0 01-.708-.708L9.293 10 4.646 5.354a.5.5 0 010-.708z"/>
          </svg>
        </button>
      </div>

      <div class="batteries-body">
        <p class="hint">
          Every replaceable battery in the house. A battery warns at {{ LOW_BATTERY_PERCENT }}% or below, on the zone
          or group it belongs to. Radiator valves and thermometers belong to their zone. For the rest, choose where
          each Home Assistant area's devices show; anything left at "Top of the page" warns there.
        </p>

        <p v-if="areas.length === 0" class="empty">The house has not reported any batteries yet.</p>

        <section v-for="entry in areas" :key="entry.area ?? '(none)'" class="area">
          <div class="area-header">
            <h4 class="area-name">{{ entry.area ?? 'No area in Home Assistant' }}</h4>
            <select
              v-if="entry.area && entry.batteries.some(item => item.zones.length === 0)"
              v-model="draft[entry.area]"
              class="area-select"
              :aria-label="`Where ${entry.area}'s devices show`"
            >
              <option value="">Top of the page</option>
              <option v-for="target in targets" :key="target.value" :value="target.value">
                {{ '  '.repeat(target.depth * 2) }}{{ target.label }}
              </option>
            </select>
          </div>
          <p v-if="!entry.area" class="area-note">
            Shown at the top of the page. Give a device an area in Home Assistant to place it.
          </p>

          <ul class="battery-list">
            <li
              v-for="item in entry.batteries"
              :key="item.battery.entityId"
              class="battery"
              :class="{ low: isLow(item.battery), unavailable: item.battery.percent == null }"
            >
              <div class="battery-main">
                <span class="battery-name">{{ item.battery.name }}</span>
                <span class="battery-level">{{ levelText(item.battery) }}</span>
              </div>
              <div class="battery-where">{{ whereText(entry, item) }}</div>
            </li>
          </ul>
        </section>

        <p v-if="error" class="error">{{ error }}</p>
      </div>

      <div class="batteries-actions">
        <button type="button" class="btn btn-secondary" :disabled="saving" @click="emit('cancel')">Cancel</button>
        <button type="button" class="btn btn-primary" :disabled="saving || !changed" @click="save">
          {{ saving ? 'Saving…' : 'Save' }}
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed, reactive } from 'vue'
import { LOW_BATTERY_PERCENT, batteriesByArea, isLow } from '../utils/batteries.js'
import { roomLabel } from '../utils/rooms.js'

const props = defineProps({
  // [{ entityId, name, percent, area, deviceEntityIds }] from the app's room states.
  batteries: {
    type: Array,
    required: true
  },
  // Every zone (with its devices), to tell which batteries belong to a zone's heating.
  rooms: {
    type: Array,
    required: true
  },
  // [{ value, label, depth }] from placeTargets(): every zone and group, in layout order.
  targets: {
    type: Array,
    required: true
  },
  // The saved area -> place map.
  areaPlacement: {
    type: Object,
    default: () => ({})
  },
  saving: {
    type: Boolean,
    default: false
  },
  error: {
    type: String,
    default: ''
  },
  zIndex: {
    type: Number,
    default: 1000
  }
})

const emit = defineEmits(['save', 'cancel'])

const areas = computed(() => batteriesByArea(props.batteries, props.rooms))

// One choice per area, '' for the top of the page. A saved place that no longer exists starts at the top.
const validPlaces = computed(() => new Set(props.targets.map(target => target.value)))
const initial = Object.fromEntries(areas.value
  .filter(entry => entry.area)
  .map(entry => {
    const saved = props.areaPlacement?.[entry.area] ?? ''
    return [entry.area, validPlaces.value.has(saved) ? saved : '']
  }))
const draft = reactive({ ...initial })
const changed = computed(() => Object.keys(draft).some(area => draft[area] !== initial[area]))

const levelText = (battery) => (battery.percent == null ? 'Unavailable' : `${Math.round(battery.percent)}%`)

const targetLabel = (place) => props.targets.find(target => target.value === place)?.label

const whereText = (entry, item) => {
  if (item.zones.length > 0) return `On ${item.zones.map(roomLabel).join(' and ')}: its heating uses it`
  const place = entry.area ? draft[entry.area] : ''
  return place ? `On ${targetLabel(place)}` : 'At the top of the page'
}

// Keeps any saved areas that have no battery right now, so a device briefly gone from HA keeps its place.
const save = () => {
  emit('save', { ...(props.areaPlacement ?? {}), ...draft })
}
</script>

<style scoped>
.batteries-overlay {
  position: fixed;
  inset: 0;
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

.batteries-modal {
  background: var(--bg-secondary);
  border-radius: 12px;
  box-shadow: 0 8px 32px var(--shadow-md);
  width: 100%;
  max-width: 520px;
  max-height: 90vh;
  display: flex;
  flex-direction: column;
  animation: slideUp 0.3s;
}

@keyframes slideUp {
  from { transform: translateY(20px); opacity: 0; }
  to { transform: translateY(0); opacity: 1; }
}

.batteries-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1.25rem 1.5rem;
  border-bottom: 1px solid var(--border-color);
}

.batteries-header h3 {
  margin: 0;
  font-size: 1.25rem;
  font-weight: 600;
  color: var(--text-primary);
}

.close-btn {
  background: none;
  border: none;
  cursor: pointer;
  color: var(--icon-color);
  padding: 0.25rem;
  display: flex;
  border-radius: 4px;
}

.close-btn:hover {
  background-color: var(--hover-bg);
}

.batteries-body {
  padding: 1rem 1.5rem;
  overflow-y: auto;
}

.hint,
.empty,
.area-note {
  margin: 0 0 0.75rem;
  font-size: 0.85rem;
  color: var(--text-secondary);
}

.area + .area {
  margin-top: 1rem;
}

.area-header {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
  margin-bottom: 0.5rem;
}

.area-name {
  margin: 0;
  font-size: 0.95rem;
  font-weight: 600;
  color: var(--text-primary);
}

.area-select {
  flex: 0 1 14rem;
  min-width: 0;
  padding: 0.35rem 0.5rem;
  border: 1px solid var(--border-color);
  border-radius: 6px;
  background: var(--bg-primary);
  color: var(--text-primary);
  font-size: 0.85rem;
}

.battery-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}

.battery {
  padding: 0.5rem 0.75rem;
  background: var(--bg-primary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
}

.battery.low {
  border-color: var(--color-warning);
}

.battery.unavailable {
  border-color: var(--color-danger, #e74c3c);
}

.battery-main {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  column-gap: 0.75rem;
}

.battery-name {
  flex: 1 1 10rem;
  font-weight: 500;
  color: var(--text-primary);
  overflow-wrap: anywhere;
}

.battery-level {
  font-size: 0.9rem;
  color: var(--text-primary);
}

.battery.low .battery-level {
  color: var(--color-warning);
  font-weight: 600;
}

.battery.unavailable .battery-level {
  color: var(--color-danger, #e74c3c);
  font-weight: 600;
}

.battery-where {
  margin-top: 0.2rem;
  font-size: 0.75rem;
  color: var(--text-secondary);
}

.error {
  margin: 0.75rem 0 0;
  color: var(--color-danger, #e74c3c);
  font-size: 0.85rem;
}

.batteries-actions {
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

.btn:disabled {
  opacity: 0.6;
  cursor: default;
}

.btn-secondary {
  background-color: var(--btn-secondary-bg);
  color: var(--text-primary);
}

.btn-primary {
  background-color: var(--color-primary);
  color: #fff;
}

@media (max-width: 600px) {
  .batteries-modal {
    border-radius: 0;
    max-height: 100vh;
    height: 100%;
    max-width: none;
  }

  .batteries-body {
    padding: 1rem;
  }
}
</style>

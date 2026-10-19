<template>
  <div class="occupancy-overlay" :style="{ zIndex }" @click.self="emit('cancel')">
    <div class="occupancy-modal" role="dialog" aria-modal="true" :aria-label="`Occupancy for ${zoneTitle}`">
      <div class="occupancy-header">
        <h3>Occupancy for {{ zoneTitle }}</h3>
        <button class="close-btn" aria-label="Close" :disabled="saving" @click="emit('cancel')">
          <svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor">
            <path d="M4.646 4.646a.5.5 0 01.708 0L10 9.293l4.646-4.647a.5.5 0 01.708.708L10.707 10l4.647 4.646a.5.5 0 01-.708.708L10 10.707l-4.646 4.647a.5.5 0 01-.708-.708L9.293 10 4.646 5.354a.5.5 0 010-.708z"/>
          </svg>
        </button>
      </div>

      <div class="occupancy-body">
        <p v-if="isDefault" class="occupancy-hint">
          These are the built-in defaults. Saving stores them, and your change, as this house's own choice.
        </p>
        <p class="section-help">
          The zone counts as in use while any ticked detector is. Schedules use it for their "room in use" and
          "room not in use" tracks, and heaters and valves can require it in their rules.
        </p>

        <label v-for="detector in available" :key="detector.id" class="check-row">
          <input type="checkbox" :checked="draft.includes(detector.id)" @change="toggle(detector.id)" />
          <span class="detector">
            <span class="detector-label">{{ detector.label }}</span>
            <span class="detector-meta">
              <span :class="{ active: detector.active }">{{ detector.active ? 'On now' : 'Off now' }}</span>
              <span v-if="!detector.meansSomeoneHome"> · does not count towards House</span>
            </span>
          </span>
        </label>

        <p v-if="draft.length === 0" class="none-note">With nothing ticked this zone never counts as in use.</p>
        <p v-if="error" class="error-text" role="alert">{{ error }}</p>
      </div>

      <div class="occupancy-actions">
        <button type="button" class="btn btn-secondary" :disabled="saving" @click="emit('cancel')">Cancel</button>
        <button type="button" class="btn btn-primary" :disabled="saving" @click="emit('save', [...draft])">
          {{ saving ? 'Saving…' : 'Save' }}
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref } from 'vue'

const props = defineProps({
  zoneTitle: {
    type: String,
    required: true
  },
  // The detector ids that count for the zone now.
  detectorIds: {
    type: Array,
    default: () => []
  },
  // [{ id, label, meansSomeoneHome, active }] every detector the app offers.
  available: {
    type: Array,
    default: () => []
  },
  // True while nothing has been saved, so the choice is the app's built-in default.
  isDefault: {
    type: Boolean,
    default: false
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

// A working copy, so cancelling leaves the saved choice untouched. Kept in the offered order.
const draft = ref(props.available.map(d => d.id).filter(id => props.detectorIds.includes(id)))

const toggle = (id) => {
  const chosen = new Set(draft.value)
  if (chosen.has(id)) chosen.delete(id)
  else chosen.add(id)
  draft.value = props.available.map(d => d.id).filter(detectorId => chosen.has(detectorId))
}
</script>

<style scoped>
.occupancy-overlay {
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

.occupancy-modal {
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

.occupancy-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 1.25rem 1.5rem;
  border-bottom: 1px solid var(--border-color);
}

.occupancy-header h3 {
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

.occupancy-body {
  padding: 1rem 1.5rem;
  overflow-y: auto;
}

.occupancy-hint {
  margin: 0 0 1rem;
  padding: 0.5rem 0.75rem;
  font-size: 0.85rem;
  color: var(--text-secondary);
  background: var(--bg-primary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
}

.section-help {
  margin: 0 0 0.75rem;
  font-size: 0.85rem;
  color: var(--text-secondary);
}

.check-row {
  display: flex;
  align-items: flex-start;
  gap: 0.6rem;
  padding: 0.5rem 0;
  color: var(--text-primary);
  cursor: pointer;
}

.check-row input {
  width: 1.1rem;
  height: 1.1rem;
  margin-top: 0.15rem;
  flex: 0 0 auto;
}

.detector {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
}

.detector-meta {
  font-size: 0.8rem;
  color: var(--text-secondary);
}

.detector-meta .active {
  color: var(--color-success, #27ae60);
  font-weight: 600;
}

.none-note {
  margin: 0.5rem 0 0;
  font-size: 0.85rem;
  color: var(--text-secondary);
}

.error-text {
  margin: 0.5rem 0 0;
  font-size: 0.85rem;
  color: var(--color-danger, #e74c3c);
}

.occupancy-actions {
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
  .occupancy-modal {
    border-radius: 0;
    max-height: 100vh;
    height: 100%;
    max-width: none;
  }

  .occupancy-body {
    padding: 1rem;
  }
}
</style>

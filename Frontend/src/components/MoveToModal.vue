<template>
  <div class="move-overlay" :style="{ zIndex }" @click.self="emit('cancel')">
    <div class="move-modal">
      <div class="move-header">
        <h3>{{ title }}</h3>
        <button class="close-btn" aria-label="Close" @click="emit('cancel')">
          <svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor">
            <path d="M4.646 4.646a.5.5 0 01.708 0L10 9.293l4.646-4.647a.5.5 0 01.708.708L10.707 10l4.647 4.646a.5.5 0 01-.708.708L10 10.707l-4.646 4.647a.5.5 0 01-.708-.708L9.293 10 4.646 5.354a.5.5 0 010-.708z"/>
          </svg>
        </button>
      </div>

      <div class="move-body">
        <p class="move-hint">Choose where to put it. It goes at the end of that group.</p>
        <ul class="targets" role="listbox" aria-label="Destinations">
          <li v-for="target in targets" :key="target.id ?? 'top'">
            <button
              type="button"
              class="target"
              :class="{ current: target.current }"
              :style="{ paddingLeft: `${0.75 + target.depth * 1.25}rem` }"
              :disabled="target.current"
              role="option"
              :aria-selected="target.current"
              @click="emit('select', target.id)"
            >
              <span v-if="target.icon" class="target-icon" aria-hidden="true">{{ target.icon }}</span>
              <span class="target-name">{{ target.name }}</span>
              <span v-if="target.current" class="target-current">Already here</span>
            </button>
          </li>
        </ul>
      </div>

      <div class="move-actions">
        <button type="button" class="btn btn-secondary" @click="emit('cancel')">Cancel</button>
      </div>
    </div>
  </div>
</template>

<script setup>
defineProps({
  title: {
    type: String,
    default: 'Move to'
  },
  // [{ id: string|null, name, icon, depth, current }] from moveTargets() in utils/roomLayout.js
  targets: {
    type: Array,
    required: true
  },
  zIndex: {
    type: Number,
    default: 1000
  }
})

const emit = defineEmits(['select', 'cancel'])
</script>

<style scoped>
.move-overlay {
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

.move-modal {
  background: var(--bg-secondary);
  border-radius: 12px;
  box-shadow: 0 8px 32px var(--shadow-md);
  width: 100%;
  max-width: 420px;
  max-height: 90vh;
  display: flex;
  flex-direction: column;
  animation: slideUp 0.3s;
}

@keyframes slideUp {
  from { transform: translateY(20px); opacity: 0; }
  to { transform: translateY(0); opacity: 1; }
}

.move-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1.25rem 1.5rem;
  border-bottom: 1px solid var(--border-color);
}

.move-header h3 {
  font-size: 1.25rem;
  font-weight: 600;
  color: var(--text-primary);
  margin: 0;
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

.move-body {
  padding: 1rem 1.5rem;
  overflow-y: auto;
}

.move-hint {
  margin: 0 0 0.75rem;
  font-size: 0.85rem;
  color: var(--text-secondary);
}

.targets {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.target {
  width: 100%;
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.75rem;
  background: var(--bg-primary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  color: var(--text-primary);
  font-size: 1rem;
  font-family: inherit;
  text-align: left;
  cursor: pointer;
  transition: background-color 0.15s;
}

.target:hover:not(:disabled) {
  background-color: var(--hover-bg);
  border-color: var(--color-primary);
}

.target:disabled {
  opacity: 0.55;
  cursor: default;
}

.target-name {
  flex: 1;
  font-weight: 500;
}

.target-current {
  font-size: 0.75rem;
  color: var(--text-secondary);
}

.move-actions {
  padding: 1rem 1.5rem 1.25rem;
  border-top: 1px solid var(--border-color);
}

.btn {
  width: 100%;
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
  .move-modal {
    border-radius: 0;
    max-height: 100vh;
    height: 100%;
    max-width: none;
  }
}
</style>

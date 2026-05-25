<template>
  <div class="modal-overlay" @click.self="$emit('close')">
    <div class="modal-panel">
      <div class="modal-header">
        <h3>Rules for {{ device.name }}</h3>
        <button class="btn-close" @click="$emit('close')">
          <svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor">
            <path d="M2.146 2.854a.5.5 0 1 1 .708-.708L8 7.293l5.146-5.147a.5.5 0 0 1 .708.708L8.707 8l5.147 5.146a.5.5 0 0 1-.708.708L8 8.707l-5.146 5.147a.5.5 0 0 1-.708-.708L7.293 8z"/>
          </svg>
        </button>
      </div>

      <div class="modal-body">
        <!-- Combinator toggle -->
        <div v-if="device.ruleIds.length > 1" class="combinator-toggle">
          <span class="combinator-label">Combine rules with:</span>
          <div class="toggle-group">
            <button
              class="toggle-btn"
              :class="{ active: device.ruleCombinator === 0 }"
              @click="$emit('set-combinator', { combinator: 0 })"
            >AND</button>
            <button
              class="toggle-btn"
              :class="{ active: device.ruleCombinator === 1 }"
              @click="$emit('set-combinator', { combinator: 1 })"
            >OR</button>
          </div>
        </div>

        <!-- Bound rules -->
        <div v-if="boundRules.length > 0" class="section">
          <div class="section-label">Bound rules</div>
          <div v-for="rule in boundRules" :key="rule.id" class="rule-row">
            <div class="rule-info">
              <span class="rule-type-badge" :class="'type-' + rule.type">{{ rule.type }}</span>
              <span class="rule-name">{{ rule.name }}</span>
            </div>
            <button class="btn-unbind" @click="$emit('unbind', { ruleId: rule.id })">Unbind</button>
          </div>
        </div>

        <!-- Available rules -->
        <div v-if="availableRules.length > 0" class="section">
          <div class="section-label">Available rules</div>
          <div v-for="rule in availableRules" :key="rule.id" class="rule-row">
            <div class="rule-info">
              <span class="rule-type-badge" :class="'type-' + rule.type">{{ rule.type }}</span>
              <span class="rule-name">{{ rule.name }}</span>
            </div>
            <button class="btn-bind" @click="$emit('bind', { ruleId: rule.id })">Bind</button>
          </div>
        </div>

        <div v-if="rules.length === 0" class="empty-state">
          <p>No rules defined yet</p>
        </div>
      </div>

      <div class="modal-footer">
        <button class="btn btn-secondary" @click="$emit('add-rule')">
          <svg width="14" height="14" viewBox="0 0 16 16" fill="currentColor">
            <path d="M8 4a.5.5 0 0 1 .5.5v3h3a.5.5 0 0 1 0 1h-3v3a.5.5 0 0 1-1 0v-3h-3a.5.5 0 0 1 0-1h3v-3A.5.5 0 0 1 8 4"/>
          </svg>
          New rule
        </button>
        <button class="btn btn-primary" @click="$emit('close')">Done</button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({
  device: {
    type: Object,
    required: true
  },
  roomId: {
    type: String,
    required: true
  },
  rules: {
    type: Array,
    default: () => []
  }
})

defineEmits(['close', 'bind', 'unbind', 'set-combinator', 'add-rule'])

const boundRules = computed(() => {
  return props.rules.filter(r => props.device.ruleIds.includes(r.id))
})

const availableRules = computed(() => {
  return props.rules.filter(r => !props.device.ruleIds.includes(r.id))
})
</script>

<style scoped>
.modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.5);
  z-index: 1100;
  display: flex;
  align-items: center;
  justify-content: center;
  animation: fadeIn 0.2s;
}

.modal-panel {
  background: var(--bg-primary);
  border-radius: 12px;
  width: 90%;
  max-width: 450px;
  max-height: 80vh;
  display: flex;
  flex-direction: column;
  animation: slideUp 0.2s;
}

.modal-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1rem 1.25rem;
  border-bottom: 1px solid var(--border-color);
}

.modal-header h3 {
  margin: 0;
  font-size: 1.1rem;
  font-weight: 600;
  color: var(--text-primary);
}

.btn-close {
  background: none;
  border: none;
  color: var(--text-tertiary);
  cursor: pointer;
  padding: 0.25rem;
  border-radius: 4px;
}

.btn-close:hover {
  color: var(--text-primary);
}

.modal-body {
  padding: 1.25rem;
  overflow-y: auto;
  flex: 1;
}

.combinator-toggle {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.6rem 0.75rem;
  background: var(--bg-secondary);
  border-radius: 8px;
  margin-bottom: 1rem;
}

.combinator-label {
  font-size: 0.85rem;
  color: var(--text-secondary);
}

.toggle-group {
  display: flex;
  gap: 0;
  border: 1px solid var(--border-color);
  border-radius: 6px;
  overflow: hidden;
}

.toggle-btn {
  padding: 0.3rem 0.75rem;
  background: var(--bg-primary);
  border: none;
  color: var(--text-secondary);
  font-size: 0.8rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.15s;
}

.toggle-btn.active {
  background: var(--color-primary);
  color: white;
}

.section {
  margin-bottom: 1rem;
}

.section-label {
  font-size: 0.75rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--text-tertiary);
  margin-bottom: 0.4rem;
}

.rule-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.5rem 0.6rem;
  border-radius: 6px;
  margin-bottom: 0.3rem;
  background: var(--bg-secondary);
}

.rule-info {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  min-width: 0;
}

.rule-type-badge {
  font-size: 0.65rem;
  font-weight: 600;
  text-transform: uppercase;
  padding: 0.1rem 0.35rem;
  border-radius: 4px;
  flex-shrink: 0;
}

.type-presence { background: #2d6a4f20; color: #2d6a4f; }
.type-power { background: #e8590720; color: #e85907; }
.type-time { background: #1971c220; color: #1971c2; }

.rule-name {
  font-size: 0.85rem;
  color: var(--text-primary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.btn-bind, .btn-unbind {
  padding: 0.25rem 0.6rem;
  border-radius: 4px;
  font-size: 0.75rem;
  font-weight: 500;
  cursor: pointer;
  border: none;
  flex-shrink: 0;
}

.btn-bind {
  background: var(--color-primary);
  color: white;
}

.btn-bind:hover {
  background: var(--color-primary-hover);
}

.btn-unbind {
  background: none;
  color: var(--color-danger);
  border: 1px solid var(--color-danger);
}

.btn-unbind:hover {
  background: #c9252510;
}

.empty-state {
  text-align: center;
  padding: 1.5rem;
  color: var(--text-tertiary);
}

.modal-footer {
  display: flex;
  justify-content: space-between;
  padding: 1rem 1.25rem;
  border-top: 1px solid var(--border-color);
}

.btn {
  padding: 0.5rem 1rem;
  border: none;
  border-radius: 6px;
  font-size: 0.9rem;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.15s;
  display: flex;
  align-items: center;
  gap: 0.4rem;
}

.btn-primary {
  background: var(--color-primary);
  color: white;
}

.btn-primary:hover {
  background: var(--color-primary-hover);
}

.btn-secondary {
  background: var(--bg-tertiary);
  color: var(--text-primary);
}

@keyframes fadeIn {
  from { opacity: 0; }
  to { opacity: 1; }
}

@keyframes slideUp {
  from { transform: translateY(20px); opacity: 0; }
  to { transform: translateY(0); opacity: 1; }
}
</style>

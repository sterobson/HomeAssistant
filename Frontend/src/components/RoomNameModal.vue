<template>
  <div class="rename-overlay" :style="{ zIndex }" @click.self="handleCancel">
    <div class="rename-modal">
      <div class="rename-header">
        <h3>Rename room</h3>
        <button class="close-btn" @click="handleCancel">
          <svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor">
            <path d="M4.646 4.646a.5.5 0 01.708 0L10 9.293l4.646-4.647a.5.5 0 01.708.708L10.707 10l4.647 4.646a.5.5 0 01-.708.708L10 10.707l-4.646 4.647a.5.5 0 01-.708-.708L9.293 10 4.646 5.354a.5.5 0 010-.708z"/>
          </svg>
        </button>
      </div>

      <form class="rename-form" @submit.prevent="handleSubmit">
        <div class="form-group">
          <label for="display-name">Display name</label>
          <input
            id="display-name"
            ref="inputRef"
            v-model="displayName"
            type="text"
            maxlength="60"
            autocomplete="off"
            :placeholder="canonicalName"
          />
          <p class="field-hint">
            Shown in the app only. This room stays wired to its radiator and sensors as
            <strong>{{ canonicalName }}</strong>, so renaming it here is always safe.
          </p>
        </div>

        <div class="form-actions">
          <button type="button" class="btn btn-secondary" @click="handleCancel">
            Cancel
          </button>
          <button
            type="button"
            class="btn btn-secondary"
            :disabled="!hasDisplayName"
            @click="handleReset"
          >
            Reset
          </button>
          <button type="submit" class="btn btn-primary" :disabled="!hasChanged">
            Save
          </button>
        </div>
      </form>
    </div>
  </div>
</template>

<script setup>
import { computed, nextTick, onMounted, ref } from 'vue'

const props = defineProps({
  canonicalName: {
    type: String,
    required: true
  },
  currentDisplayName: {
    type: String,
    default: ''
  },
  zIndex: {
    type: Number,
    default: 1000
  }
})

const emit = defineEmits(['save', 'cancel'])

const displayName = ref(props.currentDisplayName ?? '')
const inputRef = ref(null)

// An empty display name means "fall back to the canonical name", which is also how a
// reset is expressed, so both paths emit the same empty string.
const trimmedDisplayName = computed(() => displayName.value.trim())
const hasDisplayName = computed(() => trimmedDisplayName.value.length > 0)
const hasChanged = computed(() => trimmedDisplayName.value !== (props.currentDisplayName ?? '').trim())

onMounted(async () => {
  await nextTick()
  inputRef.value?.focus()
  inputRef.value?.select()
})

const handleSubmit = () => {
  emit('save', trimmedDisplayName.value)
}

const handleReset = () => {
  displayName.value = ''
  emit('save', '')
}

const handleCancel = () => {
  emit('cancel')
}
</script>

<style scoped>
.rename-overlay {
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

.rename-modal {
  background: var(--bg-secondary);
  border-radius: 12px;
  box-shadow: 0 8px 32px var(--shadow-md);
  width: 100%;
  max-width: 450px;
  max-height: 90vh;
  overflow-y: auto;
  animation: slideUp 0.3s;
}

@keyframes slideUp {
  from {
    transform: translateY(20px);
    opacity: 0;
  }
  to {
    transform: translateY(0);
    opacity: 1;
  }
}

.rename-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1.5rem;
  border-bottom: 1px solid var(--border-color);
}

.rename-header h3 {
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
  align-items: center;
  justify-content: center;
  border-radius: 4px;
  transition: all 0.2s;
}

.close-btn:hover {
  background-color: var(--hover-bg);
}

.rename-form {
  padding: 1.5rem;
}

.form-group {
  margin-bottom: 1.5rem;
}

.form-group label {
  display: block;
  margin-bottom: 0.75rem;
  font-weight: 500;
  color: var(--text-primary);
  font-size: 0.9rem;
}

.form-group input {
  width: 100%;
  padding: 0.75rem;
  border: 1px solid var(--border-color);
  border-radius: 8px;
  background-color: var(--bg-primary);
  color: var(--text-primary);
  font-size: 1rem;
  font-family: inherit;
  box-sizing: border-box;
}

.form-group input:focus {
  outline: none;
  border-color: var(--color-primary);
}

.field-hint {
  margin: 0.75rem 0 0;
  font-size: 0.8rem;
  line-height: 1.4;
  color: var(--text-secondary);
}

.form-actions {
  display: flex;
  gap: 0.75rem;
}

.btn {
  flex: 1;
  padding: 0.875rem 1.5rem;
  border: none;
  border-radius: 8px;
  font-size: 1rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.5rem;
}

.btn-secondary {
  background-color: var(--btn-secondary-bg);
  color: var(--text-primary);
}

.btn-secondary:hover:not(:disabled) {
  background-color: var(--btn-secondary-hover);
}

.btn-primary {
  background-color: var(--color-primary);
  color: white;
}

.btn-primary:hover:not(:disabled) {
  transform: translateY(-1px);
}

.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.btn:active:not(:disabled) {
  transform: scale(0.98);
}

@media (max-width: 600px) {
  .rename-modal {
    border-radius: 0;
    max-height: 100vh;
  }
}
</style>

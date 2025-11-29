<template>
  <div v-if="modelValue" class="modal-overlay" @click.self="handleCancel">
    <div class="modal-content">
      <div class="modal-header">
        <h2>🏠 Connect to Your House</h2>
        <p class="modal-subtitle">Enter your House ID to get started</p>
      </div>

      <div class="modal-body">
        <div class="input-group">
          <label for="houseId">House ID</label>
          <input
            id="houseId"
            v-model="houseIdInput"
            type="text"
            placeholder="Enter your House ID"
            class="house-id-input"
            :class="{ error: hasError }"
            @keyup.enter="handleSubmit"
            @input="hasError = false"
            autocomplete="off"
            spellcheck="false"
          />
          <p v-if="hasError" class="error-message">{{ errorMessage }}</p>
          <p class="help-text">
            This can be a 10-character code (e.g., ABC1234XYZ) or a GUID.
          </p>
        </div>
      </div>

      <div class="modal-footer">
        <button
          v-if="allowCancel"
          @click="handleCancel"
          class="btn btn-secondary"
        >
          Cancel
        </button>
        <button
          @click="handleSubmit"
          class="btn btn-primary"
          :disabled="!houseIdInput.trim()"
        >
          Connect
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, watch } from 'vue'

const props = defineProps({
  modelValue: {
    type: Boolean,
    required: true
  },
  allowCancel: {
    type: Boolean,
    default: false
  }
})

const emit = defineEmits(['update:modelValue', 'submit'])

const houseIdInput = ref('')
const hasError = ref(false)
const errorMessage = ref('')

// Reset input when modal opens
watch(() => props.modelValue, (newValue) => {
  if (newValue) {
    houseIdInput.value = ''
    hasError.value = false
    errorMessage.value = ''
  }
})

async function handleSubmit() {
  const trimmedId = houseIdInput.value.trim()

  if (!trimmedId) {
    hasError.value = true
    errorMessage.value = 'House ID cannot be empty'
    return
  }

  // Basic validation - allow alphanumeric, hyphens (for GUIDs), and uppercase
  const validPattern = /^[A-Z0-9-]+$/i
  if (!validPattern.test(trimmedId)) {
    hasError.value = true
    errorMessage.value = 'House ID can only contain letters, numbers, and hyphens'
    return
  }

  try {
    // Call parent's submit handler (which validates with API)
    await emit('submit', trimmedId)
    emit('update:modelValue', false)
  } catch (error) {
    // Validation failed - show error
    hasError.value = true
    errorMessage.value = error.message || 'Invalid House ID. No schedules found for this house.'
  }
}

function handleCancel() {
  if (props.allowCancel) {
    emit('update:modelValue', false)
  }
}
</script>

<style scoped>
.modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background-color: rgba(0, 0, 0, 0.7);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
  padding: 1rem;
}

.modal-content {
  background: var(--bg-primary, #ffffff);
  border-radius: 12px;
  max-width: 500px;
  width: 100%;
  box-shadow: 0 20px 60px rgba(0, 0, 0, 0.5);
  animation: modalSlideIn 0.3s ease-out;
}

@media (prefers-color-scheme: dark) {
  .modal-content {
    background: var(--bg-primary, #1e1e1e);
  }
}

@keyframes modalSlideIn {
  from {
    opacity: 0;
    transform: translateY(-20px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

.modal-header {
  padding: 2rem 2rem 1rem;
  text-align: center;
}

.modal-header h2 {
  margin: 0 0 0.5rem;
  font-size: 1.5rem;
  color: var(--text-primary, #1f2937);
}

.modal-subtitle {
  margin: 0;
  color: var(--text-secondary, #6b7280);
  font-size: 0.95rem;
}

@media (prefers-color-scheme: dark) {
  .modal-header h2 {
    color: var(--text-primary, #f9fafb);
  }

  .modal-subtitle {
    color: var(--text-secondary, #9ca3af);
  }
}

.modal-body {
  padding: 1rem 2rem 2rem;
}

.input-group {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.input-group label {
  font-weight: 500;
  color: var(--text-primary, #1f2937);
  font-size: 0.9rem;
}

@media (prefers-color-scheme: dark) {
  .input-group label {
    color: var(--text-primary, #f9fafb);
  }
}

.house-id-input {
  padding: 0.75rem 1rem;
  font-size: 1rem;
  font-family: 'Courier New', monospace;
  border: 2px solid var(--border-color, #d1d5db);
  border-radius: 8px;
  background: var(--bg-primary, #f9fafb);
  color: var(--text-primary, #1f2937);
  transition: all 0.2s;
}

@media (prefers-color-scheme: dark) {
  .house-id-input {
    background: var(--bg-primary, #2d2d2d);
    color: var(--text-primary, #e5e7eb);
    border-color: var(--border-color, #4b5563);
  }
}

.house-id-input:focus {
  outline: none;
  border-color: var(--accent-color);
  box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.1);
}

.house-id-input.error {
  border-color: #ef4444;
}

.house-id-input.error:focus {
  box-shadow: 0 0 0 3px rgba(239, 68, 68, 0.1);
}

.error-message {
  margin: 0;
  color: #ef4444;
  font-size: 0.85rem;
}

.help-text {
  margin: 0;
  color: var(--text-secondary, #6b7280);
  font-size: 0.85rem;
}

@media (prefers-color-scheme: dark) {
  .help-text {
    color: var(--text-secondary, #9ca3af);
  }
}

.modal-footer {
  padding: 1rem 2rem 2rem;
  display: flex;
  gap: 1rem;
  justify-content: flex-end;
}

.btn {
  padding: 0.75rem 1.5rem;
  border: none;
  border-radius: 8px;
  font-size: 1rem;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s;
}

.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.btn-primary {
  background: var(--accent-color, #3b82f6);
  color: white;
}

.btn-primary:hover:not(:disabled) {
  background: var(--accent-hover, #2563eb);
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(59, 130, 246, 0.3);
}

.btn-secondary {
  background: transparent;
  color: var(--text-secondary, #6b7280);
  border: 2px solid var(--border-color, #d1d5db);
}

.btn-secondary:hover {
  background: var(--hover-bg, #f3f4f6);
  color: var(--text-primary, #1f2937);
}

@media (prefers-color-scheme: dark) {
  .btn-secondary {
    color: var(--text-secondary, #9ca3af);
    border-color: var(--border-color, #4b5563);
  }

  .btn-secondary:hover {
    background: var(--hover-bg, #374151);
    color: var(--text-primary, #f9fafb);
  }
}

@media (max-width: 600px) {
  .modal-content {
    margin: 1rem;
  }

  .modal-header {
    padding: 1.5rem 1.5rem 0.75rem;
  }

  .modal-body {
    padding: 0.75rem 1.5rem 1.5rem;
  }

  .modal-footer {
    padding: 0.75rem 1.5rem 1.5rem;
    flex-direction: column-reverse;
  }

  .btn {
    width: 100%;
  }
}
</style>

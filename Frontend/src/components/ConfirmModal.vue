<template>
  <div class="confirm-overlay" @click.self="handleCancel">
    <div class="confirm-modal">
      <div class="confirm-icon">
        <svg width="48" height="48" viewBox="0 0 16 16" fill="currentColor">
          <path d="M8 0a8 8 0 100 16A8 8 0 008 0zm.93 4.588l-2.29.287-.082.38.45.083c.294.07.352.176.288.469l-.738 3.468c-.194.897.105 1.319.808 1.319.545 0 1.178-.252 1.465-.598l.088-.416c-.2.176-.492.246-.686.246-.275 0-.375-.193-.304-.533l.816-3.833z"/>
          <circle cx="8" cy="2.5" r="1"/>
        </svg>
      </div>

      <div class="confirm-content">
        <h3 class="confirm-title">{{ title }}</h3>
        <p class="confirm-message">{{ message }}</p>
      </div>

      <div class="confirm-actions">
        <button type="button" class="btn btn-cancel" @click="handleCancel">
          {{ cancelText }}
        </button>
        <button type="button" class="btn btn-confirm" @click="handleConfirm">
          {{ confirmText }}
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
const props = defineProps({
  title: {
    type: String,
    default: 'Confirm Action'
  },
  message: {
    type: String,
    default: 'Are you sure you want to proceed?'
  },
  confirmText: {
    type: String,
    default: 'Confirm'
  },
  cancelText: {
    type: String,
    default: 'Cancel'
  }
})

const emit = defineEmits(['confirm', 'cancel'])

const handleConfirm = () => {
  emit('confirm')
}

const handleCancel = () => {
  emit('cancel')
}
</script>

<style scoped>
.confirm-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background-color: var(--overlay);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
  padding: 1rem;
  animation: fadeIn 0.2s;
}

@keyframes fadeIn {
  from { opacity: 0; }
  to { opacity: 1; }
}

.confirm-modal {
  background: var(--bg-secondary);
  border-radius: 12px;
  box-shadow: 0 8px 32px var(--shadow-md);
  width: 100%;
  max-width: 400px;
  animation: slideUp 0.3s;
  padding: 2rem;
  text-align: center;
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

.confirm-icon {
  display: flex;
  justify-content: center;
  margin-bottom: 1.5rem;
}

.confirm-icon svg {
  color: var(--color-warning);
}

.confirm-content {
  margin-bottom: 2rem;
}

.confirm-title {
  font-size: 1.25rem;
  font-weight: 600;
  color: var(--text-primary);
  margin: 0 0 0.75rem 0;
}

.confirm-message {
  font-size: 0.95rem;
  color: var(--text-secondary);
  line-height: 1.5;
  margin: 0;
}

.confirm-actions {
  display: flex;
  gap: 0.75rem;
}

.btn {
  flex: 1;
  padding: 0.75rem 1.5rem;
  border: none;
  border-radius: 8px;
  font-size: 1rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s;
}

.btn-cancel {
  background-color: var(--btn-secondary-bg);
  color: var(--text-primary);
}

.btn-cancel:hover {
  background-color: var(--btn-secondary-hover);
}

.btn-confirm {
  background-color: var(--color-danger);
  color: white;
}

.btn-confirm:hover {
  background-color: var(--color-danger-hover);
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(231, 76, 60, 0.3);
}

.btn:active {
  transform: scale(0.98);
}

@media (max-width: 600px) {
  .confirm-modal {
    padding: 1.5rem;
  }

  .confirm-actions {
    flex-direction: column-reverse;
  }

  .btn {
    width: 100%;
  }
}
</style>

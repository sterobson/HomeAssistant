<template>
  <Transition name="toast">
    <div v-if="visible" class="toast" :class="type">
      <div class="toast-icon">
        <svg v-if="type === 'success'" width="20" height="20" viewBox="0 0 16 16" fill="currentColor">
          <path d="M16 8A8 8 0 110 8a8 8 0 0116 0zm-3.97-3.03a.75.75 0 00-1.08.022L7.477 9.417 5.384 7.323a.75.75 0 00-1.06 1.06L6.97 11.03a.75.75 0 001.079-.02l3.992-4.99a.75.75 0 00-.01-1.05z"/>
        </svg>
        <svg v-else-if="type === 'error'" width="20" height="20" viewBox="0 0 16 16" fill="currentColor">
          <path d="M16 8A8 8 0 110 8a8 8 0 0116 0zM5.354 4.646a.5.5 0 10-.708.708L7.293 8l-2.647 2.646a.5.5 0 00.708.708L8 8.707l2.646 2.647a.5.5 0 00.708-.708L8.707 8l2.647-2.646a.5.5 0 00-.708-.708L8 7.293 5.354 4.646z"/>
        </svg>
        <svg v-else width="20" height="20" viewBox="0 0 16 16" fill="currentColor">
          <path d="M8 16A8 8 0 108 0a8 8 0 000 16zm.93-9.412l-1 4.705c-.07.34.029.533.304.533.194 0 .487-.07.686-.246l-.088.416c-.287.346-.92.598-1.465.598-.703 0-1.002-.422-.808-1.319l.738-3.468c.064-.293.006-.399-.287-.47l-.451-.081.082-.381 2.29-.287zM8 5.5a1 1 0 110-2 1 1 0 010 2z"/>
        </svg>
      </div>
      <div class="toast-message">{{ message }}</div>
    </div>
  </Transition>
</template>

<script setup>
import { ref, watch } from 'vue'

const props = defineProps({
  message: {
    type: String,
    required: true
  },
  type: {
    type: String,
    default: 'info',
    validator: (value) => ['success', 'error', 'info'].includes(value)
  },
  duration: {
    type: Number,
    default: 3000
  }
})

const emit = defineEmits(['close'])

const visible = ref(true)
let timeoutId = null

watch(() => props.message, () => {
  visible.value = true
  clearTimeout(timeoutId)
  timeoutId = setTimeout(() => {
    visible.value = false
    setTimeout(() => {
      emit('close')
    }, 300)
  }, props.duration)
}, { immediate: true })
</script>

<style scoped>
.toast {
  position: fixed;
  top: 5rem;
  left: 50%;
  transform: translateX(-50%);
  background: var(--bg-secondary);
  border-radius: 8px;
  box-shadow: 0 4px 12px var(--shadow-md);
  padding: 1rem 1.5rem;
  display: flex;
  align-items: center;
  gap: 0.75rem;
  z-index: 2000;
  min-width: 300px;
  max-width: 500px;
  border: 1px solid var(--border-color);
}

.toast.success {
  border-left: 4px solid var(--color-success);
}

.toast.error {
  border-left: 4px solid var(--color-danger);
}

.toast.info {
  border-left: 4px solid var(--color-primary);
}

.toast-icon {
  flex-shrink: 0;
}

.toast.success .toast-icon {
  color: var(--color-success);
}

.toast.error .toast-icon {
  color: var(--color-danger);
}

.toast.info .toast-icon {
  color: var(--color-primary);
}

.toast-message {
  flex: 1;
  color: var(--text-primary);
  font-weight: 500;
}

.toast-enter-active,
.toast-leave-active {
  transition: all 0.3s ease;
}

.toast-enter-from {
  opacity: 0;
  transform: translateX(-50%) translateY(-20px);
}

.toast-leave-to {
  opacity: 0;
  transform: translateX(-50%) translateY(-20px);
}

@media (max-width: 600px) {
  .toast {
    min-width: auto;
    max-width: calc(100% - 2rem);
    left: 1rem;
    right: 1rem;
    transform: none;
  }

  .toast-enter-from,
  .toast-leave-to {
    transform: translateY(-20px);
  }
}
</style>

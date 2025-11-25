<template>
  <div class="boost-card">
    <div class="boost-card-header">
      <div class="boost-icon">
        <svg width="24" height="24" viewBox="0 0 16 16" fill="currentColor">
          <path d="M11.251.068a.5.5 0 01.227.58L9.677 6.5H13a.5.5 0 01.364.843l-8 8.5a.5.5 0 01-.842-.49L6.323 9.5H3a.5.5 0 01-.364-.843l8-8.5a.5.5 0 01.615-.09z"/>
        </svg>
      </div>
      <div class="boost-info">
        <h3>Boost Active</h3>
        <div class="boost-details">
          <div class="boost-temp">
            <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" class="icon">
              <path d="M6 2a2 2 0 012-2 2 2 0 012 2v7.5a3.5 3.5 0 11-4 0V2zm2-1a1 1 0 00-1 1v8a1 1 0 00.5.866 2.5 2.5 0 102.5 0A1 1 0 0010 10V2a1 1 0 00-1-1z"/>
            </svg>
            <span>{{ boostTemperature }}°C</span>
          </div>
          <div class="boost-time">
            <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" class="icon">
              <path d="M8 0a8 8 0 100 16A8 8 0 008 0zm0 14.5a6.5 6.5 0 110-13 6.5 6.5 0 010 13z"/>
              <path d="M8 3v5l3.5 2-.75 1.25L6.5 8.5V3H8z"/>
            </svg>
            <span>{{ timeRemaining }}</span>
          </div>
        </div>
      </div>
    </div>
    <button class="btn-cancel" @click="$emit('cancel')">
      Cancel Boost
    </button>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue'

const props = defineProps({
  boostEndTime: {
    type: String,
    required: true
  },
  boostTemperature: {
    type: Number,
    required: true
  }
})

defineEmits(['cancel'])

const now = ref(Date.now())
let intervalId = null

onMounted(() => {
  intervalId = setInterval(() => {
    now.value = Date.now()
  }, 1000)
})

onUnmounted(() => {
  if (intervalId) {
    clearInterval(intervalId)
  }
})

const timeRemaining = computed(() => {
  const endTime = new Date(props.boostEndTime).getTime()
  const remaining = endTime - now.value

  if (remaining <= 0) {
    return 'Ending...'
  }

  const hours = Math.floor(remaining / (1000 * 60 * 60))
  const minutes = Math.floor((remaining % (1000 * 60 * 60)) / (1000 * 60))

  if (hours > 0) {
    return `${hours}h ${minutes}m remaining`
  } else {
    return `${minutes}m remaining`
  }
})
</script>

<style scoped>
.boost-card {
  background: linear-gradient(135deg, rgba(243, 156, 18, 0.1) 0%, rgba(230, 126, 34, 0.1) 100%);
  border: 2px solid var(--color-warning);
  border-radius: 8px;
  padding: 1.5rem;
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.boost-card-header {
  display: flex;
  align-items: center;
  gap: 1rem;
}

.boost-icon {
  width: 48px;
  height: 48px;
  background: var(--color-warning);
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  color: white;
  flex-shrink: 0;
  animation: pulse-icon 2s ease-in-out infinite;
}

@keyframes pulse-icon {
  0%, 100% {
    transform: scale(1);
  }
  50% {
    transform: scale(1.05);
  }
}

.boost-info {
  flex: 1;
}

.boost-info h3 {
  font-size: 1.125rem;
  font-weight: 600;
  color: var(--text-primary);
  margin: 0 0 0.5rem 0;
}

.boost-details {
  display: flex;
  gap: 1.5rem;
  align-items: center;
}

.boost-temp,
.boost-time {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.95rem;
  color: var(--text-secondary);
}

.boost-temp .icon,
.boost-time .icon {
  color: var(--icon-color);
}

.boost-temp span,
.boost-time span {
  font-weight: 600;
  color: var(--text-primary);
}

.btn-cancel {
  background: var(--bg-secondary);
  border: 2px solid var(--color-warning);
  color: var(--color-warning);
  padding: 0.75rem 1rem;
  border-radius: 6px;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s;
  font-size: 1rem;
}

.btn-cancel:hover {
  background: var(--color-warning);
  color: white;
  transform: translateY(-1px);
}

.btn-cancel:active {
  transform: translateY(0);
}

@media (max-width: 600px) {
  .boost-card {
    padding: 1rem;
  }

  .boost-card-header {
    gap: 0.75rem;
  }

  .boost-icon {
    width: 40px;
    height: 40px;
  }

  .boost-details {
    flex-direction: column;
    align-items: flex-start;
    gap: 0.5rem;
  }
}
</style>

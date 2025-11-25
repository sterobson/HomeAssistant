<template>
  <div class="boost-overlay" @click.self="handleCancel">
    <div class="boost-modal">
      <div class="boost-header">
        <h3>Boost {{ roomName }}</h3>
        <button class="close-btn" @click="handleCancel">
          <svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor">
            <path d="M4.646 4.646a.5.5 0 01.708 0L10 9.293l4.646-4.647a.5.5 0 01.708.708L10.707 10l4.647 4.646a.5.5 0 01-.708.708L10 10.707l-4.646 4.647a.5.5 0 01-.708-.708L9.293 10 4.646 5.354a.5.5 0 010-.708z"/>
          </svg>
        </button>
      </div>

      <form @submit.prevent="handleSubmit" class="boost-form">
        <div class="form-group">
          <label for="temperature">Target Temperature (°C)</label>
          <div class="temperature-selector">
            <button type="button" class="temp-btn" @click="decreaseTemp">
              <svg width="24" height="24" viewBox="0 0 16 16" fill="currentColor">
                <path d="M0 8a.5.5 0 01.5-.5h15a.5.5 0 010 1H.5A.5.5 0 010 8z"/>
              </svg>
            </button>
            <div class="temperature-display">
              <span class="temp-value">{{ temperature }}</span>
              <span class="temp-unit">°C</span>
            </div>
            <button type="button" class="temp-btn" @click="increaseTemp">
              <svg width="24" height="24" viewBox="0 0 16 16" fill="currentColor">
                <path d="M8 0a.5.5 0 01.5.5v7h7a.5.5 0 010 1h-7v7a.5.5 0 01-1 0v-7h-7a.5.5 0 010-1h7v-7A.5.5 0 018 0z"/>
              </svg>
            </button>
          </div>
        </div>

        <div class="form-group">
          <label for="duration">Duration (hours)</label>
          <div class="duration-selector">
            <button
              v-for="hour in durationOptions"
              :key="hour"
              type="button"
              class="duration-btn"
              :class="{ active: duration === hour }"
              @click="duration = hour"
            >
              {{ hour }}h
            </button>
          </div>
          <div class="custom-duration">
            <input
              v-model.number="customDuration"
              type="number"
              min="0.5"
              max="24"
              step="0.5"
              placeholder="Custom"
              class="form-control"
              @input="handleCustomDuration"
            />
          </div>
        </div>

        <div class="boost-info">
          <svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor">
            <path d="M8 0a8 8 0 100 16A8 8 0 008 0zm.93 4.588l-2.29.287-.082.38.45.083c.294.07.352.176.288.469l-.738 3.468c-.194.897.105 1.319.808 1.319.545 0 1.178-.252 1.465-.598l.088-.416c-.2.176-.492.246-.686.246-.275 0-.375-.193-.304-.533l.816-3.833z"/>
            <circle cx="8" cy="2.5" r="1"/>
          </svg>
          <p>Boost will override your schedule and maintain {{ temperature }}°C for {{ displayDuration }}</p>
        </div>

        <div class="form-actions">
          <button type="button" class="btn btn-secondary" @click="handleCancel">
            Cancel
          </button>
          <button type="submit" class="btn btn-boost">
            <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor">
              <path d="M11.251.068a.5.5 0 01.227.58L9.677 6.5H13a.5.5 0 01.364.843l-8 8.5a.5.5 0 01-.842-.49L6.323 9.5H3a.5.5 0 01-.364-.843l8-8.5a.5.5 0 01.615-.09z"/>
            </svg>
            Start Boost
          </button>
        </div>
      </form>
    </div>
  </div>
</template>

<script setup>
import { ref, computed } from 'vue'

const props = defineProps({
  roomName: {
    type: String,
    required: true
  }
})

const emit = defineEmits(['boost', 'cancel'])

const temperature = ref(21)
const duration = ref(2)
const customDuration = ref(null)
const durationOptions = [1, 2, 3, 4, 6]

const displayDuration = computed(() => {
  const hours = duration.value
  if (hours === 1) return '1 hour'
  if (hours < 1) return `${hours * 60} minutes`
  return `${hours} hours`
})

const increaseTemp = () => {
  if (temperature.value < 30) {
    temperature.value += 0.5
  }
}

const decreaseTemp = () => {
  if (temperature.value > 5) {
    temperature.value -= 0.5
  }
}

const handleCustomDuration = () => {
  if (customDuration.value && customDuration.value > 0) {
    duration.value = customDuration.value
  }
}

const handleSubmit = () => {
  emit('boost', {
    temperature: temperature.value,
    duration: duration.value
  })
}

const handleCancel = () => {
  emit('cancel')
}
</script>

<style scoped>
.boost-overlay {
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

.boost-modal {
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

.boost-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1.5rem;
  border-bottom: 1px solid var(--border-color);
}

.boost-header h3 {
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

.boost-form {
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

.temperature-selector {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 1.5rem;
}

.temp-btn {
  background: var(--color-primary);
  border: none;
  color: white;
  width: 48px;
  height: 48px;
  border-radius: 50%;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.2s;
}

.temp-btn:hover {
  background: var(--color-primary-hover);
  transform: scale(1.05);
}

.temp-btn:active {
  transform: scale(0.95);
}

.temperature-display {
  display: flex;
  align-items: baseline;
  justify-content: center;
  gap: 0.25rem;
  min-width: 140px;
}

.temp-value {
  font-size: 3rem;
  font-weight: 700;
  color: var(--color-primary);
  line-height: 1;
}

.temp-unit {
  font-size: 1.5rem;
  font-weight: 500;
  color: var(--text-secondary);
}

.duration-selector {
  display: flex;
  gap: 0.5rem;
  margin-bottom: 0.75rem;
}

.duration-btn {
  flex: 1;
  padding: 0.75rem;
  border: 2px solid var(--border-color);
  background: var(--bg-secondary);
  color: var(--text-primary);
  border-radius: 8px;
  cursor: pointer;
  font-weight: 600;
  font-size: 1rem;
  transition: all 0.2s;
}

.duration-btn:hover {
  border-color: var(--color-primary);
  background-color: var(--hover-bg);
}

.duration-btn.active {
  background-color: var(--color-primary);
  border-color: var(--color-primary);
  color: white;
}

.custom-duration {
  margin-top: 0.5rem;
}

.form-control {
  width: 100%;
  padding: 0.75rem;
  border: 1px solid var(--border-color);
  background: var(--bg-secondary);
  color: var(--text-primary);
  border-radius: 6px;
  font-size: 1rem;
  transition: all 0.2s;
}

.form-control:focus {
  outline: none;
  border-color: var(--color-primary);
  box-shadow: 0 0 0 3px rgba(52, 152, 219, 0.1);
}

.boost-info {
  display: flex;
  align-items: flex-start;
  gap: 0.75rem;
  padding: 1rem;
  background-color: var(--bg-tertiary);
  border-radius: 8px;
  margin-bottom: 1.5rem;
  border: 1px solid var(--border-color);
}

.boost-info svg {
  color: var(--color-primary);
  flex-shrink: 0;
  margin-top: 0.125rem;
}

.boost-info p {
  margin: 0;
  font-size: 0.9rem;
  color: var(--text-secondary);
  line-height: 1.5;
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

.btn-secondary:hover {
  background-color: var(--btn-secondary-hover);
}

.btn-boost {
  background-color: var(--color-warning);
  color: white;
}

.btn-boost:hover {
  background-color: #e67e22;
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(243, 156, 18, 0.3);
}

.btn:active {
  transform: scale(0.98);
}

@media (max-width: 600px) {
  .boost-modal {
    border-radius: 0;
    max-height: 100vh;
  }

  .boost-header {
    padding: 1rem;
  }

  .boost-form {
    padding: 1rem;
  }

  .temperature-display {
    min-width: 120px;
  }

  .temp-value {
    font-size: 2.5rem;
  }

  .temp-unit {
    font-size: 1.25rem;
  }

  .form-actions {
    flex-direction: column;
  }
}
</style>

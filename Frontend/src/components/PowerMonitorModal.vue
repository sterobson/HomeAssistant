<template>
  <div class="monitor-overlay" @click.self="handleClose">
    <div class="monitor-modal">
      <div class="monitor-header">
        <h3>Live power</h3>
        <button class="close-btn" @click="handleClose">
          <svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor">
            <path d="M4.646 4.646a.5.5 0 01.708 0L10 9.293l4.646-4.647a.5.5 0 01.708.708L10.707 10l4.647 4.646a.5.5 0 01-.708.708L10 10.707l-4.646 4.647a.5.5 0 01-.708-.708L9.293 10 4.646 5.354a.5.5 0 010-.708z"/>
          </svg>
        </button>
      </div>

      <div class="monitor-body">
        <div v-if="!hasData" class="waiting-message">Waiting for data...</div>
        <EnergyFlowDiagram
          v-else
          :solarW="snapshot.solarW"
          :batteryW="snapshot.batteryW"
          :houseW="snapshot.houseW"
          :gridW="snapshot.gridW"
        />
      </div>

      <div class="monitor-footer">
        <div class="progress-bar">
          <div class="progress-fill" :style="{ width: progressPercent + '%' }"></div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue'
import EnergyFlowDiagram from './EnergyFlowDiagram.vue'
import { batteryApi } from '../services/batteryApi.js'
import { useSignalR } from '../composables/useSignalR.js'
import { getHouseId } from '../utils/cookies.js'

const emit = defineEmits(['close'])

const houseId = getHouseId()
const signalR = useSignalR(houseId)

const DURATION_SECONDS = 60

const snapshot = ref({
  solarW: 0,
  batteryW: 0,
  houseW: 0,
  gridW: 0
})

const hasData = ref(false)
const secondsRemaining = ref(DURATION_SECONDS)
let countdownInterval = null

const progressPercent = computed(() => ((DURATION_SECONDS - secondsRemaining.value) / DURATION_SECONDS) * 100)

function handleSnapshot(data) {
  snapshot.value = {
    solarW: data.solarW ?? 0,
    batteryW: data.batteryW ?? 0,
    houseW: data.houseW ?? 0,
    gridW: data.gridW ?? 0
  }
  hasData.value = true
}

function handleClose() {
  emit('close')
}

onMounted(async () => {
  // Register SignalR listener
  signalR.on('power-snapshot', handleSnapshot)

  // Start countdown
  countdownInterval = setInterval(() => {
    secondsRemaining.value--
    if (secondsRemaining.value <= 0) {
      handleClose()
    }
  }, 1000)

  // Tell backend to start monitoring
  try {
    await batteryApi.startPowerMonitor()
  } catch (err) {
    console.error('Failed to start power monitor:', err)
  }
})

onUnmounted(() => {
  if (countdownInterval) {
    clearInterval(countdownInterval)
  }
  signalR.off('power-snapshot')
})
</script>

<style scoped>
.monitor-overlay {
  position: fixed;
  inset: 0;
  background-color: var(--overlay);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
  animation: fadeIn 0.2s;
}

.monitor-modal {
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 12px;
  width: 90%;
  max-width: 380px;
  display: flex;
  flex-direction: column;
  box-shadow: 0 8px 32px var(--shadow-md);
  animation: slideUp 0.25s;
}

.monitor-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1rem 1.25rem;
  border-bottom: 1px solid var(--border-color);
}

.monitor-header h3 {
  margin: 0;
  font-size: 1.1rem;
  font-weight: 600;
  color: var(--text-primary);
}

.close-btn {
  background: none;
  border: none;
  color: var(--icon-color);
  cursor: pointer;
  padding: 4px;
  border-radius: 4px;
  transition: background-color 0.15s, color 0.15s;
}

.close-btn:hover {
  color: var(--text-primary);
  background-color: var(--hover-bg);
}

.monitor-body {
  padding: 1.5rem 1.25rem;
  min-height: 300px;
  display: flex;
  align-items: center;
  justify-content: center;
}

.waiting-message {
  color: var(--text-secondary);
  font-size: 0.9rem;
}

.monitor-footer {
  padding: 0.75rem 1.25rem;
  border-top: 1px solid var(--border-color);
}

.progress-bar {
  height: 3px;
  background: var(--bg-tertiary);
  border-radius: 2px;
  overflow: hidden;
}

.progress-fill {
  height: 100%;
  background: var(--color-primary);
  border-radius: 2px;
  transition: width 1s linear;
}

@keyframes fadeIn {
  from { opacity: 0; }
  to { opacity: 1; }
}

@keyframes slideUp {
  from { opacity: 0; transform: translateY(20px); }
  to { opacity: 1; transform: translateY(0); }
}
</style>

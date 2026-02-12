<template>
  <div class="battery-view">
    <div class="date-nav">
      <button class="date-nav-btn" @click="goToPreviousDay">&lt;</button>
      <span class="date-label">{{ displayDate }}</span>
      <button class="date-nav-btn" @click="goToNextDay" :disabled="isToday">&gt;</button>
    </div>

    <div class="chart-wrapper">
      <BatteryChart
        :pricing-data="pricingData"
        :zones="resolvedZones"
        :date="selectedDate"
        :battery-history="batteryChartData"
        @create-zone="handleCreateZone"
        @edit-zone="handleEditZone"
        @update-target="handleUpdateTarget"
        @update-edge-time="handleUpdateEdgeTime"
      />
    </div>

    <div v-if="overlapError" class="overlap-toast">
      This zone overlaps an existing zone
    </div>

    <ZoneEditorModal
      v-if="showModal"
      :start-minutes="modalStartMinutes"
      :end-minutes="modalEndMinutes"
      :editing-rule="modalEditingRule"
      :pricing-data="pricingData"
      :suggested-action="modalSuggestedAction"
      :suggested-target-percent="modalSuggestedTargetPercent"
      :suggested-start-rule-type-key="modalSuggestedStartRuleTypeKey"
      :suggested-end-rule-type-key="modalSuggestedEndRuleTypeKey"
      @save="handleSave"
      @delete="handleDelete"
      @cancel="closeModal"
    />

    <BatterySetupModal
      v-if="showSetupModal"
      @save="closeSetupModal"
      @cancel="closeSetupModal"
    />
  </div>
</template>

<script setup>
import { ref, computed, watch, onMounted, onUnmounted } from 'vue'
import BatteryChart from '../components/BatteryChart.vue'
import ZoneEditorModal from '../components/ZoneEditorModal.vue'
import BatterySetupModal from '../components/BatterySetupModal.vue'
import { batteryApi } from '../services/batteryApi.js'
import { useBatteryRules } from '../composables/useBatteryRules.js'
import { useSignalR } from '../composables/useSignalR.js'
import { getHouseId } from '../utils/cookies.js'

const props = defineProps({
  showEntitySettings: { type: Boolean, default: false }
})

const emit = defineEmits(['entity-settings-closed'])

watch(() => props.showEntitySettings, (val) => {
  if (val) {
    showSetupModal.value = true
  }
})

function closeSetupModal() {
  showSetupModal.value = false
  emit('entity-settings-closed')
}

// Date navigation
const selectedDate = ref(new Date())

const dateString = computed(() => {
  const d = selectedDate.value
  const year = d.getFullYear()
  const month = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
})

const displayDate = computed(() => {
  const d = selectedDate.value
  return d.toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric' })
})

const isToday = computed(() => {
  const today = new Date()
  const sel = selectedDate.value
  return sel.getFullYear() === today.getFullYear()
    && sel.getMonth() === today.getMonth()
    && sel.getDate() === today.getDate()
})

function goToPreviousDay() {
  const d = new Date(selectedDate.value)
  d.setDate(d.getDate() - 1)
  selectedDate.value = d
}

function goToNextDay() {
  if (isToday.value) return
  const d = new Date(selectedDate.value)
  d.setDate(d.getDate() + 1)
  selectedDate.value = d
}

// Pricing data
const pricingData = ref([])

function getNextDateString(dateStr) {
  const d = new Date(dateStr + 'T00:00:00')
  d.setDate(d.getDate() + 1)
  const year = d.getFullYear()
  const month = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

async function loadPricing() {
  try {
    const [response, nextResponse] = await Promise.all([
      batteryApi.getPricing(dateString.value),
      batteryApi.getPricing(getNextDateString(dateString.value)).catch(() => null)
    ])

    if (response && response.points && response.points.length > 0) {
      const points = [...response.points]

      // Append tomorrow's first point at minute 1440 so stepped lines extend to midnight
      if (nextResponse && nextResponse.points && nextResponse.points.length > 0) {
        const firstTomorrow = nextResponse.points[0]
        points.push({
          timeMinutes: 1440,
          importPrice: firstTomorrow.importPrice,
          exportPrice: firstTomorrow.exportPrice
        })
      }

      pricingData.value = points
    } else {
      pricingData.value = []
      requestBackfillIfNeeded()
    }
  } catch {
    pricingData.value = []
  }
}

// Backfill logic — triggers once per date if either battery history or pricing is empty
const backfillRequestedForDate = ref(null)

function requestBackfillIfNeeded() {
  if (backfillRequestedForDate.value === dateString.value) return
  backfillRequestedForDate.value = dateString.value
  batteryApi.requestBackfill(dateString.value).catch(err => {
    console.error('Failed to request backfill:', err)
  })
}

// Battery history
const batteryHistory = ref([])

const batteryChartData = computed(() => {
  return batteryHistory.value
    .filter(point => point.batteryPercent != null && !isNaN(point.batteryPercent))
    .map(point => {
      const date = new Date(point.timestamp)
      const minutesFromMidnight = date.getHours() * 60 + date.getMinutes()
      return { x: minutesFromMidnight, y: point.batteryPercent }
    })
})

async function loadBatteryHistory() {
  try {
    const response = await batteryApi.getHistory(dateString.value)
    if (response && response.points && response.points.length > 0) {
      batteryHistory.value = response.points.map(p => ({
        timestamp: p.timestamp,
        batteryPercent: p.batteryPercent
      }))
    } else {
      batteryHistory.value = []
      requestBackfillIfNeeded()
    }
  } catch {
    batteryHistory.value = []
  }
}

watch(dateString, () => {
  backfillRequestedForDate.value = null
  loadPricing()
  loadBatteryHistory()
}, { immediate: true })

// SignalR connection for real-time battery updates
const houseId = getHouseId()
let signalR = houseId ? useSignalR(houseId) : null

async function initializeSignalR() {
  if (!signalR) return

  try {
    await signalR.connect()
    signalR.on('battery-state-changed', (data) => {
      if (!isToday.value) return
      if (data.batteryPercent == null) return

      const newPoint = {
        timestamp: new Date().toISOString(),
        batteryPercent: data.batteryPercent
      }

      // Only append if newer than the last point
      const lastPoint = batteryHistory.value[batteryHistory.value.length - 1]
      if (!lastPoint || new Date(newPoint.timestamp) > new Date(lastPoint.timestamp)) {
        batteryHistory.value = [...batteryHistory.value, newPoint]
      }
    })

    signalR.on('battery-history-replaced', (data) => {
      if (data.date === dateString.value) {
        loadBatteryHistory()
        loadPricing()
      }
    })
  } catch (err) {
    console.error('Failed to connect to SignalR for battery updates:', err)
  }
}

onMounted(() => {
  initializeSignalR()
})

onUnmounted(async () => {
  if (signalR) {
    signalR.off('battery-state-changed')
    signalR.off('battery-history-replaced')
    await signalR.disconnect()
  }
})

// Zone rules
const { getResolvedZones, addRule, updateRule, deleteRule, getRuleById, hasOverlap } = useBatteryRules()

const resolvedZones = getResolvedZones(pricingData)

// Setup modal state
const showSetupModal = ref(false)

// Modal state
const showModal = ref(false)
const modalStartMinutes = ref(0)
const modalEndMinutes = ref(0)
const modalEditingRule = ref(null)
const modalSuggestedAction = ref(null)
const modalSuggestedTargetPercent = ref(null)
const modalSuggestedStartRuleTypeKey = ref(null)
const modalSuggestedEndRuleTypeKey = ref(null)
const overlapError = ref(false)
let overlapTimer = null

function showOverlapError() {
  overlapError.value = true
  clearTimeout(overlapTimer)
  overlapTimer = setTimeout(() => {
    overlapError.value = false
  }, 3000)
}

function handleCreateZone({ startMinutes, endMinutes, suggestedAction, suggestedTargetPercent, suggestedStartRuleTypeKey, suggestedEndRuleTypeKey }) {
  const proposed = { startMinutes, endMinutes }
  if (hasOverlap(proposed, resolvedZones.value)) {
    showOverlapError()
    return
  }

  modalStartMinutes.value = startMinutes
  modalEndMinutes.value = endMinutes
  modalEditingRule.value = null
  modalSuggestedAction.value = suggestedAction || null
  modalSuggestedTargetPercent.value = suggestedTargetPercent ?? null
  modalSuggestedStartRuleTypeKey.value = suggestedStartRuleTypeKey || null
  modalSuggestedEndRuleTypeKey.value = suggestedEndRuleTypeKey || null
  showModal.value = true
}

function handleEditZone(ruleId) {
  const rule = getRuleById(ruleId)
  if (!rule) return

  const zone = resolvedZones.value.find(z => z.ruleId === ruleId)
  modalStartMinutes.value = zone ? zone.startMinutes : (rule.startTime?.fixedTimeMinutes ?? 0)
  modalEndMinutes.value = zone ? zone.endMinutes : (rule.endTime?.fixedTimeMinutes ?? 0)
  modalEditingRule.value = rule
  showModal.value = true
}

function closeModal() {
  showModal.value = false
}

function handleUpdateTarget({ ruleId, targetPercent }) {
  const rule = getRuleById(ruleId)
  if (!rule) return
  updateRule(ruleId, { ...rule, targetPercent })
}

function handleUpdateEdgeTime({ ruleId, edge, minutes }) {
  const rule = getRuleById(ruleId)
  if (!rule) return
  if (edge === 'start') {
    updateRule(ruleId, {
      ...rule,
      startTime: { ...rule.startTime, type: 'fixed-time', fixedTimeMinutes: minutes }
    })
  } else {
    updateRule(ruleId, {
      ...rule,
      endTime: { ...rule.endTime, type: 'fixed-time', fixedTimeMinutes: minutes }
    })
  }
}

function handleSave({ ruleData, editingRuleId }) {
  if (editingRuleId) {
    updateRule(editingRuleId, ruleData)
  } else {
    addRule(ruleData)
  }
  closeModal()
}

function handleDelete(ruleId) {
  deleteRule(ruleId)
  closeModal()
}
</script>

<style scoped>
.battery-view {
  display: flex;
  flex-direction: column;
  height: calc(100vh - 80px);
  width: 100%;
  position: relative;
}

.date-nav {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.5rem;
  padding: 0.5rem 1rem;
  flex-shrink: 0;
  position: relative;
}

.date-nav-btn {
  background: var(--bg-tertiary);
  color: var(--color-primary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  padding: 0.4rem 0.7rem;
  font-size: 1rem;
  font-weight: 600;
  cursor: pointer;
  transition: background 0.15s, border-color 0.15s, color 0.15s;
  line-height: 1;
}

.date-nav-btn:hover:not(:disabled) {
  background: var(--color-primary);
  color: var(--text-header);
  border-color: var(--color-primary);
}

.date-nav-btn:disabled {
  opacity: 0.3;
  cursor: not-allowed;
}

.date-label {
  font-size: 1.05rem;
  font-weight: 600;
  color: var(--text-primary);
  min-width: 200px;
  text-align: center;
  letter-spacing: 0.01em;
}

.chart-wrapper {
  flex: 1;
  min-height: 0;
}

.overlap-toast {
  position: absolute;
  bottom: 80px;
  left: 50%;
  transform: translateX(-50%);
  background: var(--color-danger, #e74c3c);
  color: white;
  padding: 0.75rem 1.25rem;
  border-radius: 8px;
  font-size: 0.9rem;
  font-weight: 500;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
  z-index: 500;
  animation: fadeIn 0.2s;
  white-space: nowrap;
}

@keyframes fadeIn {
  from { opacity: 0; transform: translateX(-50%) translateY(10px); }
  to { opacity: 1; transform: translateX(-50%) translateY(0); }
}
</style>

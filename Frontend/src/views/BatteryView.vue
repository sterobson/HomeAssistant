<template>
  <div class="battery-view">
    <div class="date-nav">
      <button class="date-nav-btn" @click="goToPrevious">&lt;</button>
      <span class="date-label">{{ navLabel }}</span>
      <button class="date-nav-btn" @click="goToNext" :disabled="atRangeEnd">&gt;</button>
    </div>

    <div class="chart-wrapper">
      <BatteryChart
        :pricing-data="pricingData"
        :zones="resolvedZones"
        :date="selectedDate"
        :month="selectedMonth"
        :year="selectedYear"
        :battery-history="batteryChartData"
        :energy-history="energyHistory"
        :daily-energy="dailyEnergy"
        :monthly-energy="monthlyEnergy"
        :days-in-month="daysInMonth"
        :chart-mode="chartMode"
        :pending-rule-ids="savingRuleIds"
        @create-zone="handleCreateZone"
        @edit-zone="handleEditZone"
        @update-target="handleUpdateTarget"
        @update-edge-time="handleUpdateEdgeTime"
        @update:chart-mode="chartMode = $event"
        @show-zones-list="showZonesList = true"
      />
    </div>

    <div v-if="overlapError" class="overlap-toast">
      This zone overlaps an existing zone
    </div>

    <div v-if="saveErrorVisible" class="overlap-toast">
      Failed to save, changes reverted
    </div>

    <ZonesListModal
      v-if="showZonesList"
      :rules="rules"
      :resolved-zones="resolvedZones"
      :z-index="1000"
      @edit="handleEditZoneFromList"
      @cancel="showZonesList = false"
    />

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
      :z-index="editorZIndex"
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
import ZonesListModal from '../components/ZonesListModal.vue'
import BatterySetupModal from '../components/BatterySetupModal.vue'
import { batteryApi } from '../services/batteryApi.js'
import { useBatteryRules } from '../composables/useBatteryRules.js'
import { useSignalR } from '../composables/useSignalR.js'
import { getHouseId } from '../utils/cookies.js'
import { getCache, setCache, pruneCache } from '../utils/apiCache.js'

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

const selectedMonth = ref(new Date())

const displayMonth = computed(() => {
  const d = selectedMonth.value
  return d.toLocaleDateString('en-GB', { month: 'long', year: 'numeric' })
})

const isCurrentMonth = computed(() => {
  const today = new Date()
  const sel = selectedMonth.value
  return sel.getFullYear() === today.getFullYear()
    && sel.getMonth() === today.getMonth()
})

const daysInMonth = computed(() => {
  const d = selectedMonth.value
  return new Date(d.getFullYear(), d.getMonth() + 1, 0).getDate()
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

function goToPreviousMonth() {
  const d = new Date(selectedMonth.value)
  d.setMonth(d.getMonth() - 1)
  selectedMonth.value = d
}

function goToNextMonth() {
  if (isCurrentMonth.value) return
  const d = new Date(selectedMonth.value)
  d.setMonth(d.getMonth() + 1)
  selectedMonth.value = d
}

// House ID (needed early for cache operations)
const houseId = getHouseId()

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

function getRecentDateKeys() {
  const keys = []
  for (let i = 0; i < 7; i++) {
    const d = new Date()
    d.setDate(d.getDate() - i)
    const year = d.getFullYear()
    const month = String(d.getMonth() + 1).padStart(2, '0')
    const day = String(d.getDate()).padStart(2, '0')
    keys.push(`${year}-${month}-${day}`)
  }
  // Also keep tomorrow for next-day pricing
  const tomorrow = new Date()
  tomorrow.setDate(tomorrow.getDate() + 1)
  const ty = tomorrow.getFullYear()
  const tm = String(tomorrow.getMonth() + 1).padStart(2, '0')
  const td = String(tomorrow.getDate()).padStart(2, '0')
  keys.push(`${ty}-${tm}-${td}`)
  return keys
}

function pruneDateCache(category) {
  if (!houseId) return
  pruneCache(houseId, category, getRecentDateKeys())
}

function mergePricingPoints(response) {
  if (!response || !response.points || response.points.length === 0) return null
  const points = [...response.points]
  const lastPoint = response.points[response.points.length - 1]

  // Bridge point: carry today's last rate past midnight so the end-of-day
  // plateau continues smoothly even if the midnight rate is a different tier.
  points.push({
    timeMinutes: 1440,
    importPrice: lastPoint.importPrice,
    exportPrice: lastPoint.exportPrice
  })

  // Self-extend: append today's data shifted by +1440 for cross-midnight zone
  // detection. Skip minute 0 (already covered by the bridge point above).
  for (const pt of response.points) {
    if (pt.timeMinutes === 0) continue
    points.push({
      timeMinutes: pt.timeMinutes + 1440,
      importPrice: pt.importPrice,
      exportPrice: pt.exportPrice
    })
  }
  return points
}

async function loadPricing() {
  if (!houseId) return

  const date = dateString.value

  // Show cached data immediately
  if (houseId) {
    const cachedResponse = getCache(houseId, 'pricing', date)
    const cachedPoints = mergePricingPoints(cachedResponse)
    if (cachedPoints) {
      pricingData.value = cachedPoints
    }
  }

  // Fetch fresh data in background
  try {
    const response = await batteryApi.getPricing(date)

    const points = mergePricingPoints(response)
    if (points) {
      pricingData.value = points
      if (houseId) {
        setCache(houseId, 'pricing', date, response)
        pruneDateCache('pricing')
      }
    } else {
      pricingData.value = []
      requestBackfillIfNeeded()
    }
  } catch {
    // Keep showing cached data if available
  }
}

// Backfill logic — triggers once per date if either battery history or pricing is empty
const backfillRequestedForDate = ref(null)

function requestBackfillIfNeeded() {
  if (!houseId) return
  if (backfillRequestedForDate.value === dateString.value) return
  backfillRequestedForDate.value = dateString.value
  batteryApi.requestBackfill(dateString.value).catch(err => {
    console.error('Failed to request backfill:', err)
  })
}

// Track which dates/months we've already requested energy backfill for
const energyBackfillRequested = new Set()

function requestEnergyBackfillIfNeeded(date) {
  if (!houseId) return
  if (energyBackfillRequested.has(date)) return
  energyBackfillRequested.add(date)
  batteryApi.requestEnergyBackfill(date).catch(err => {
    console.error('Failed to request energy backfill:', err)
  })
}

const energyBackfillMonthsRequested = new Set()

function requestEnergyBackfillForMonth(year, month, missingDates) {
  if (!houseId || missingDates.length === 0) return
  const monthKey = `${year}-${String(month + 1).padStart(2, '0')}`
  if (energyBackfillMonthsRequested.has(monthKey)) return
  energyBackfillMonthsRequested.add(monthKey)
  // Mark individual dates as requested too
  for (const d of missingDates) {
    energyBackfillRequested.add(d)
  }
  const fromDate = missingDates[0]
  const toDate = missingDates[missingDates.length - 1]
  batteryApi.requestEnergyBackfillRange(fromDate, toDate).catch(err => {
    console.error('Failed to request energy backfill range:', err)
  })
}

// Chart mode toggle (persisted)
const chartMode = ref(localStorage.getItem('battery-chart-mode') || 'battery')
watch(chartMode, (mode) => localStorage.setItem('battery-chart-mode', mode))

// Energy history
const energyHistory = ref([])

async function loadEnergyHistory() {
  if (!houseId) return

  const date = dateString.value

  // Show cached data immediately
  if (houseId) {
    const cached = getCache(houseId, 'energyHistory', date)
    if (cached && cached.points && cached.points.length > 0) {
      energyHistory.value = cached.points
    }
  }

  // Fetch fresh data in background
  try {
    const response = await batteryApi.getEnergyHistory(date)
    if (response && response.points && response.points.length > 0) {
      energyHistory.value = response.points
      if (houseId) {
        setCache(houseId, 'energyHistory', date, response)
        pruneDateCache('energyHistory')
      }
    } else {
      energyHistory.value = []
      requestEnergyBackfillIfNeeded(date)
    }
  } catch {
    // Keep showing cached data if available
  }
}

// Daily energy (monthly aggregation)
const dailyEnergy = ref([])
const dailyEnergyLoading = ref(false)

function formatDateString(year, month, day) {
  return `${year}-${String(month + 1).padStart(2, '0')}-${String(day).padStart(2, '0')}`
}

async function loadDailyEnergy() {
  if (!houseId) return
  dailyEnergyLoading.value = true

  const d = selectedMonth.value
  const year = d.getFullYear()
  const month = d.getMonth()
  const numDays = new Date(year, month + 1, 0).getDate()

  // Determine how many days to fetch (don't fetch future days)
  const today = new Date()
  const isCurrentMonth = year === today.getFullYear() && month === today.getMonth()
  const maxDay = isCurrentMonth ? today.getDate() : numDays

  const fromDate = formatDateString(year, month, 1)
  const toDate = formatDateString(year, month, maxDay)

  try {
    const response = await batteryApi.getEnergyHistoryRange(fromDate, toDate)
    const days = (response && response.days) || []

    const byDate = new Map()
    for (const entry of days) {
      byDate.set(entry.date, entry)
    }

    const results = []
    const missingDates = []
    for (let day = 1; day <= maxDay; day++) {
      const dateStr = formatDateString(year, month, day)
      const entry = byDate.get(dateStr)
      if (entry) {
        results.push({
          day,
          gridKwh: entry.gridKwh,
          batteryKwh: entry.batteryKwh,
          solarKwh: entry.solarKwh,
          houseKwh: entry.houseKwh,
          importCostPence: entry.importCostPence || 0,
          exportCostPence: entry.exportCostPence || 0
        })
      } else {
        missingDates.push(dateStr)
      }
    }

    if (missingDates.length > 0) {
      requestEnergyBackfillForMonth(year, month, missingDates)
    }

    dailyEnergy.value = results
  } catch {
    dailyEnergy.value = []
  } finally {
    dailyEnergyLoading.value = false
  }
}

// Monthly energy (yearly aggregation)
const monthlyEnergy = ref([])
const monthlyEnergyLoading = ref(false)
const selectedYear = ref(new Date().getFullYear())

const displayYear = computed(() => String(selectedYear.value))

const isCurrentYear = computed(() => selectedYear.value === new Date().getFullYear())

function goToPreviousYear() {
  selectedYear.value -= 1
}

function goToNextYear() {
  if (isCurrentYear.value) return
  selectedYear.value += 1
}

async function loadMonthlyEnergy() {
  if (!houseId) return
  monthlyEnergyLoading.value = true

  const year = selectedYear.value
  const today = new Date()
  const isThisYear = year === today.getFullYear()
  const lastMonth = isThisYear ? today.getMonth() : 11
  const lastDayOfLastMonth = new Date(year, lastMonth + 1, 0).getDate()
  const maxDay = isThisYear ? today.getDate() : lastDayOfLastMonth

  const fromDate = formatDateString(year, 0, 1)
  const toDate = isThisYear
    ? formatDateString(year, today.getMonth(), maxDay)
    : `${year}-12-31`

  try {
    const response = await batteryApi.getEnergyHistoryRange(fromDate, toDate)
    const days = (response && response.days) || []

    const buckets = new Array(12).fill(null).map((_, i) => ({
      month: i + 1,
      gridKwh: 0,
      batteryKwh: 0,
      solarKwh: 0,
      houseKwh: 0,
      importCostPence: 0,
      exportCostPence: 0,
      hasData: false
    }))

    for (const entry of days) {
      const parts = entry.date.split('-')
      if (parts.length < 2) continue
      const monthIndex = parseInt(parts[1], 10) - 1
      if (monthIndex < 0 || monthIndex > 11) continue
      const b = buckets[monthIndex]
      b.gridKwh += entry.gridKwh
      b.batteryKwh += entry.batteryKwh
      b.solarKwh += entry.solarKwh
      b.houseKwh += entry.houseKwh
      b.importCostPence += entry.importCostPence || 0
      b.exportCostPence += entry.exportCostPence || 0
      b.hasData = true
    }

    monthlyEnergy.value = buckets.filter(b => b.hasData)
  } catch {
    monthlyEnergy.value = []
  } finally {
    monthlyEnergyLoading.value = false
  }
}

watch([() => chartMode.value, selectedMonth], ([mode]) => {
  if (mode === 'daily' || mode === 'cost-daily') {
    loadDailyEnergy()
  }
}, { immediate: true })

watch([() => chartMode.value, selectedYear], ([mode]) => {
  if (mode === 'monthly' || mode === 'cost-monthly') {
    loadMonthlyEnergy()
  }
}, { immediate: true })

const isMonthlyMode = computed(() => chartMode.value === 'daily' || chartMode.value === 'cost-daily')
const isYearlyMode = computed(() => chartMode.value === 'monthly' || chartMode.value === 'cost-monthly')

const navLabel = computed(() => {
  if (isYearlyMode.value) return displayYear.value
  if (isMonthlyMode.value) return displayMonth.value
  return displayDate.value
})

const atRangeEnd = computed(() => {
  if (isYearlyMode.value) return isCurrentYear.value
  if (isMonthlyMode.value) return isCurrentMonth.value
  return isToday.value
})

function goToPrevious() {
  if (isYearlyMode.value) goToPreviousYear()
  else if (isMonthlyMode.value) goToPreviousMonth()
  else goToPreviousDay()
}

function goToNext() {
  if (isYearlyMode.value) goToNextYear()
  else if (isMonthlyMode.value) goToNextMonth()
  else goToNextDay()
}

// Battery history
const batteryHistory = ref([])
const previousDayLastPoint = ref(null)
const nextDayFirstPoint = ref(null)

const batteryChartData = computed(() => {
  const points = batteryHistory.value
    .filter(point => point.batteryPercent != null && !isNaN(point.batteryPercent))
    .map(point => {
      const date = new Date(point.timestamp)
      const minutesFromMidnight = date.getHours() * 60 + date.getMinutes()
      return { x: minutesFromMidnight, y: point.batteryPercent }
    })

  if (points.length === 0) return points

  // Interpolate a point at x=0 (midnight start) from the previous day's last point
  if (previousDayLastPoint.value && points[0].x > 0) {
    const prev = previousDayLastPoint.value
    const prevDate = new Date(prev.timestamp)
    const prevMinutes = prevDate.getHours() * 60 + prevDate.getMinutes()
    const first = points[0]
    // Time gap: minutes from prev point to midnight + minutes from midnight to first point
    const totalGapMinutes = (1440 - prevMinutes) + first.x
    const midnightOffset = 1440 - prevMinutes
    if (totalGapMinutes > 0) {
      const ratio = midnightOffset / totalGapMinutes
      const interpolatedY = prev.batteryPercent + (first.y - prev.batteryPercent) * ratio
      points.unshift({ x: 0, y: Math.round(interpolatedY * 10) / 10 })
    }
  }

  // Interpolate a point at x=1440 (midnight end) from the next day's first point
  if (nextDayFirstPoint.value && points[points.length - 1].x < 1440) {
    const next = nextDayFirstPoint.value
    const nextDate = new Date(next.timestamp)
    const nextMinutes = nextDate.getHours() * 60 + nextDate.getMinutes()
    const last = points[points.length - 1]
    // Time gap: minutes from last point to midnight + minutes from midnight to next point
    const totalGapMinutes = (1440 - last.x) + nextMinutes
    const midnightOffset = 1440 - last.x
    if (totalGapMinutes > 0) {
      const ratio = midnightOffset / totalGapMinutes
      const interpolatedY = last.y + (next.batteryPercent - last.y) * ratio
      points.push({ x: 1440, y: Math.round(interpolatedY * 10) / 10 })
    }
  }

  return points
})

function applyHistoryResponse(response) {
  batteryHistory.value = response.points.map(p => ({
    timestamp: p.timestamp,
    batteryPercent: p.batteryPercent
  }))
  previousDayLastPoint.value = response.previousDayLastPoint || null
  nextDayFirstPoint.value = response.nextDayFirstPoint || null
}

async function loadBatteryHistory() {
  if (!houseId) return

  const date = dateString.value

  // Show cached data immediately
  if (houseId) {
    const cached = getCache(houseId, 'history', date)
    if (cached && cached.points && cached.points.length > 0) {
      applyHistoryResponse(cached)
    }
  }

  // Fetch fresh data in background
  try {
    const response = await batteryApi.getHistory(date)
    if (response && response.points && response.points.length > 0) {
      applyHistoryResponse(response)
      if (houseId) {
        setCache(houseId, 'history', date, response)
        pruneDateCache('history')
      }
    } else {
      batteryHistory.value = []
      previousDayLastPoint.value = null
      nextDayFirstPoint.value = null
      requestBackfillIfNeeded()
    }
  } catch {
    // Keep showing cached data if available
  }
}

watch(dateString, () => {
  backfillRequestedForDate.value = null
  loadPricing()
  loadBatteryHistory()
  loadEnergyHistory()
}, { immediate: true })

// SignalR connection for real-time battery updates
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

    signalR.on('energy-history-changed', (data) => {
      if (data.date === dateString.value) {
        loadEnergyHistory()
      }
      if (chartMode.value === 'daily' || chartMode.value === 'cost-daily') {
        loadDailyEnergy()
      }
      if (chartMode.value === 'monthly' || chartMode.value === 'cost-monthly') {
        loadMonthlyEnergy()
      }
    })

    signalR.on('energy-history-replaced', (data) => {
      if (data.date === dateString.value) {
        loadEnergyHistory()
      }
      if (chartMode.value === 'daily' || chartMode.value === 'cost-daily') {
        loadDailyEnergy()
      }
      if (chartMode.value === 'monthly' || chartMode.value === 'cost-monthly') {
        loadMonthlyEnergy()
      }
    })
  } catch (err) {
    console.error('Failed to connect to SignalR for battery updates:', err)
  }
}

onMounted(() => {
  initializeSignalR()
  document.addEventListener('visibilitychange', handleVisibilityChange)
})

onUnmounted(async () => {
  document.removeEventListener('visibilitychange', handleVisibilityChange)
  if (signalR) {
    signalR.off('battery-state-changed')
    signalR.off('battery-history-replaced')
    signalR.off('energy-history-changed')
    signalR.off('energy-history-replaced')
    await signalR.disconnect()
  }
})

async function handleVisibilityChange() {
  if (document.visibilityState !== 'visible') return

  // Reconnect SignalR if the connection dropped while dormant
  if (signalR) {
    await signalR.ensureConnected()
  }

  // Reload data to catch up on anything missed
  loadPricing()
  loadBatteryHistory()
  loadEnergyHistory()
}

// Zone rules
const { rules, getResolvedZones, addRule, updateRule, deleteRule, getRuleById, hasOverlap, savingRuleIds, saveError } = useBatteryRules()

const resolvedZones = getResolvedZones(pricingData)

// Zones list modal
const showZonesList = ref(false)

// Editor sits above the list when it was opened from the list (so list stays
// visible underneath and the user can pick another zone after closing it).
const editorZIndex = computed(() => showZonesList.value ? 1100 : 1000)

function handleEditZoneFromList(ruleId) {
  handleEditZone(ruleId)
}

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
const saveErrorVisible = ref(false)
let saveErrorTimer = null

function showOverlapError() {
  overlapError.value = true
  clearTimeout(overlapTimer)
  overlapTimer = setTimeout(() => {
    overlapError.value = false
  }, 3000)
}

watch(saveError, (val) => {
  if (val) {
    saveErrorVisible.value = true
    clearTimeout(saveErrorTimer)
    saveErrorTimer = setTimeout(() => {
      saveErrorVisible.value = false
      saveError.value = null
    }, 4000)
  }
})

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
  height: calc(100dvh - 60px);
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

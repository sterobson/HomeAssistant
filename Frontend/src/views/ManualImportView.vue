<template>
  <div class="manual-import-view">
    <div class="info-banner">
      Manual energy import. Enter daily totals and a comparison date — each
      hour gets the same share of the total it had on the comparison day.
    </div>

    <section class="form-section">
      <h3>1. Target date</h3>
      <p class="hint">The day you want to write data for. Existing hours on this day will be overwritten.</p>
      <input type="date" v-model="targetDate" class="date-input" :max="todayStr" />
    </section>

    <section class="form-section">
      <h3>2. Totals</h3>
      <p class="hint">
        Net mode treats grid and battery as signed totals (positive grid = imported, negative = exported).
        Split mode takes each direction separately and computes the net as <em>import − export</em> / <em>charge − discharge</em>.
      </p>
      <div class="split-toggles">
        <label class="checkbox-field">
          <input type="checkbox" v-model="splitGrid" />
          <span>Split grid into import / export</span>
        </label>
        <label class="checkbox-field">
          <input type="checkbox" v-model="splitBattery" />
          <span>Split battery into charge / discharge</span>
        </label>
      </div>
      <div class="totals-grid">
        <template v-if="!splitGrid">
          <label class="total-field">
            <span>Grid kWh (+ import / − export)</span>
            <input type="number" step="0.001" v-model.number="totals.gridKwh" />
          </label>
        </template>
        <template v-else>
          <label class="total-field">
            <span>Grid import kWh</span>
            <input type="number" step="0.001" min="0" v-model.number="totals.gridImport" />
          </label>
          <label class="total-field">
            <span>Grid export kWh</span>
            <input type="number" step="0.001" min="0" v-model.number="totals.gridExport" />
          </label>
        </template>

        <template v-if="!splitBattery">
          <label class="total-field">
            <span>Battery kWh (+ charge / − discharge)</span>
            <input type="number" step="0.001" v-model.number="totals.batteryKwh" />
          </label>
        </template>
        <template v-else>
          <label class="total-field">
            <span>Battery charge kWh</span>
            <input type="number" step="0.001" min="0" v-model.number="totals.batteryCharge" />
          </label>
          <label class="total-field">
            <span>Battery discharge kWh</span>
            <input type="number" step="0.001" min="0" v-model.number="totals.batteryDischarge" />
          </label>
        </template>

        <label class="total-field">
          <span>Solar kWh</span>
          <input type="number" step="0.001" min="0" v-model.number="totals.solarKwh" />
        </label>
        <label class="total-field">
          <span>House kWh</span>
          <input type="number" step="0.001" min="0" v-model.number="totals.houseKwh" />
        </label>
        <label class="total-field">
          <span>Import cost (pence)</span>
          <input type="number" step="0.01" min="0" v-model.number="totals.importCostPence" />
        </label>
        <label class="total-field">
          <span>Export cost (pence)</span>
          <input type="number" step="0.01" min="0" v-model.number="totals.exportCostPence" />
        </label>
      </div>
    </section>

    <section class="form-section">
      <h3>3. Comparison date</h3>
      <p class="hint">
        The hourly distribution of this day will be copied. Pick a similar
        day — e.g. a weekday with comparable weather. Each hour's share of
        each component on the comparison day is applied to your totals.
      </p>
      <input type="date" v-model="comparisonDate" class="date-input" :max="todayStr" />
      <div class="comparison-status">
        <span v-if="loadingComparison" class="status-loading">Loading comparison data…</span>
        <span v-else-if="comparisonDate && comparisonHourCount === 0" class="status-empty">
          No data found for this date. Pick a different comparison date.
        </span>
        <span v-else-if="comparisonHourCount > 0" class="status-ok">
          {{ comparisonHourCount }} hours found for comparison.
        </span>
      </div>
    </section>

    <section v-if="previewPoints.length > 0" class="form-section">
      <h3>4. Preview</h3>
      <p class="hint">Updates live as you change the values above.</p>
      <ManualImportPreview :points="previewPoints" />
      <div class="preview-totals">
        <span><strong>Sums:</strong></span>
        <span>Grid {{ formatKwh(actualSums.gridKwh) }}</span>
        <span>Battery {{ formatKwh(actualSums.batteryKwh) }}</span>
        <span>Solar {{ formatKwh(actualSums.solarKwh) }}</span>
        <span>House {{ formatKwh(actualSums.houseKwh) }}</span>
        <span>Import £{{ (actualSums.importCostPence / 100).toFixed(2) }}</span>
        <span>Export £{{ (actualSums.exportCostPence / 100).toFixed(2) }}</span>
      </div>
    </section>

    <section class="form-section submit-section">
      <button
        class="submit-btn"
        :disabled="!canSubmit || submitting"
        @click="handleSubmit"
      >
        {{ submitting ? 'Saving…' : 'Save to ' + targetDate }}
      </button>
      <p v-if="submitMessage" :class="['submit-message', submitOk ? 'ok' : 'fail']">{{ submitMessage }}</p>
    </section>
  </div>
</template>

<script setup>
import { ref, computed, watch } from 'vue'
import { batteryApi } from '../services/batteryApi.js'
import ManualImportPreview from '../components/ManualImportPreview.vue'
import { buildPreviewPoints } from '../utils/energyDistribution.js'

function formatDate(d) {
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

const today = new Date()
const todayStr = formatDate(today)
const yesterday = new Date(today)
yesterday.setDate(yesterday.getDate() - 1)
const weekAgo = new Date(today)
weekAgo.setDate(weekAgo.getDate() - 7)

const targetDate = ref(formatDate(yesterday))
const comparisonDate = ref(formatDate(weekAgo))

const splitGrid = ref(false)
const splitBattery = ref(false)

// All fields kept in one object so toggling between net and split modes
// doesn't lose the user's previous input — only which fields the template
// renders changes.
const totals = ref({
  gridKwh: 0,
  gridImport: 0,
  gridExport: 0,
  batteryKwh: 0,
  batteryCharge: 0,
  batteryDischarge: 0,
  solarKwh: 0,
  houseKwh: 0,
  importCostPence: 0,
  exportCostPence: 0
})

// What buildPreviewPoints actually consumes: gridKwh and batteryKwh as nets,
// computed from the split fields when those toggles are on.
const effectiveTotals = computed(() => ({
  gridKwh: splitGrid.value
    ? (totals.value.gridImport || 0) - (totals.value.gridExport || 0)
    : (totals.value.gridKwh || 0),
  batteryKwh: splitBattery.value
    ? (totals.value.batteryCharge || 0) - (totals.value.batteryDischarge || 0)
    : (totals.value.batteryKwh || 0),
  solarKwh: totals.value.solarKwh || 0,
  houseKwh: totals.value.houseKwh || 0,
  importCostPence: totals.value.importCostPence || 0,
  exportCostPence: totals.value.exportCostPence || 0
}))

const comparisonHours = ref([]) // 24-entry array, filled with zeros if missing
const comparisonHourCount = ref(0) // number of hours that actually had data
const loadingComparison = ref(false)
const submitting = ref(false)
const submitMessage = ref('')
const submitOk = ref(false)

watch(comparisonDate, loadComparison, { immediate: true })

async function loadComparison(date) {
  comparisonHours.value = []
  comparisonHourCount.value = 0
  if (!date) return

  loadingComparison.value = true
  try {
    const response = await batteryApi.getEnergyHistory(date)
    const points = (response && response.points) || []
    comparisonHourCount.value = points.length

    // Index by hour, fill missing hours with zeros so we always have 24.
    const byHour = new Map(points.map(p => [p.hour, p]))
    const filled = []
    for (let h = 0; h < 24; h++) {
      const p = byHour.get(h)
      filled.push(p ? {
        hour: h,
        gridKwh: p.gridKwh || 0,
        batteryKwh: p.batteryKwh || 0,
        solarKwh: p.solarKwh || 0,
        houseKwh: p.houseKwh || 0,
        importCostPence: p.importCostPence || 0,
        exportCostPence: p.exportCostPence || 0
      } : {
        hour: h,
        gridKwh: 0,
        batteryKwh: 0,
        solarKwh: 0,
        houseKwh: 0,
        importCostPence: 0,
        exportCostPence: 0
      })
    }
    comparisonHours.value = filled
  } catch (e) {
    console.error('Failed to load comparison day:', e)
    comparisonHours.value = []
    comparisonHourCount.value = 0
  } finally {
    loadingComparison.value = false
  }
}

const previewPoints = computed(() => buildPreviewPoints(effectiveTotals.value, comparisonHours.value))

const actualSums = computed(() => {
  const init = { gridKwh: 0, batteryKwh: 0, solarKwh: 0, houseKwh: 0, importCostPence: 0, exportCostPence: 0 }
  for (const p of previewPoints.value) {
    init.gridKwh += p.gridKwh
    init.batteryKwh += p.batteryKwh
    init.solarKwh += p.solarKwh
    init.houseKwh += p.houseKwh
    init.importCostPence += p.importCostPence
    init.exportCostPence += p.exportCostPence
  }
  return init
})

function formatKwh(v) {
  return `${v.toFixed(3)} kWh`
}

const canSubmit = computed(() => {
  return !!targetDate.value && previewPoints.value.length === 24
})

async function handleSubmit() {
  console.log('[ManualImport] Submit clicked', {
    canSubmit: canSubmit.value,
    targetDate: targetDate.value,
    pointsLength: previewPoints.value.length
  })
  if (!canSubmit.value) {
    submitMessage.value = previewPoints.value.length !== 24
      ? 'Cannot submit: no preview points yet. Pick a comparison date with data first.'
      : 'Cannot submit: pick a target date.'
    submitOk.value = false
    return
  }
  submitting.value = true
  submitMessage.value = ''
  submitOk.value = false
  try {
    console.log('[ManualImport] Posting', previewPoints.value.length, 'points to', targetDate.value)
    const result = await batteryApi.replaceEnergyHistoryDay(targetDate.value, previewPoints.value)
    console.log('[ManualImport] Save response', result)
    submitOk.value = true
    submitMessage.value = `Saved ${result?.pointCount ?? 24} hourly points to ${targetDate.value}.`
  } catch (e) {
    console.error('[ManualImport] Save failed', e)
    submitOk.value = false
    submitMessage.value = `Failed: ${e.message || 'unknown error'}`
  } finally {
    submitting.value = false
  }
}
</script>

<style scoped>
.manual-import-view {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  padding: 1rem;
  max-width: 920px;
  margin: 0 auto;
}

.info-banner {
  background: var(--bg-tertiary);
  border-left: 4px solid var(--color-primary);
  color: var(--text-primary);
  padding: 0.6rem 0.8rem;
  border-radius: 6px;
  font-size: 0.9rem;
}

.form-section {
  background: var(--bg-secondary, var(--bg-tertiary));
  border: 1px solid var(--border-color);
  border-radius: 8px;
  padding: 0.9rem 1rem;
}

.form-section h3 {
  margin: 0 0 0.4rem 0;
  font-size: 1rem;
  color: var(--text-primary);
}

.hint {
  margin: 0 0 0.6rem 0;
  font-size: 0.82rem;
  color: var(--text-secondary);
  line-height: 1.4;
}

.date-input {
  background: var(--bg-tertiary);
  color: var(--text-primary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  padding: 0.4rem 0.6rem;
  font-size: 0.9rem;
}

.split-toggles {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem 1.2rem;
  margin-bottom: 0.7rem;
}

.checkbox-field {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  font-size: 0.85rem;
  color: var(--text-secondary);
  cursor: pointer;
  user-select: none;
}

.checkbox-field input[type="checkbox"] {
  accent-color: var(--color-primary);
  cursor: pointer;
}

.totals-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(190px, 1fr));
  gap: 0.6rem;
}

.total-field {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.82rem;
  color: var(--text-secondary);
}

.total-field input {
  background: var(--bg-tertiary);
  color: var(--text-primary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  padding: 0.4rem 0.6rem;
  font-size: 0.95rem;
  font-variant-numeric: tabular-nums;
}

.total-field input:focus {
  outline: 2px solid var(--color-primary);
  outline-offset: -1px;
}

.comparison-status {
  margin-top: 0.5rem;
  font-size: 0.85rem;
}

.status-loading { color: var(--text-secondary); }
.status-empty { color: #e67e22; }
.status-ok { color: #27ae60; }

.preview-totals {
  margin-top: 0.6rem;
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem 1rem;
  font-size: 0.82rem;
  color: var(--text-secondary);
  font-variant-numeric: tabular-nums;
}

.submit-section {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.submit-btn {
  background: var(--color-primary);
  color: var(--text-header);
  border: none;
  border-radius: 8px;
  padding: 0.55rem 1.1rem;
  font-size: 0.95rem;
  font-weight: 600;
  cursor: pointer;
  transition: opacity 0.15s;
}

.submit-btn:disabled {
  opacity: 0.45;
  cursor: not-allowed;
}

.submit-message {
  margin: 0;
  font-size: 0.88rem;
}

.submit-message.ok { color: #27ae60; }
.submit-message.fail { color: #e74c3c; }
</style>

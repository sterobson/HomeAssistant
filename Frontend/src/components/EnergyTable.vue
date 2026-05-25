<template>
  <div class="energy-table-wrapper">
    <div v-if="useSelector && columns.length > 1" class="column-selector-row">
      <label class="column-selector-label">Show:</label>
      <select v-model="selectedColumnKey" class="column-selector">
        <option v-for="col in columns" :key="col.key" :value="col.key">{{ col.label }}</option>
      </select>
    </div>
    <div ref="containerRef" class="table-scroll" :class="{ 'with-selector': useSelector }">
      <table class="energy-table">
        <thead>
          <tr>
            <th
              class="row-header-col sortable"
              :class="{ 'sort-active': sortKey === '__label' }"
              @click="toggleSort('__label')"
            >
              {{ rowHeader }}
              <span class="sort-indicator">{{ sortIndicator('__label') }}</span>
            </th>
            <template v-if="useSelector">
              <th
                class="value-col sortable"
                :class="{ 'sort-active': sortKey === activeColumn.key }"
                @click="toggleSort(activeColumn.key)"
              >
                {{ activeColumn.label }} <span class="unit">({{ activeColumn.unit }})</span>
                <span class="sort-indicator">{{ sortIndicator(activeColumn.key) }}</span>
              </th>
            </template>
            <template v-else>
              <th
                v-for="col in columns"
                :key="col.key"
                class="value-col sortable"
                :class="{ 'sort-active': sortKey === col.key }"
                @click="toggleSort(col.key)"
              >
                {{ col.label }} <span class="unit">({{ col.unit }})</span>
                <span class="sort-indicator">{{ sortIndicator(col.key) }}</span>
              </th>
            </template>
          </tr>
        </thead>
        <tbody>
          <tr v-for="row in sortedRows" :key="row.key">
            <td class="row-header-col">{{ row.label }}</td>
            <template v-if="useSelector">
              <td class="value-col">{{ formatValue(row.values[activeColumn.key], activeColumn) }}</td>
            </template>
            <template v-else>
              <td v-for="col in columns" :key="col.key" class="value-col">
                {{ formatValue(row.values[col.key], col) }}
              </td>
            </template>
          </tr>
        </tbody>
        <tfoot v-if="sortedRows.length > 0">
          <tr>
            <th class="row-header-col">Total</th>
            <template v-if="useSelector">
              <th class="value-col">{{ formatValue(totals[activeColumn.key], activeColumn) }}</th>
            </template>
            <template v-else>
              <th v-for="col in columns" :key="col.key" class="value-col">
                {{ formatValue(totals[col.key], col) }}
              </th>
            </template>
          </tr>
        </tfoot>
      </table>
      <div v-if="sortedRows.length === 0" class="empty-state">No data available</div>
    </div>
  </div>
</template>

<script setup>
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useFormatting } from '../composables/useFormatting.js'

const props = defineProps({
  mode: {
    type: String,
    required: true,
    validator: (v) => ['hourly', 'daily', 'monthly'].includes(v)
  },
  variant: {
    type: String,
    required: true,
    validator: (v) => ['energy', 'cost'].includes(v)
  },
  data: {
    type: Array,
    default: () => []
  },
  month: {
    type: Date,
    default: () => new Date()
  },
  year: {
    type: Number,
    default: () => new Date().getFullYear()
  }
})

const { formatTimeDisplay } = useFormatting()

const monthNames = ['January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December']

function formatHourLabel(hour) {
  const start = `${hour.toString().padStart(2, '0')}:00`
  const endHour = (hour + 1) % 24
  const end = `${endHour.toString().padStart(2, '0')}:00`
  return `${formatTimeDisplay(start)} – ${formatTimeDisplay(end)}`
}

const rowHeader = computed(() => {
  if (props.mode === 'hourly') return 'Hour'
  if (props.mode === 'daily') return 'Day'
  return 'Month'
})

const energyColumns = [
  { key: 'solar', label: 'Solar', unit: 'kWh', decimals: 2 },
  { key: 'house', label: 'House', unit: 'kWh', decimals: 2 },
  { key: 'import', label: 'Import', unit: 'kWh', decimals: 2 },
  { key: 'export', label: 'Export', unit: 'kWh', decimals: 2 },
  { key: 'gridNet', label: 'Import/Export net', unit: 'kWh', decimals: 2 },
  { key: 'charge', label: 'Charge', unit: 'kWh', decimals: 2 },
  { key: 'discharge', label: 'Discharge', unit: 'kWh', decimals: 2 },
  { key: 'batteryNet', label: 'Charge/Discharge net', unit: 'kWh', decimals: 2 }
]

const costColumns = [
  { key: 'importCost', label: 'Import', unit: '£', decimals: 2 },
  { key: 'exportCost', label: 'Export', unit: '£', decimals: 2 },
  { key: 'netCost', label: 'Net', unit: '£', decimals: 2 }
]

const columns = computed(() => props.variant === 'cost' ? costColumns : energyColumns)

function computeValues(p) {
  if (props.variant === 'cost') {
    const importCost = (p.importCostPence || 0) / 100
    const exportCost = (p.exportCostPence || 0) / 100
    return {
      importCost,
      exportCost,
      netCost: importCost - exportCost
    }
  }
  const gridKwh = p.gridKwh || 0
  const batteryKwh = p.batteryKwh || 0
  return {
    solar: p.solarKwh || 0,
    house: p.houseKwh || 0,
    import: Math.max(0, gridKwh),
    export: Math.max(0, -gridKwh),
    gridNet: gridKwh,
    discharge: Math.max(0, -batteryKwh),
    charge: Math.max(0, batteryKwh),
    batteryNet: batteryKwh
  }
}

const rows = computed(() => {
  if (!props.data || props.data.length === 0) return []

  if (props.mode === 'hourly') {
    return props.data.map(p => ({
      key: `h-${p.hour}`,
      label: formatHourLabel(p.hour),
      order: p.hour,
      values: computeValues(p)
    }))
  }

  if (props.mode === 'daily') {
    return props.data.map(p => {
      const d = new Date(props.month.getFullYear(), props.month.getMonth(), p.day)
      return {
        key: `d-${p.day}`,
        label: d.toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric' }),
        order: p.day,
        values: computeValues(p)
      }
    })
  }

  // monthly
  return props.data.map(p => ({
    key: `m-${p.month}`,
    label: monthNames[p.month - 1] || `Month ${p.month}`,
    order: p.month,
    values: computeValues(p)
  }))
})

// Sort state — persists across data and variant changes. Use '__label' as the
// pseudo-key for the chronological row-header column.
const sortKey = ref('__label')
const sortDirection = ref('asc')

function toggleSort(key) {
  if (sortKey.value === key) {
    sortDirection.value = sortDirection.value === 'asc' ? 'desc' : 'asc'
  } else {
    sortKey.value = key
    sortDirection.value = 'asc'
  }
}

function sortIndicator(key) {
  if (sortKey.value !== key) return ''
  return sortDirection.value === 'asc' ? '↑' : '↓'
}

const sortedRows = computed(() => {
  const list = rows.value.slice()
  const mult = sortDirection.value === 'asc' ? 1 : -1
  if (sortKey.value === '__label') {
    list.sort((a, b) => (a.order - b.order) * mult)
  } else {
    const key = sortKey.value
    list.sort((a, b) => ((a.values[key] || 0) - (b.values[key] || 0)) * mult)
  }
  return list
})

const totals = computed(() => {
  const out = {}
  for (const col of columns.value) {
    out[col.key] = 0
  }
  for (const row of rows.value) {
    for (const col of columns.value) {
      out[col.key] += row.values[col.key] || 0
    }
  }
  return out
})

function formatValue(value, col) {
  if (value == null || isNaN(value)) return '—'
  if (Math.abs(value) < Math.pow(10, -col.decimals) / 2) return '0'
  return value.toFixed(col.decimals)
}

// Switch to single-column selector when the container isn't wide enough to
// fit every column without overflow. Threshold scales with the column count
// (8 for energy, 3 for cost).
const containerRef = ref(null)
const containerWidth = ref(window.innerWidth)
let resizeObserver = null

function estimateColumnWidth(col) {
  const headerChars = col.label.length + col.unit.length + 3 // " ()" overhead
  // ~7.5px per char at 0.85rem bold, plus ~24px cell padding
  return Math.max(80, headerChars * 7.5) + 24
}

const requiredWidth = computed(() => {
  const ROW_HEADER_COL = 80
  return ROW_HEADER_COL + columns.value.reduce((sum, c) => sum + estimateColumnWidth(c), 0)
})

const useSelector = computed(() => containerWidth.value < requiredWidth.value)

onMounted(() => {
  if (containerRef.value && typeof ResizeObserver !== 'undefined') {
    resizeObserver = new ResizeObserver(entries => {
      containerWidth.value = entries[0].contentRect.width
    })
    resizeObserver.observe(containerRef.value)
  }
})
onUnmounted(() => {
  if (resizeObserver) resizeObserver.disconnect()
})

const selectedColumnKey = ref(columns.value[0].key)
const activeColumn = computed(() =>
  columns.value.find(c => c.key === selectedColumnKey.value) || columns.value[0]
)

// Reset selection when the variant changes (energy <-> cost) so the dropdown
// doesn't get stuck holding a key that no longer exists in the column set.
// Same for the sort key — if the user was sorting by an energy-only column
// and switches to cost, fall back to the chronological row column.
watch(columns, (cols) => {
  if (!cols.find(c => c.key === selectedColumnKey.value)) {
    selectedColumnKey.value = cols[0].key
  }
  if (sortKey.value !== '__label' && !cols.find(c => c.key === sortKey.value)) {
    sortKey.value = '__label'
    sortDirection.value = 'asc'
  }
})
</script>

<style scoped>
.energy-table-wrapper {
  width: 100%;
  height: 100%;
  display: flex;
  flex-direction: column;
  padding: 8px;
  box-sizing: border-box;
}

.column-selector-row {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 0.5rem;
  flex-shrink: 0;
}

.column-selector-label {
  color: var(--text-secondary);
  font-size: 0.85rem;
  font-weight: 500;
}

.column-selector {
  background: var(--bg-tertiary);
  color: var(--text-primary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  padding: 0.3rem 0.6rem;
  font-size: 0.85rem;
  cursor: pointer;
  flex: 1;
  max-width: 16rem;
}

.table-scroll {
  flex: 1;
  overflow: auto;
  border: 1px solid var(--border-color);
  border-radius: 6px;
  background: var(--bg-secondary, var(--bg-tertiary));
}

.energy-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.85rem;
}

.energy-table thead th {
  position: sticky;
  top: 0;
  background: var(--bg-tertiary);
  color: var(--text-primary);
  font-weight: 600;
  text-align: right;
  padding: 0.5rem 0.75rem;
  border-bottom: 2px solid var(--border-color);
  white-space: nowrap;
  z-index: 1;
}

.energy-table thead th.row-header-col {
  text-align: left;
}

.energy-table thead .unit {
  color: var(--text-secondary);
  font-weight: 400;
  font-size: 0.78rem;
}

.energy-table thead th.sortable {
  cursor: pointer;
  user-select: none;
}

.energy-table thead th.sortable:hover {
  background: var(--bg-secondary, var(--bg-tertiary));
}

.energy-table thead th.sort-active {
  color: var(--color-primary, var(--text-primary));
}

.sort-indicator {
  display: inline-block;
  min-width: 0.8em;
  margin-left: 0.25em;
  color: var(--color-primary, var(--text-secondary));
  font-size: 0.85em;
}

.energy-table tbody td {
  padding: 0.4rem 0.75rem;
  border-bottom: 1px solid var(--border-color);
  color: var(--text-primary);
  white-space: nowrap;
}

.energy-table tbody tr:last-child td {
  border-bottom: none;
}

.energy-table .row-header-col {
  text-align: left;
  color: var(--text-secondary);
  font-weight: 500;
}

.energy-table .value-col {
  text-align: right;
  font-variant-numeric: tabular-nums;
}

.energy-table tfoot th {
  position: sticky;
  bottom: 0;
  background: var(--bg-tertiary);
  color: var(--text-primary);
  font-weight: 600;
  text-align: right;
  padding: 0.5rem 0.75rem;
  border-top: 2px solid var(--border-color);
  white-space: nowrap;
}

.energy-table tfoot th.row-header-col {
  text-align: left;
}

.empty-state {
  padding: 2rem;
  text-align: center;
  color: var(--text-secondary);
  font-size: 0.9rem;
}
</style>

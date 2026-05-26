<template>
  <div class="manual-preview">
    <div class="preview-toggle">
      <button
        :class="['toggle-btn', { active: viewMode === 'chart' }]"
        @click="viewMode = 'chart'"
      >Chart</button>
      <button
        :class="['toggle-btn', { active: viewMode === 'table' }]"
        @click="viewMode = 'table'"
      >Table</button>
    </div>
    <div class="preview-body">
      <div v-if="viewMode === 'chart'" class="chart-wrap">
        <Line :data="chartData" :options="chartOptions" />
      </div>
      <EnergyTable
        v-else
        mode="hourly"
        variant="energy"
        :data="points"
      />
    </div>
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue'
import { Line } from 'vue-chartjs'
import {
  Chart as ChartJS,
  LinearScale,
  PointElement,
  LineElement,
  BarElement,
  BarController,
  Title,
  Tooltip,
  Legend
} from 'chart.js'
import EnergyTable from './EnergyTable.vue'

// Idempotent: safe to call again if BatteryChart already registered these.
ChartJS.register(LinearScale, PointElement, LineElement, BarElement, BarController, Title, Tooltip, Legend)

const props = defineProps({
  points: {
    type: Array,
    default: () => []
  }
})

const viewMode = ref('chart')

// Chart.js doesn't resolve CSS custom properties from option strings, so we
// snapshot the values at mount time. Matches BatteryChart's approach.
const themeColors = ref({
  textPrimary: '#000000',
  textSecondary: '#666666',
  gridColor: 'rgba(128, 128, 128, 0.1)'
})

onMounted(() => {
  const styles = getComputedStyle(document.documentElement)
  const textSecondary = styles.getPropertyValue('--text-secondary').trim()
  themeColors.value = {
    textPrimary: styles.getPropertyValue('--text-primary').trim() || '#000000',
    textSecondary: textSecondary || '#666666',
    gridColor: textSecondary ? `${textSecondary}33` : 'rgba(128, 128, 128, 0.1)'
  }
})

const chartData = computed(() => {
  const pos = v => v > 0 ? v : 0
  const neg = v => v < 0 ? v : 0
  const toXY = (p, val) => ({ x: p.hour * 60 + 30, y: val })
  const barBase = { type: 'bar', barPercentage: 1.0, categoryPercentage: 1.0, borderWidth: 1, stack: 'energy' }

  return {
    datasets: [
      {
        ...barBase,
        label: 'Solar',
        data: props.points.map(p => toXY(p, p.solarKwh)),
        backgroundColor: 'rgba(241, 196, 15, 0.7)',
        borderColor: '#f1c40f'
      },
      {
        ...barBase,
        label: 'House',
        data: props.points.map(p => toXY(p, -p.houseKwh)),
        backgroundColor: 'rgba(155, 89, 182, 0.7)',
        borderColor: '#9b59b6'
      },
      {
        ...barBase,
        label: 'Import',
        data: props.points.map(p => toXY(p, pos(p.gridKwh))),
        backgroundColor: 'rgba(231, 76, 60, 0.7)',
        borderColor: '#e74c3c'
      },
      {
        ...barBase,
        label: 'Export',
        data: props.points.map(p => toXY(p, neg(p.gridKwh))),
        backgroundColor: 'rgba(39, 174, 96, 0.7)',
        borderColor: '#27ae60'
      },
      {
        ...barBase,
        label: 'Discharge',
        data: props.points.map(p => toXY(p, pos(-p.batteryKwh))),
        backgroundColor: 'rgba(230, 126, 34, 0.7)',
        borderColor: '#e67e22'
      },
      {
        ...barBase,
        label: 'Charge',
        data: props.points.map(p => toXY(p, neg(-p.batteryKwh))),
        backgroundColor: 'rgba(52, 152, 219, 0.7)',
        borderColor: '#3498db'
      }
    ]
  }
})

const chartOptions = computed(() => ({
  responsive: true,
  maintainAspectRatio: false,
  scales: {
    x: {
      type: 'linear',
      min: 0,
      max: 1440,
      ticks: {
        color: themeColors.value.textSecondary,
        stepSize: 120,
        callback: v => `${String(Math.floor(v / 60)).padStart(2, '0')}:00`
      },
      grid: { color: themeColors.value.gridColor }
    },
    y: {
      stacked: true,
      ticks: {
        color: themeColors.value.textSecondary,
        callback: v => `${v} kWh`
      },
      grid: { color: themeColors.value.gridColor }
    }
  },
  plugins: {
    legend: {
      position: 'bottom',
      labels: {
        color: themeColors.value.textPrimary,
        usePointStyle: true,
        padding: 12
      }
    },
    tooltip: {
      callbacks: {
        title: items => {
          if (items.length === 0) return ''
          const m = items[0].parsed.x
          const h = Math.round((m - 30) / 60)
          const nextH = (h + 1) % 24
          return `${String(h).padStart(2, '0')}:00 – ${String(nextH).padStart(2, '0')}:00`
        },
        label: ctx => {
          if (ctx.parsed.y === 0) return null
          return `${ctx.dataset.label}: ${Math.abs(ctx.parsed.y).toFixed(3)} kWh`
        }
      }
    }
  }
}))
</script>

<style scoped>
.manual-preview {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.preview-toggle {
  display: flex;
  justify-content: center;
  gap: 0;
}

.toggle-btn {
  background: var(--bg-tertiary);
  color: var(--text-secondary);
  border: 1px solid var(--border-color);
  padding: 0.25rem 0.75rem;
  font-size: 0.85rem;
  cursor: pointer;
  transition: background 0.15s, color 0.15s, border-color 0.15s;
}

.toggle-btn:first-child {
  border-radius: 6px 0 0 6px;
}

.toggle-btn:last-child {
  border-radius: 0 6px 6px 0;
  border-left: none;
}

.toggle-btn.active {
  background: var(--color-primary);
  color: var(--text-header);
  border-color: var(--color-primary);
}

.preview-body {
  min-height: 360px;
  height: 360px;
  display: flex;
}

.chart-wrap {
  position: relative;
  width: 100%;
  height: 100%;
}
</style>

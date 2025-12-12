<template>
  <div class="chart-container">
    <Line :data="chartData" :options="chartOptions" />
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue'
import { Line } from 'vue-chartjs'
import {
  Chart as ChartJS,
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  Title,
  Tooltip,
  Legend,
  Filler,
  TimeScale
} from 'chart.js'
import 'chartjs-adapter-date-fns'
import { useFormatting } from '../composables/useFormatting.js'
import { useSettings } from '../composables/useSettings.js'

// Register Chart.js components
ChartJS.register(
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  Title,
  Tooltip,
  Legend,
  Filler,
  TimeScale
)

const { convertToDisplay, currentTempUnit } = useFormatting()

// Get CSS custom properties for theming
const themeColors = ref({
  textPrimary: '#000000',
  textSecondary: '#666666',
  bgTertiary: '#f5f5f5',
  borderColor: '#e0e0e0',
  gridColor: 'rgba(128, 128, 128, 0.1)'
})

onMounted(() => {
  const root = document.documentElement
  const styles = getComputedStyle(root)
  themeColors.value = {
    textPrimary: styles.getPropertyValue('--text-primary').trim() || '#000000',
    textSecondary: styles.getPropertyValue('--text-secondary').trim() || '#666666',
    bgTertiary: styles.getPropertyValue('--bg-tertiary').trim() || '#f5f5f5',
    borderColor: styles.getPropertyValue('--border-color').trim() || '#e0e0e0',
    gridColor: styles.getPropertyValue('--text-secondary').trim()
      ? `${styles.getPropertyValue('--text-secondary').trim()}33`
      : 'rgba(128, 128, 128, 0.1)'
  }
})

const props = defineProps({
  historyData: {
    type: Array,
    required: true
  },
  roomName: {
    type: String,
    default: 'Room'
  },
  period: {
    type: String,
    default: 'Day'
  }
})

const chartData = computed(() => {
  if (!props.historyData || props.historyData.length === 0) {
    return {
      datasets: []
    }
  }

  // Sort data by timestamp
  const sortedData = [...props.historyData].sort((a, b) =>
    new Date(a.timestamp) - new Date(b.timestamp)
  )

  // Prepare datasets with temperature conversion
  const currentTempData = sortedData
    .filter(point => point.currentTemperature != null)
    .map(point => ({
      x: new Date(point.timestamp),
      y: convertToDisplay(point.currentTemperature)
    }))

  const targetTempData = sortedData
    .filter(point => point.targetTemperature != null)
    .map(point => ({
      x: new Date(point.timestamp),
      y: convertToDisplay(point.targetTemperature)
    }))

  // Create heating active background segments
  const heatingSegments = []
  let heatingStart = null

  sortedData.forEach((point, index) => {
    if (point.heatingActive && heatingStart === null) {
      heatingStart = new Date(point.timestamp)
    } else if (!point.heatingActive && heatingStart !== null) {
      heatingSegments.push({
        x: heatingStart,
        x2: new Date(point.timestamp)
      })
      heatingStart = null
    }
  })

  // If heating is still on at the end
  if (heatingStart !== null && sortedData.length > 0) {
    heatingSegments.push({
      x: heatingStart,
      x2: new Date(sortedData[sortedData.length - 1].timestamp)
    })
  }

  return {
    datasets: [
      {
        label: 'Current Temperature',
        data: currentTempData,
        borderColor: 'rgb(66, 165, 245)',
        backgroundColor: 'rgba(66, 165, 245, 0.1)',
        fill: true,
        tension: 0.4,
        pointRadius: 0,
        pointHoverRadius: 0,
        borderWidth: 2
      },
      {
        label: 'Target Temperature',
        data: targetTempData,
        borderColor: 'rgb(251, 140, 0)',
        backgroundColor: 'transparent',
        borderDash: [5, 5],
        fill: false,
        tension: 0.4,
        pointRadius: 0,
        pointHoverRadius: 0,
        borderWidth: 2
      }
    ]
  }
})

const chartOptions = computed(() => {
  // Configure X-axis based on period
  let timeConfig = {}
  let ticksConfig = {
    color: themeColors.value.textSecondary,
    maxRotation: 0,
    autoSkip: false
  }

  if (props.period === 'Day') {
    // Day: Ticks at 0:00, 4:00, 8:00, 12:00, 16:00, 20:00
    timeConfig = {
      unit: 'hour',
      displayFormats: {
        hour: 'HH:mm'
      },
      tooltipFormat: 'PPpp'
    }
    ticksConfig.callback = function(value, index, ticks) {
      const date = new Date(value)
      const hour = date.getHours()
      // Only show labels for 0, 4, 8, 12, 16, 20
      if (hour % 4 === 0 && hour <= 20) {
        return hour.toString().padStart(2, '0') + ':00'
      }
      return null
    }
  } else if (props.period === 'Week') {
    // Week: 7 equal chunks with day names in the middle
    timeConfig = {
      unit: 'day',
      displayFormats: {
        day: 'EEE' // Mon, Tue, etc.
      },
      tooltipFormat: 'PPpp'
    }
    ticksConfig.callback = function(value, index, ticks) {
      const date = new Date(value)
      return date.toLocaleDateString(undefined, { weekday: 'short' })
    }
  } else {
    // Month: Labels at 1st, 4th, 7th, etc.
    timeConfig = {
      unit: 'day',
      displayFormats: {
        day: 'd'
      },
      tooltipFormat: 'PPpp'
    }
    ticksConfig.callback = function(value, index, ticks) {
      const date = new Date(value)
      const day = date.getDate()
      // Only show labels for 1, 4, 7, 10, etc.
      if (day === 1 || (day - 1) % 3 === 0) {
        // Ordinal suffix
        const suffix = day === 1 ? 'st' : day === 2 ? 'nd' : day === 3 ? 'rd' : 'th'
        return day + suffix
      }
      return null
    }
  }

  return {
    responsive: true,
    maintainAspectRatio: false,
    interaction: {
      mode: 'index',
      intersect: false
    },
    plugins: {
      legend: {
        display: true,
        position: 'bottom',
        labels: {
          color: themeColors.value.textPrimary,
          usePointStyle: true,
          padding: 15
        }
      },
      tooltip: {
        enabled: true,
        backgroundColor: themeColors.value.bgTertiary,
        titleColor: themeColors.value.textPrimary,
        bodyColor: themeColors.value.textSecondary,
        borderColor: themeColors.value.borderColor,
        borderWidth: 1,
        padding: 12,
        displayColors: true,
        callbacks: {
          label: function(context) {
            let label = context.dataset.label || ''
            if (label) {
              label += ': '
            }
            if (context.parsed.y !== null) {
              label += Math.round(context.parsed.y) + '°' + currentTempUnit.value
            }
            return label
          }
        }
      }
    },
    scales: {
      x: {
        type: 'time',
        // Chart.js automatically uses browser's local timezone
        // UTC timestamps from API are converted to local time for display
        time: timeConfig,
        grid: {
          color: themeColors.value.gridColor,
          drawBorder: true,
          lineWidth: 1
        },
        ticks: ticksConfig,
        title: {
          display: false
        }
      },
      y: {
        beginAtZero: false,
        grid: {
          color: themeColors.value.gridColor,
          drawBorder: true,
          lineWidth: 1
        },
        ticks: {
          color: themeColors.value.textSecondary,
          stepSize: 1,
          precision: 0,
          callback: function(value) {
            return Math.round(value) + '°' + currentTempUnit.value
          }
        },
        title: {
          display: true,
          text: 'Temperature',
          color: themeColors.value.textSecondary
        }
      }
    }
  }
})
</script>

<style scoped>
.chart-container {
  position: relative;
  height: 340px;
  width: 100%;
  padding: 12px;
}

@media (max-width: 768px) {
  .chart-container {
    height: 260px;
    padding: 8px;
  }
}
</style>

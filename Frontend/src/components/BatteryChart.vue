<template>
  <div class="battery-chart-container">
    <Line ref="chartRef" :data="chartData" :options="chartOptions" :plugins="[zoneRenderingPlugin, zoneInteractionPlugin]" />
  </div>
</template>

<script setup>
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { Line } from 'vue-chartjs'
import { getAvailableRuleTypes, calculateAveragePrice } from '../utils/priceAnalysis.js'
import { useFormatting } from '../composables/useFormatting.js'
import { isMinuteInZone, getElapsedMinutes } from '../composables/useBatteryRules.js'
import {
  Chart as ChartJS,
  LinearScale,
  PointElement,
  LineElement,
  Title,
  Tooltip,
  Legend,
  Filler
} from 'chart.js'

ChartJS.register(
  LinearScale,
  PointElement,
  LineElement,
  Title,
  Tooltip,
  Legend,
  Filler
)

const props = defineProps({
  pricingData: {
    type: Array,
    required: true
  },
  zones: {
    type: Array,
    default: () => []
  },
  date: {
    type: Date,
    required: true
  },
  batteryHistory: {
    type: Array,
    default: () => []
  },
  pendingRuleIds: {
    type: Set,
    default: () => new Set()
  }
})

const emit = defineEmits(['create-zone', 'edit-zone', 'update-target', 'update-edge-time'])

const chartRef = ref(null)
const { formatTimeDisplay, currentTimeFormat } = useFormatting()

// Theme colors from CSS custom properties
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

// Force chart re-render when zones change (vue-chartjs only watches data/options)
watch(() => props.zones, () => {
  chartRef.value?.chart?.update('none')
}, { deep: true })

// Animated dashed borders for pending zones
let pendingAnimationId = null

function startPendingAnimation() {
  if (pendingAnimationId) return
  function tick() {
    if (props.pendingRuleIds.size === 0) {
      pendingAnimationId = null
      return
    }
    chartRef.value?.chart?.update('none')
    pendingAnimationId = requestAnimationFrame(tick)
  }
  pendingAnimationId = requestAnimationFrame(tick)
}

watch(() => props.pendingRuleIds, (ids) => {
  if (ids.size > 0) {
    startPendingAnimation()
  }
}, { deep: true })

onUnmounted(() => {
  if (pendingAnimationId) {
    cancelAnimationFrame(pendingAnimationId)
  }
  cancelEdgeDragPending()
})

function formatMinutes(minutes) {
  const h = Math.floor(minutes / 60)
  const m = Math.round(minutes % 60)
  const time24 = `${h.toString().padStart(2, '0')}:${m.toString().padStart(2, '0')}`
  return formatTimeDisplay(time24)
}

/**
 * Split a zone into 1-2 display segments for rendering on the [0, 1440] chart.
 * Each segment carries _fullStart/_fullEnd from the original zone for graduated progress.
 */
function getDisplaySegments(zone) {
  if (zone.endMinutes <= 1440) {
    return [{ ...zone, _fullStart: zone.startMinutes, _fullEnd: zone.endMinutes }]
  }
  // Wrapped zone: split into evening + morning visual segments
  const segments = [
    { ...zone, startMinutes: zone.startMinutes, endMinutes: 1440, _fullStart: zone.startMinutes, _fullEnd: zone.endMinutes }
  ]
  const morningEnd = zone.endMinutes - 1440
  if (morningEnd > 0) {
    segments.push({ ...zone, startMinutes: 0, endMinutes: morningEnd, _fullStart: zone.startMinutes, _fullEnd: zone.endMinutes })
  }
  return segments
}

// --- Zone rendering plugin ---
// Split into two hooks so zone fills are behind datasets but labels are in front.
const zoneRenderingPlugin = {
  id: 'zoneRendering',

  // Zone fill rectangles and vertical borders: drawn BEHIND dataset lines
  beforeDatasetsDraw(chart) {
    const zones = props.zones
    const ctx = chart.ctx
    const chartArea = chart.chartArea
    const xScale = chart.scales.x

    if (!chartArea || !xScale) return

    ctx.save()

    // Flatten zones into display segments for rendering on [0, 1440]
    const allSegments = []
    for (const zone of zones) {
      allSegments.push(...getDisplaySegments(zone))
    }
    allSegments.sort((a, b) => a.startMinutes - b.startMinutes)

    for (const seg of allSegments) {
      const startPx = xScale.getPixelForValue(seg.startMinutes)
      const endPx = xScale.getPixelForValue(seg.endMinutes)
      const width = endPx - startPx
      const areaHeight = chartArea.bottom - chartArea.top

      const isImport = seg.action === 'import'
      const yChargeScale = chart.scales.yCharge
      const targetY = yChargeScale
        ? yChargeScale.getPixelForValue(seg.targetPercent)
        : chartArea.bottom - (seg.targetPercent / 100) * areaHeight

      // Zone shading: two-tone for graduated (by-end) zones, flat fill for fixed-rate zones
      const isGraduated = seg.rateMode !== 'fixed'

      if (isGraduated && yChargeScale) {
        // Two-tone shading divided by the ideal charge/discharge trajectory
        const fullStart = seg._fullStart
        const fullEnd = seg._fullEnd
        const totalDuration = fullEnd - fullStart

        // Calculate progress at segment start and end using elapsed minutes
        const startProgress = getElapsedMinutes(seg.startMinutes, fullStart, fullEnd) / totalDuration
        const endProgress = getElapsedMinutes(seg.endMinutes === 1440 && fullEnd > 1440 ? 0 : seg.endMinutes, fullStart, fullEnd) / totalDuration
        // Handle the special case where segment ends at 1440 (midnight boundary of wrapped zone)
        const adjustedEndProgress = seg.endMinutes === 1440 && fullEnd > 1440
          ? (1440 - fullStart) / totalDuration
          : endProgress

        const defaultStart = isImport ? 0 : 100
        const sortedZones = [...zones].sort((a, b) => a.startMinutes - b.startMinutes)
        const precedingZone = sortedZones.find(z => z.ruleId !== seg.ruleId && z.endMinutes === fullStart)
        const baseStartPercent = precedingZone ? precedingZone.targetPercent : defaultStart

        const idealStartPercent = baseStartPercent + startProgress * (seg.targetPercent - baseStartPercent)
        const idealEndPercent = baseStartPercent + adjustedEndProgress * (seg.targetPercent - baseStartPercent)

        const idealStartY = yChargeScale.getPixelForValue(idealStartPercent)
        const idealEndY = yChargeScale.getPixelForValue(idealEndPercent)

        if (isImport) {
          // Above ideal line → target: lighter
          ctx.beginPath()
          ctx.moveTo(startPx, idealStartY)
          ctx.lineTo(endPx, idealEndY)
          ctx.lineTo(endPx, targetY)
          ctx.lineTo(startPx, targetY)
          ctx.closePath()
          ctx.fillStyle = 'rgba(231, 76, 60, 0.10)'
          ctx.fill()

          // Below ideal line → bottom: darker
          ctx.beginPath()
          ctx.moveTo(startPx, idealStartY)
          ctx.lineTo(endPx, idealEndY)
          ctx.lineTo(endPx, chartArea.bottom)
          ctx.lineTo(startPx, chartArea.bottom)
          ctx.closePath()
          ctx.fillStyle = 'rgba(231, 76, 60, 0.25)'
          ctx.fill()
        } else {
          // Above ideal line → top: lighter
          ctx.beginPath()
          ctx.moveTo(startPx, idealStartY)
          ctx.lineTo(endPx, idealEndY)
          ctx.lineTo(endPx, chartArea.top)
          ctx.lineTo(startPx, chartArea.top)
          ctx.closePath()
          ctx.fillStyle = 'rgba(39, 174, 96, 0.10)'
          ctx.fill()

          // Below ideal line → target: darker
          ctx.beginPath()
          ctx.moveTo(startPx, idealStartY)
          ctx.lineTo(endPx, idealEndY)
          ctx.lineTo(endPx, targetY)
          ctx.lineTo(startPx, targetY)
          ctx.closePath()
          ctx.fillStyle = 'rgba(39, 174, 96, 0.25)'
          ctx.fill()
        }
      } else {
        // Flat fill for fixed-rate zones (or fallback when yCharge scale unavailable)
        ctx.fillStyle = isImport
          ? 'rgba(231, 76, 60, 0.2)'
          : 'rgba(39, 174, 96, 0.2)'
        if (isImport) {
          ctx.fillRect(startPx, targetY, width, chartArea.bottom - targetY)
        } else {
          ctx.fillRect(startPx, chartArea.top, width, targetY - chartArea.top)
        }
      }

      // Vertical borders matching zone color
      const isPending = props.pendingRuleIds.has(seg.ruleId)
      ctx.strokeStyle = isImport
        ? 'rgba(231, 76, 60, 0.35)'
        : 'rgba(39, 174, 96, 0.35)'
      ctx.lineWidth = 1.5
      if (isPending) {
        const dashOffset = (Date.now() / 50) % 20
        ctx.setLineDash([8, 4])
        ctx.lineDashOffset = -dashOffset
      } else {
        ctx.setLineDash([])
      }
      ctx.beginPath()
      if (isImport) {
        ctx.moveTo(startPx, targetY)
        ctx.lineTo(startPx, chartArea.bottom)
        ctx.moveTo(endPx, targetY)
        ctx.lineTo(endPx, chartArea.bottom)
      } else {
        ctx.moveTo(startPx, chartArea.top)
        ctx.lineTo(startPx, targetY)
        ctx.moveTo(endPx, chartArea.top)
        ctx.lineTo(endPx, targetY)
      }
      ctx.stroke()

      // Animated dashed outline for pending zones
      if (isPending) {
        const dashOffset = (Date.now() / 50) % 20
        ctx.strokeStyle = isImport
          ? 'rgba(231, 76, 60, 0.5)'
          : 'rgba(39, 174, 96, 0.5)'
        ctx.lineWidth = 2
        ctx.setLineDash([8, 4])
        ctx.lineDashOffset = -dashOffset
        ctx.beginPath()
        if (isImport) {
          ctx.moveTo(startPx, targetY)
          ctx.lineTo(endPx, targetY)
          ctx.lineTo(endPx, chartArea.bottom)
          ctx.lineTo(startPx, chartArea.bottom)
          ctx.closePath()
        } else {
          ctx.moveTo(startPx, chartArea.top)
          ctx.lineTo(endPx, chartArea.top)
          ctx.lineTo(endPx, targetY)
          ctx.lineTo(startPx, targetY)
          ctx.closePath()
        }
        ctx.stroke()
        ctx.setLineDash([])
      }

    }

    ctx.restore()
  },

  // Target lines, arrows, and labels: drawn IN FRONT of dataset lines
  afterDatasetsDraw(chart) {
    const zones = props.zones
    const ctx = chart.ctx
    const chartArea = chart.chartArea
    const xScale = chart.scales.x

    if (!chartArea || !xScale) return

    ctx.save()

    // Flatten zones into display segments, track which is the largest per zone for label placement
    const segmentsByZone = new Map()
    for (const zone of zones) {
      const segs = getDisplaySegments(zone)
      segmentsByZone.set(zone, segs)
    }

    const allSegments = []
    for (const [zone, segs] of segmentsByZone) {
      // Find the largest segment for this zone (where we draw labels)
      let largestIdx = 0
      let largestWidth = 0
      for (let i = 0; i < segs.length; i++) {
        const w = segs[i].endMinutes - segs[i].startMinutes
        if (w > largestWidth) { largestWidth = w; largestIdx = i }
      }
      for (let i = 0; i < segs.length; i++) {
        allSegments.push({ seg: segs[i], zone, isLargest: i === largestIdx })
      }
    }
    allSegments.sort((a, b) => a.seg.startMinutes - b.seg.startMinutes)

    for (const { seg, zone, isLargest } of allSegments) {
      const startPx = xScale.getPixelForValue(seg.startMinutes)
      const endPx = xScale.getPixelForValue(seg.endMinutes)
      const width = endPx - startPx
      const areaHeight = chartArea.bottom - chartArea.top
      const centerX = startPx + width / 2

      const isImport = seg.action === 'import'
      const yChargeScale = chart.scales.yCharge
      const targetY = yChargeScale
        ? yChargeScale.getPixelForValue(seg.targetPercent)
        : chartArea.bottom - (seg.targetPercent / 100) * areaHeight

      // Target line (red for import, green for export)
      ctx.beginPath()
      ctx.strokeStyle = isImport ? 'rgba(231, 76, 60, 0.85)' : 'rgba(39, 174, 96, 0.85)'
      ctx.lineWidth = 1.5
      ctx.moveTo(startPx, targetY)
      ctx.lineTo(endPx, targetY)
      ctx.stroke()

      // Only draw label and arrow on the largest segment
      if (isLargest) {
        const actionLabel = isImport ? 'Import' : 'Export'
        const rateLabel = zone.rateMode === 'fixed'
          ? 'at fixed rate'
          : 'by end of zone'
        const label = `${actionLabel} to ${zone.targetPercent}% ${rateLabel}`
        const textColor = isImport ? '#c0392b' : '#1e8449'
        const arrowColor = isImport ? 'rgba(231, 76, 60, 0.9)' : 'rgba(39, 174, 96, 0.9)'
        const arrowSize = 9
        const arrowGap = 5

        ctx.font = 'bold 11px sans-serif'
        const fullTextWidth = ctx.measureText(label).width
        const shortLabel = `${zone.targetPercent}%`

        let displayLabel = null
        if (width > fullTextWidth + 16) {
          displayLabel = label
        } else {
          const shortWidth = ctx.measureText(shortLabel).width
          if (width > shortWidth + 8) {
            displayLabel = shortLabel
          }
        }

        const arrowTip = arrowGap
        const arrowBase = arrowGap + arrowSize * 1.5
        const textOffset = arrowBase + 5

        const pillBg = 'rgba(255, 255, 255, 0.92)'
        const pillBorder = isImport ? 'rgba(231, 76, 60, 0.5)' : 'rgba(39, 174, 96, 0.5)'
        const labelPadX = 6
        const labelPadY = 3
        const pillRadius = 4

        if (isImport) {
          ctx.beginPath()
          ctx.moveTo(centerX, targetY + arrowTip)
          ctx.lineTo(centerX - arrowSize, targetY + arrowBase)
          ctx.lineTo(centerX + arrowSize, targetY + arrowBase)
          ctx.closePath()
          ctx.fillStyle = arrowColor
          ctx.fill()
          if (displayLabel) {
            const labelW = ctx.measureText(displayLabel).width
            const pillW = labelW + labelPadX * 2
            const pillH = 14 + labelPadY * 2
            let labelCenterX = centerX
            const minCenter = chartArea.left + pillW / 2 + 2
            const maxCenter = chartArea.right - pillW / 2 - 2
            labelCenterX = Math.max(minCenter, Math.min(maxCenter, labelCenterX))
            const pillX = labelCenterX - pillW / 2
            const pillY = targetY + textOffset - labelPadY

            ctx.fillStyle = pillBg
            if (ctx.roundRect) {
              ctx.beginPath()
              ctx.roundRect(pillX, pillY, pillW, pillH, pillRadius)
              ctx.fill()
              ctx.strokeStyle = pillBorder
              ctx.lineWidth = 1
              ctx.stroke()
            } else {
              ctx.fillRect(pillX, pillY, pillW, pillH)
              ctx.strokeStyle = pillBorder
              ctx.lineWidth = 1
              ctx.strokeRect(pillX, pillY, pillW, pillH)
            }

            ctx.fillStyle = textColor
            ctx.textAlign = 'center'
            ctx.textBaseline = 'top'
            ctx.fillText(displayLabel, labelCenterX, targetY + textOffset)
          }
        } else {
          ctx.beginPath()
          ctx.moveTo(centerX, targetY - arrowTip)
          ctx.lineTo(centerX - arrowSize, targetY - arrowBase)
          ctx.lineTo(centerX + arrowSize, targetY - arrowBase)
          ctx.closePath()
          ctx.fillStyle = arrowColor
          ctx.fill()
          if (displayLabel) {
            const labelW = ctx.measureText(displayLabel).width
            const pillW = labelW + labelPadX * 2
            const pillH = 14 + labelPadY * 2
            let labelCenterX = centerX
            const minCenter = chartArea.left + pillW / 2 + 2
            const maxCenter = chartArea.right - pillW / 2 - 2
            labelCenterX = Math.max(minCenter, Math.min(maxCenter, labelCenterX))
            const pillX = labelCenterX - pillW / 2
            const pillY = targetY - textOffset - 14 - labelPadY

            ctx.fillStyle = pillBg
            if (ctx.roundRect) {
              ctx.beginPath()
              ctx.roundRect(pillX, pillY, pillW, pillH, pillRadius)
              ctx.fill()
              ctx.strokeStyle = pillBorder
              ctx.lineWidth = 1
              ctx.stroke()
            } else {
              ctx.fillRect(pillX, pillY, pillW, pillH)
              ctx.strokeStyle = pillBorder
              ctx.lineWidth = 1
              ctx.strokeRect(pillX, pillY, pillW, pillH)
            }

            ctx.fillStyle = textColor
            ctx.textAlign = 'center'
            ctx.textBaseline = 'bottom'
            ctx.fillText(displayLabel, labelCenterX, targetY - textOffset)
          }
        }
      }

      // Boundary type icons at zone edges
      const iconSize = 14
      const iconPad = 3
      const edges = [
        { px: startPx, type: seg.startTimeType },
        { px: endPx, type: seg.endTimeType }
      ]

      for (const edge of edges) {
        const isFixed = edge.type === 'fixed-time'
        const iconY = isImport
          ? chartArea.bottom - iconSize - iconPad
          : chartArea.top + iconPad
        const iconX = edge.px - iconSize / 2

        const cx = iconX + iconSize / 2
        const cy = iconY + iconSize / 2
        const radius = iconSize / 2 + 2
        ctx.beginPath()
        ctx.arc(cx, cy, radius, 0, Math.PI * 2)
        ctx.fillStyle = 'rgba(255, 255, 255, 0.9)'
        ctx.fill()
        ctx.strokeStyle = isImport
          ? 'rgba(231, 76, 60, 0.4)'
          : 'rgba(39, 174, 96, 0.4)'
        ctx.lineWidth = 1
        ctx.stroke()

        const iconColor = isImport ? '#c0392b' : '#1e8449'

        if (isFixed) {
          drawClockIcon(ctx, cx, cy, iconSize * 0.4, iconColor)
        } else {
          drawBoltIcon(ctx, cx, cy, iconSize * 0.4, iconColor)
        }
      }
    }

    ctx.restore()
  }
}

function drawClockIcon(ctx, cx, cy, r, color) {
  ctx.beginPath()
  ctx.arc(cx, cy, r, 0, Math.PI * 2)
  ctx.strokeStyle = color
  ctx.lineWidth = 1.5
  ctx.stroke()
  // Hour hand (pointing to 12)
  ctx.beginPath()
  ctx.moveTo(cx, cy)
  ctx.lineTo(cx, cy - r * 0.65)
  ctx.strokeStyle = color
  ctx.lineWidth = 1.5
  ctx.stroke()
  // Minute hand (pointing to 3)
  ctx.beginPath()
  ctx.moveTo(cx, cy)
  ctx.lineTo(cx + r * 0.5, cy)
  ctx.stroke()
}

function drawBoltIcon(ctx, cx, cy, r, color) {
  const s = r * 1.1
  ctx.beginPath()
  ctx.moveTo(cx + s * 0.1, cy - s)
  ctx.lineTo(cx - s * 0.4, cy + s * 0.1)
  ctx.lineTo(cx + s * 0.05, cy + s * 0.1)
  ctx.lineTo(cx - s * 0.1, cy + s)
  ctx.lineTo(cx + s * 0.4, cy - s * 0.1)
  ctx.lineTo(cx - s * 0.05, cy - s * 0.1)
  ctx.closePath()
  ctx.fillStyle = color
  ctx.fill()
}

// --- Target drag state (arrow dragging) ---
const targetDragState = {
  active: false,
  ruleId: null,
  zone: null,
  startY: 0,
  currentY: 0,
  currentPercent: 0,
  didMove: false
}

// --- Edge drag state (zone start/end time dragging) ---
const edgeDragState = {
  active: false,
  ruleId: null,
  zone: null,
  edge: null, // 'start' or 'end'
  startX: 0,
  currentX: 0,
  currentMinutes: 0,
  didMove: false
}

let suppressNextClick = false
let mouseDownX = null
let mouseDownY = null
const DRAG_THRESHOLD_PX = 10
const ARROW_HIT_RADIUS = 18
const EDGE_HIT_PX = 8
const EDGE_HOLD_DELAY_MS = 200

// Pending edge drag — armed after a hold delay, only then promoted to active
const edgeDragPending = {
  active: false,
  zone: null,
  edge: null,
  startX: 0,
  timerId: null
}

function armEdgeDrag() {
  if (!edgeDragPending.active) return
  edgeDragState.active = true
  edgeDragState.ruleId = edgeDragPending.zone.ruleId
  edgeDragState.zone = edgeDragPending.zone
  edgeDragState.edge = edgeDragPending.edge
  edgeDragState.startX = edgeDragPending.startX
  edgeDragState.currentX = edgeDragPending.startX
  edgeDragState.currentMinutes = edgeDragPending.edge === 'start'
    ? edgeDragPending.zone.startMinutes
    : edgeDragPending.zone.endMinutes
  edgeDragState.didMove = false
  edgeDragPending.active = false
  edgeDragPending.timerId = null
}

function cancelEdgeDragPending() {
  if (edgeDragPending.timerId) {
    clearTimeout(edgeDragPending.timerId)
    edgeDragPending.timerId = null
  }
  edgeDragPending.active = false
  edgeDragPending.zone = null
  edgeDragPending.edge = null
}

/**
 * Check if a point is near a draggable edge of a zone (fixed-time only).
 * Returns { zone, edge } if hit, null otherwise.
 */
function hitTestZoneEdge(x, y, chart) {
  const zones = props.zones
  const xScale = chart.scales.x
  const chartArea = chart.chartArea
  if (!xScale || !chartArea) return null
  if (y < chartArea.top || y > chartArea.bottom) return null

  const hits = []
  for (const zone of zones) {
    // Use display segments so edges are tested at their visual positions
    const segs = getDisplaySegments(zone)
    for (const seg of segs) {
      if (zone.startTimeType === 'fixed-time') {
        const edgePx = xScale.getPixelForValue(seg.startMinutes)
        const dist = Math.abs(x - edgePx)
        if (dist <= EDGE_HIT_PX) {
          hits.push({ zone, edge: 'start', dist, edgePx })
        }
      }
      if (zone.endTimeType === 'fixed-time') {
        const edgePx = xScale.getPixelForValue(seg.endMinutes)
        const dist = Math.abs(x - edgePx)
        if (dist <= EDGE_HIT_PX) {
          hits.push({ zone, edge: 'end', dist, edgePx })
        }
      }
    }
  }

  if (hits.length === 0) return null
  if (hits.length === 1) return hits[0]

  // Sort by distance — closest edge wins
  hits.sort((a, b) => a.dist - b.dist)

  // If top two are at the same pixel (shared boundary), disambiguate:
  // mouse left of edge → prefer 'end' (left zone), mouse right → prefer 'start' (right zone)
  if (Math.abs(hits[0].dist - hits[1].dist) < 1) {
    const edgePx = hits[0].edgePx
    if (x >= edgePx) {
      const startHit = hits.find(h => h.edge === 'start')
      if (startHit) return startHit
    } else {
      const endHit = hits.find(h => h.edge === 'end')
      if (endHit) return endHit
    }
  }

  return hits[0]
}

/**
 * Check if a point is near a zone's arrow.
 * Returns the zone if hit, null otherwise.
 */
function hitTestArrow(x, y, chart) {
  const zones = props.zones
  const xScale = chart.scales.x
  const yChargeScale = chart.scales.yCharge
  if (!xScale || !yChargeScale) return null

  for (const zone of zones) {
    // For wrapped zones, test arrow on the largest display segment
    const segs = getDisplaySegments(zone)
    let largestSeg = segs[0]
    for (const seg of segs) {
      if ((seg.endMinutes - seg.startMinutes) > (largestSeg.endMinutes - largestSeg.startMinutes)) {
        largestSeg = seg
      }
    }

    const startPx = xScale.getPixelForValue(largestSeg.startMinutes)
    const endPx = xScale.getPixelForValue(largestSeg.endMinutes)
    const centerX = startPx + (endPx - startPx) / 2
    const targetY = yChargeScale.getPixelForValue(zone.targetPercent)

    const isImport = zone.action === 'import'
    const arrowGap = 6
    const arrowCenterY = isImport
      ? targetY + arrowGap + 6
      : targetY - arrowGap - 6

    const dx = Math.abs(x - centerX)
    const dy = Math.abs(y - arrowCenterY)
    if (dx < ARROW_HIT_RADIUS && dy < ARROW_HIT_RADIUS) {
      return zone
    }
  }
  return null
}

// Re-entrancy guard
let isUpdating = false

const zoneInteractionPlugin = {
  id: 'zoneInteraction',

  afterDraw(chart) {
    const ctx = chart.ctx
    const chartArea = chart.chartArea
    if (!chartArea) return

    // Draw target drag preview
    if (targetDragState.active && targetDragState.didMove) {
      const zone = targetDragState.zone
      const xScale = chart.scales.x
      const yChargeScale = chart.scales.yCharge
      if (xScale && yChargeScale) {
        const startPx = xScale.getPixelForValue(zone.startMinutes)
        const endPx = xScale.getPixelForValue(zone.endMinutes)
        const width = endPx - startPx
        const newTargetY = yChargeScale.getPixelForValue(targetDragState.currentPercent)
        const isImport = zone.action === 'import'
        const centerX = startPx + width / 2

        ctx.save()

        // Preview dashed target line
        ctx.setLineDash([5, 4])
        ctx.strokeStyle = isImport ? 'rgba(231, 76, 60, 0.6)' : 'rgba(39, 174, 96, 0.6)'
        ctx.lineWidth = 2
        ctx.beginPath()
        ctx.moveTo(startPx, newTargetY)
        ctx.lineTo(endPx, newTargetY)
        ctx.stroke()
        ctx.setLineDash([])

        // Preview arrow
        const arrowSize = 8
        const arrowGap = 6
        const arrowTip = arrowGap
        const arrowBase = arrowGap + arrowSize * 1.5
        const arrowColor = isImport ? 'rgba(231, 76, 60, 0.85)' : 'rgba(39, 174, 96, 0.85)'

        ctx.beginPath()
        if (isImport) {
          ctx.moveTo(centerX, newTargetY + arrowTip)
          ctx.lineTo(centerX - arrowSize, newTargetY + arrowBase)
          ctx.lineTo(centerX + arrowSize, newTargetY + arrowBase)
        } else {
          ctx.moveTo(centerX, newTargetY - arrowTip)
          ctx.lineTo(centerX - arrowSize, newTargetY - arrowBase)
          ctx.lineTo(centerX + arrowSize, newTargetY - arrowBase)
        }
        ctx.closePath()
        ctx.fillStyle = arrowColor
        ctx.fill()

        // Percentage label
        const label = `${targetDragState.currentPercent}%`
        ctx.font = 'bold 12px sans-serif'
        ctx.fillStyle = arrowColor
        ctx.textAlign = 'center'
        const textOffset = arrowBase + 4
        if (isImport) {
          ctx.textBaseline = 'top'
          ctx.fillText(label, centerX, newTargetY + textOffset)
        } else {
          ctx.textBaseline = 'bottom'
          ctx.fillText(label, centerX, newTargetY - textOffset)
        }

        ctx.restore()
      }
    }

    // Draw edge drag preview
    if (edgeDragState.active && edgeDragState.didMove) {
      const zone = edgeDragState.zone
      const xScale = chart.scales.x
      if (xScale) {
        const newEndPx = xScale.getPixelForValue(edgeDragState.currentMinutes)

        ctx.save()

        // Vertical line at new end position
        ctx.setLineDash([5, 4])
        ctx.strokeStyle = 'rgba(128, 128, 128, 0.8)'
        ctx.lineWidth = 2
        ctx.beginPath()
        ctx.moveTo(newEndPx, chartArea.top)
        ctx.lineTo(newEndPx, chartArea.bottom)
        ctx.stroke()
        ctx.setLineDash([])

        // Time label
        const timeText = formatMinutes(edgeDragState.currentMinutes)
        ctx.font = 'bold 11px sans-serif'
        const textW = ctx.measureText(timeText).width
        const pad = 4
        const labelY = chartArea.bottom + 2
        ctx.fillStyle = 'rgba(80, 80, 80, 0.9)'
        if (ctx.roundRect) {
          ctx.beginPath()
          ctx.roundRect(newEndPx - textW / 2 - pad, labelY, textW + pad * 2, 16, 3)
          ctx.fill()
        } else {
          ctx.fillRect(newEndPx - textW / 2 - pad, labelY, textW + pad * 2, 16)
        }
        ctx.fillStyle = '#ffffff'
        ctx.textAlign = 'center'
        ctx.textBaseline = 'top'
        ctx.fillText(timeText, newEndPx, labelY + 2)

        ctx.restore()
      }
    }
  },

  afterEvent(chart, args) {
    if (isUpdating) return
    const event = args.event
    const chartArea = chart.chartArea
    const xScale = chart.scales.x
    const yChargeScale = chart.scales.yCharge
    if (!chartArea || !xScale) return

    // Check if event is within chart area vertically
    const inChartArea = event.y >= chartArea.top && event.y <= chartArea.bottom
      && event.x >= chartArea.left && event.x <= chartArea.right

    if (event.type === 'mousedown' || event.type === 'pointerdown' || event.type === 'touchstart') {
      if (!inChartArea) return

      mouseDownX = event.x
      mouseDownY = event.y

      // Check if mousedown is on an arrow
      const hitArrowZone = hitTestArrow(event.x, event.y, chart)
      if (hitArrowZone) {
        targetDragState.active = true
        targetDragState.ruleId = hitArrowZone.ruleId
        targetDragState.zone = hitArrowZone
        targetDragState.startY = event.y
        targetDragState.currentY = event.y
        targetDragState.currentPercent = hitArrowZone.targetPercent
        targetDragState.didMove = false
        chart.canvas.style.cursor = 'ns-resize'
        return
      }

      // Check if mousedown is on a zone edge (fixed-time only)
      // Don't activate immediately — require a hold delay to avoid accidental drags
      const hitEdge = hitTestZoneEdge(event.x, event.y, chart)
      if (hitEdge) {
        edgeDragPending.active = true
        edgeDragPending.zone = hitEdge.zone
        edgeDragPending.edge = hitEdge.edge
        edgeDragPending.startX = event.x
        edgeDragPending.timerId = setTimeout(() => armEdgeDrag(), EDGE_HOLD_DELAY_MS)
        return
      }

      return
    }

    if (event.type === 'mousemove' || event.type === 'pointermove' || event.type === 'touchmove') {
      // Target arrow drag
      if (targetDragState.active) {
        const dy = Math.abs(event.y - targetDragState.startY)
        if (!targetDragState.didMove && dy < DRAG_THRESHOLD_PX) return

        targetDragState.didMove = true
        targetDragState.currentY = Math.max(chartArea.top, Math.min(event.y, chartArea.bottom))

        if (yChargeScale) {
          const rawPercent = yChargeScale.getValueForPixel(targetDragState.currentY)
          targetDragState.currentPercent = Math.max(0, Math.min(100, Math.round(rawPercent)))
        }

        chart.canvas.style.cursor = 'ns-resize'
        args.changed = true
        return
      }

      // Edge drag
      if (edgeDragState.active) {
        const dx = Math.abs(event.x - edgeDragState.startX)
        if (!edgeDragState.didMove && dx < DRAG_THRESHOLD_PX) return

        edgeDragState.didMove = true
        edgeDragState.currentX = Math.max(chartArea.left, Math.min(event.x, chartArea.right))

        const rawMinutes = xScale.getValueForPixel(edgeDragState.currentX)
        const snapped = Math.round(rawMinutes / 30) * 30
        if (edgeDragState.edge === 'start') {
          edgeDragState.currentMinutes = Math.max(
            0,
            Math.min(edgeDragState.zone.endMinutes - 30, snapped)
          )
        } else {
          edgeDragState.currentMinutes = Math.max(
            edgeDragState.zone.startMinutes + 30,
            Math.min(1440, snapped)
          )
        }

        chart.canvas.style.cursor = 'col-resize'
        args.changed = true
        return
      }

      // Hover cursor
      if (inChartArea) {
        const hitArrowZone = hitTestArrow(event.x, event.y, chart)
        if (hitArrowZone) {
          chart.canvas.style.cursor = 'ns-resize'
        } else {
          const hitEdge = hitTestZoneEdge(event.x, event.y, chart)
          if (hitEdge) {
            chart.canvas.style.cursor = 'col-resize'
          } else {
            const hoverMinutes = xScale.getValueForPixel(event.x)
            const overZone = props.zones.find(z => isMinuteInZone(Math.round(hoverMinutes), z.startMinutes, z.endMinutes))
            chart.canvas.style.cursor = overZone ? 'pointer' : 'crosshair'
          }
        }
      } else {
        chart.canvas.style.cursor = ''
      }
      return
    }

    if (event.type === 'mouseup' || event.type === 'pointerup' || event.type === 'touchend') {
      // Target arrow drag end
      if (targetDragState.active) {
        chart.canvas.style.cursor = ''

        if (targetDragState.didMove) {
          suppressNextClick = true
          emit('update-target', {
            ruleId: targetDragState.ruleId,
            targetPercent: targetDragState.currentPercent
          })
        }

        targetDragState.active = false
        targetDragState.didMove = false
        targetDragState.ruleId = null
        targetDragState.zone = null
        args.changed = true
        return
      }

      // Cancel pending edge drag if mouseup before hold delay
      if (edgeDragPending.active) {
        cancelEdgeDragPending()
        // Let the click event fire normally (will open zone editor)
      }

      // Edge drag end
      if (edgeDragState.active) {
        chart.canvas.style.cursor = ''

        if (edgeDragState.didMove) {
          suppressNextClick = true
          emit('update-edge-time', {
            ruleId: edgeDragState.ruleId,
            edge: edgeDragState.edge,
            minutes: edgeDragState.currentMinutes
          })
        }

        edgeDragState.active = false
        edgeDragState.didMove = false
        edgeDragState.ruleId = null
        edgeDragState.zone = null
        args.changed = true
        return
      }

      return
    }

    if (event.type === 'mouseout') {
      cancelEdgeDragPending()
      if (targetDragState.active) {
        chart.canvas.style.cursor = ''
        targetDragState.active = false
        targetDragState.didMove = false
        targetDragState.ruleId = null
        targetDragState.zone = null
        args.changed = true
        return
      }
      if (edgeDragState.active) {
        chart.canvas.style.cursor = ''
        edgeDragState.active = false
        edgeDragState.didMove = false
        edgeDragState.ruleId = null
        edgeDragState.zone = null
        args.changed = true
      }
      return
    }

    if (event.type === 'click') {
      // Suppress click if we just finished an arrow/edge drag
      if (suppressNextClick) {
        suppressNextClick = false
        mouseDownX = null
        return
      }

      if (!inChartArea) { mouseDownX = null; return }

      // Detect if this click followed a drag (mouse moved significantly from mousedown)
      const wasDrag = mouseDownX !== null &&
        (Math.abs(event.x - mouseDownX) > DRAG_THRESHOLD_PX || Math.abs(event.y - mouseDownY) > DRAG_THRESHOLD_PX)

      if (wasDrag) {
        // Only allow drag-to-create over unzoned time
        const startMinutes = xScale.getValueForPixel(mouseDownX)
        const endMinutes = xScale.getValueForPixel(event.x)
        const dragStart = Math.round(Math.min(startMinutes, endMinutes) / 30) * 30
        const dragEnd = Math.round(Math.max(startMinutes, endMinutes) / 30) * 30
        mouseDownX = null

        if (dragStart >= dragEnd) return

        // Check entire drag range is unzoned
        const overlapsZone = props.zones.some(z => {
          // Check if the drag range [dragStart, dragEnd) overlaps with any segment of this zone
          if (z.endMinutes <= 1440) {
            return dragStart < z.endMinutes && dragEnd > z.startMinutes
          }
          // Wrapped zone: check against both segments
          return (dragStart < 1440 && dragEnd > z.startMinutes) || (dragStart < (z.endMinutes - 1440) && dragEnd > 0)
        })
        if (overlapsZone) return

        // Treat as drag-to-create
        const midMinutes = (dragStart + dragEnd) / 2
        const selection = getSmartSelection(midMinutes)
        if (selection) {
          // Use the dragged range but keep the smart suggestions
          selection.startMinutes = dragStart
          selection.endMinutes = dragEnd
          emit('create-zone', selection)
        } else {
          emit('create-zone', { startMinutes: dragStart, endMinutes: dragEnd })
        }
        return
      }

      mouseDownX = null

      // Don't open editor if clicking on an arrow (drag handles it)
      if (hitTestArrow(event.x, event.y, chart)) return

      // Check if clicked on an existing zone
      const clickMinutes = xScale.getValueForPixel(event.x)
      const clickedZone = props.zones.find(z => isMinuteInZone(Math.round(clickMinutes), z.startMinutes, z.endMinutes))
      if (clickedZone) {
        emit('edit-zone', clickedZone.ruleId)
      } else {
        // Smart select: detect price region and suggest a zone
        const selection = getSmartSelection(clickMinutes)
        if (selection) {
          emit('create-zone', selection)
        }
      }
    }
  }
}

/**
 * Build a rule-type key from a rule type object (mirrors ZoneEditorModal's getRuleTypeKey).
 */
function getRuleTypeKey(ruleType) {
  let key = ruleType.type
  if (ruleType.priceType) key += ':' + ruleType.priceType
  if (ruleType.regionType) key += ':' + ruleType.regionType
  if (ruleType.extremaType) key += ':' + ruleType.extremaType
  return key
}

/**
 * Pick the best rule type key for a boundary, preferring price-based rules
 * that align with the suggested action.
 */
function pickBestRuleTypeKey(availableTypes, action) {
  let bestType = availableTypes[0] // fixed-time fallback
  let bestScore = 0

  for (const rt of availableTypes) {
    if (rt.type === 'fixed-time') continue

    let score = 1 // any price-based rule beats fixed-time

    // Crossover types: strong match when aligned with action
    if (rt.type === 'export-exceeds-import') score += action === 'export' ? 5 : 2
    if (rt.type === 'import-exceeds-export') score += action === 'import' ? 5 : 2

    // Extrema boundary: most descriptive — prefer over crossovers when aligned
    if (rt.type === 'local-extrema-boundary') {
      score += rt.priceType === action ? 6 : 1
    }

    // Peak/trough: prefer matching price type
    if (rt.type === 'local-peak-trough') {
      score += rt.priceType === action ? 3 : 1
    }

    if (score > bestScore) {
      bestScore = score
      bestType = rt
    }
  }

  return getRuleTypeKey(bestType)
}

/**
 * Analyze a time range against pricing data to suggest action, target, and rule types.
 * Used by both the smart-click (region detection) and drag paths.
 */
function analyzePriceRange(startMinutes, endMinutes) {
  const pricingData = props.pricingData
  if (!pricingData || pricingData.length === 0) return {}

  // Find the slot at startMinutes
  let startSlotIndex = -1
  for (let i = pricingData.length - 1; i >= 0; i--) {
    if (pricingData[i].timeMinutes <= startMinutes) {
      startSlotIndex = i
      break
    }
  }
  if (startSlotIndex === -1) return {}

  const currentImport = pricingData[startSlotIndex].importPrice
  const currentExport = pricingData[startSlotIndex].exportPrice

  // Determine action from price change direction at the start boundary
  let suggestedAction = 'import'
  let suggestedTargetPercent = 95

  // Find the price in the slot just before startMinutes (may be a different price level)
  let prevSlotIndex = -1
  for (let i = pricingData.length - 1; i >= 0; i--) {
    if (pricingData[i].timeMinutes < startMinutes) {
      prevSlotIndex = i
      break
    }
  }

  if (prevSlotIndex >= 0) {
    const prevImport = pricingData[prevSlotIndex].importPrice
    const prevExport = pricingData[prevSlotIndex].exportPrice

    if (prevImport !== currentImport || prevExport !== currentExport) {
      // Price changed at this boundary — use direction
      const exportOpportunity = currentExport - prevExport
      const importOpportunity = prevImport - currentImport

      if (exportOpportunity > importOpportunity) {
        suggestedAction = 'export'
        suggestedTargetPercent = 10
      }
    } else {
      // No price change at start — use relative levels
      suggestedAction = getActionFromLevels(currentImport, currentExport, pricingData)
      suggestedTargetPercent = suggestedAction === 'export' ? 10 : 95
    }
  } else {
    // First region of the day
    suggestedAction = getActionFromLevels(currentImport, currentExport, pricingData)
    suggestedTargetPercent = suggestedAction === 'export' ? 10 : 95
  }

  // Pick best rule types for start and end boundaries
  const startRuleTypes = getAvailableRuleTypes(startMinutes, pricingData)
  const endRuleTypes = getAvailableRuleTypes(endMinutes, pricingData)

  return {
    suggestedAction,
    suggestedTargetPercent,
    suggestedStartRuleTypeKey: pickBestRuleTypeKey(startRuleTypes, suggestedAction),
    suggestedEndRuleTypeKey: pickBestRuleTypeKey(endRuleTypes, suggestedAction)
  }
}

/**
 * Fallback: determine action from relative price levels when there's no
 * boundary transition to analyze.
 */
function getActionFromLevels(currentImport, currentExport, pricingData) {
  const importAvg = calculateAveragePrice(pricingData, 'importPrice')
  const exportAvg = calculateAveragePrice(pricingData, 'exportPrice')

  const isCheapImport = importAvg !== null && currentImport <= importAvg * 0.75
  const isExpensiveExport = exportAvg !== null && currentExport >= exportAvg * 1.25

  if (isCheapImport && isExpensiveExport) {
    // Both apply — prefer whichever is further from its threshold
    const importDistance = (importAvg * 0.75 - currentImport) / importAvg
    const exportDistance = (currentExport - exportAvg * 1.25) / exportAvg
    return exportDistance >= importDistance ? 'export' : 'import'
  }
  if (isExpensiveExport) return 'export'
  if (isCheapImport) return 'import'

  // Neither clearly cheap nor expensive — fall back to relative comparison
  if (currentExport > currentImport) return 'export'
  return 'import'
}

/**
 * Smart zone selection: finds the contiguous price region around the click,
 * constrains by existing zones, then analyzes for suggestions.
 */
function getSmartSelection(clickMinutes) {
  const pricingData = props.pricingData
  const zones = props.zones

  if (!pricingData || pricingData.length === 0) return null

  // Find the slot containing the click
  let slotIndex = -1
  for (let i = pricingData.length - 1; i >= 0; i--) {
    if (pricingData[i].timeMinutes <= clickMinutes) {
      slotIndex = i
      break
    }
  }
  if (slotIndex === -1) return null

  const currentSlot = pricingData[slotIndex]
  const currentImport = currentSlot.importPrice
  const currentExport = currentSlot.exportPrice

  // Expand backwards to find region start
  let regionStartIndex = slotIndex
  while (regionStartIndex > 0 &&
    pricingData[regionStartIndex - 1].importPrice === currentImport &&
    pricingData[regionStartIndex - 1].exportPrice === currentExport) {
    regionStartIndex--
  }

  // Expand forwards to find region end
  let regionEndIndex = slotIndex
  while (regionEndIndex < pricingData.length - 1 &&
    pricingData[regionEndIndex + 1].importPrice === currentImport &&
    pricingData[regionEndIndex + 1].exportPrice === currentExport) {
    regionEndIndex++
  }

  let regionStart = pricingData[regionStartIndex].timeMinutes
  let regionEnd = regionEndIndex < pricingData.length - 1
    ? pricingData[regionEndIndex + 1].timeMinutes
    : 1440

  // Constrain by existing zones
  for (const zone of zones) {
    if (zone.endMinutes > regionStart && zone.endMinutes <= clickMinutes) {
      regionStart = Math.max(regionStart, zone.endMinutes)
    }
    if (zone.startMinutes < regionEnd && zone.startMinutes >= clickMinutes) {
      regionEnd = Math.min(regionEnd, zone.startMinutes)
    }
  }

  // Snap to 30-minute boundaries
  regionStart = Math.round(regionStart / 30) * 30
  regionEnd = Math.round(regionEnd / 30) * 30

  if (regionEnd <= regionStart) return null

  return {
    startMinutes: regionStart,
    endMinutes: regionEnd,
    ...analyzePriceRange(regionStart, regionEnd)
  }
}

const chartData = computed(() => {
  const todayOnly = props.pricingData.filter(d => d.timeMinutes <= 1440)
  const importData = todayOnly.map(d => ({ x: d.timeMinutes, y: d.importPrice }))
  const exportData = todayOnly.map(d => ({ x: d.timeMinutes, y: d.exportPrice }))

  const datasets = []

  // Only include battery dataset if we have history data
  if (props.batteryHistory.length > 0) {
    datasets.push({
      label: 'Battery (%)',
      data: props.batteryHistory,
      borderColor: '#3498db',
      borderWidth: 3,
      pointRadius: 0,
      pointHoverRadius: 0,
      fill: false,
      tension: 0,
      yAxisID: 'yCharge',
      order: 1
    })
  }

  datasets.push(
    {
      label: 'Import (p/kWh)',
      data: importData,
      borderColor: '#e74c3c',
      backgroundColor: 'rgba(231, 76, 60, 0.05)',
      borderWidth: 2,
      pointRadius: 0,
      pointHoverRadius: 0,
      fill: false,
      tension: 0,
      stepped: 'before',
      yAxisID: 'yCost',
      order: 2
    },
    {
      label: 'Export (p/kWh)',
      data: exportData,
      borderColor: '#27ae60',
      backgroundColor: 'rgba(39, 174, 96, 0.05)',
      borderWidth: 2,
      pointRadius: 0,
      pointHoverRadius: 0,
      fill: false,
      tension: 0,
      stepped: 'before',
      yAxisID: 'yCost',
      order: 3
    }
  )

  return { datasets }
})

const yCostMax = computed(() => {
  if (!props.pricingData || props.pricingData.length === 0) return undefined
  const allPrices = props.pricingData.filter(d => d.timeMinutes <= 1440).flatMap(d => [d.importPrice, d.exportPrice])
  const max = Math.max(...allPrices)
  const step = 5
  return Math.ceil(max / step) * step + step
})

const chartOptions = computed(() => {
  // Access timeFormat so Vue tracks it as a dependency and recomputes when it changes
  void currentTimeFormat.value

  return {
  responsive: true,
  maintainAspectRatio: false,
  events: ['mousemove', 'mouseout', 'click', 'touchstart', 'touchmove', 'touchend', 'mousedown', 'mouseup'],
  interaction: {
    mode: 'nearest',
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
        title: function (items) {
          if (items.length === 0) return ''
          const minutes = items[0].parsed.x
          return formatMinutes(minutes)
        },
        label: function (context) {
          let label = context.dataset.label || ''
          if (label) label += ': '
          if (context.dataset.yAxisID === 'yCharge') {
            label += Math.round(context.parsed.y) + '%'
          } else {
            label += context.parsed.y.toFixed(1) + 'p'
          }
          return label
        }
      }
    }
  },
  scales: {
    x: {
      type: 'linear',
      min: 0,
      max: 1440,
      grid: {
        color: themeColors.value.gridColor,
        drawBorder: true,
        lineWidth: 1
      },
      ticks: {
        color: themeColors.value.textSecondary,
        maxRotation: 0,
        autoSkip: true,
        stepSize: 120,
        callback: function (value) {
          const h = Math.floor(value / 60)
          const time24 = `${h.toString().padStart(2, '0')}:00`
          return formatTimeDisplay(time24)
        }
      }
    },
    yCharge: {
      type: 'linear',
      position: 'left',
      min: 0,
      max: 100,
      grid: {
        color: themeColors.value.gridColor,
        drawOnChartArea: true,
        lineWidth: 1
      },
      ticks: {
        color: themeColors.value.textSecondary,
        stepSize: 20,
        callback: function (value) {
          return value + '%'
        }
      }
    },
    yCost: {
      type: 'linear',
      position: 'right',
      min: 0,
      max: yCostMax.value,
      grid: {
        drawOnChartArea: false
      },
      ticks: {
        color: themeColors.value.textSecondary,
        stepSize: 5,
        callback: function (value) {
          return value.toFixed(0) + 'p'
        }
      }
    }
  }
}})
</script>

<style scoped>
.battery-chart-container {
  position: relative;
  width: 100%;
  height: 100%;
  min-height: 300px;
  padding: 8px;
}

@media (max-width: 600px) {
  .battery-chart-container {
    padding: 2px 0;
  }
}
</style>

<template>
  <svg class="energy-flow" viewBox="0 0 300 300" xmlns="http://www.w3.org/2000/svg">
    <defs>
      <marker v-for="m in markers" :key="m.id"
        :id="m.id" viewBox="0 0 10 10" refX="0" refY="5"
        :markerWidth="m.size" :markerHeight="m.size"
        markerUnits="userSpaceOnUse"
        orient="auto-start-reverse"
      >
        <path d="M 0 1 L 10 5 L 0 9 Z" :fill="m.color" />
      </marker>
    </defs>

    <!-- Solar (top centre) -->
    <g class="node">
      <circle cx="150" cy="28" r="28" fill="none" :stroke="colors.solar" stroke-width="2" />
      <circle cx="150" cy="28" r="8" :fill="colors.solar" />
      <line v-for="i in 8" :key="'ray-'+i"
        :x1="150 + 12 * Math.cos(i * Math.PI / 4)"
        :y1="28 + 12 * Math.sin(i * Math.PI / 4)"
        :x2="150 + 17 * Math.cos(i * Math.PI / 4)"
        :y2="28 + 17 * Math.sin(i * Math.PI / 4)"
        :stroke="colors.solar" stroke-width="2" stroke-linecap="round"
      />
    </g>

    <!-- Inverter (centre) -->
    <g class="node">
      <circle cx="150" cy="150" r="24" fill="none" :stroke="colors.inverter" stroke-width="2" />
      <!-- Device body -->
      <rect x="138" y="140" width="24" height="18" rx="2" fill="none" :stroke="colors.inverter" stroke-width="1.5" />
      <!-- Blinking LEDs -->
      <circle cx="144" cy="152" r="2" fill="#22c55e" class="led led-1" />
      <circle cx="150" cy="152" r="2" fill="#22c55e" class="led led-2" />
      <circle cx="156" cy="152" r="2" fill="#6b7280" class="led led-3" />
    </g>

    <!-- Battery (left) -->
    <g class="node">
      <circle cx="32" cy="150" r="28" fill="none" :stroke="colors.battery" stroke-width="2" />
      <rect x="21" y="141" width="18" height="12" rx="2" fill="none" :stroke="colors.battery" stroke-width="2" />
      <rect x="39" y="144" width="3" height="6" rx="1" :fill="colors.battery" />
      <!-- Battery fill level -->
      <rect x="23" y="143" :width="batteryFillWidth" height="8" rx="1" :fill="colors.battery" :opacity="batterySoc != null ? 0.5 : 0.4" />
      <!-- Battery SoC below icon -->
      <text v-if="batterySoc != null" x="32" y="165" text-anchor="middle" class="node-value" font-size="9">{{ Math.round(batterySoc) }}%</text>
    </g>

    <!-- Grid (right) -->
    <g class="node">
      <circle cx="268" cy="150" r="28" fill="none" :stroke="gridColor" stroke-width="2" />
      <!-- UK pylon: two tapered legs -->
      <line x1="260" y1="164" x2="264" y2="140" :stroke="gridColor" stroke-width="1.5" />
      <line x1="276" y1="164" x2="272" y2="140" :stroke="gridColor" stroke-width="1.5" />
      <!-- Peak -->
      <line x1="264" y1="140" x2="268" y2="134" :stroke="gridColor" stroke-width="1.5" />
      <line x1="272" y1="140" x2="268" y2="134" :stroke="gridColor" stroke-width="1.5" />
      <!-- Cross-arms (top) -->
      <line x1="256" y1="140" x2="280" y2="140" :stroke="gridColor" stroke-width="1.5" />
      <!-- Cross-arms (lower) -->
      <line x1="258" y1="148" x2="278" y2="148" :stroke="gridColor" stroke-width="1.5" />
      <!-- Cross-bracing -->
      <line x1="262" y1="156" x2="274" y2="148" :stroke="gridColor" stroke-width="1" />
      <line x1="274" y1="156" x2="262" y2="148" :stroke="gridColor" stroke-width="1" />
      <!-- Wires hanging from cross-arms -->
      <path d="M256 140 Q258 143 260 140" fill="none" :stroke="gridColor" stroke-width="1" />
      <path d="M276 140 Q278 143 280 140" fill="none" :stroke="gridColor" stroke-width="1" />
      <path d="M258 148 Q260 151 262 148" fill="none" :stroke="gridColor" stroke-width="1" />
      <path d="M274 148 Q276 151 278 148" fill="none" :stroke="gridColor" stroke-width="1" />
    </g>

    <!-- House (bottom centre) -->
    <g class="node">
      <circle cx="150" cy="268" r="28" fill="none" :stroke="colors.house" stroke-width="2" />
      <!-- Roof -->
      <path d="M133 266 L150 252 L167 266" fill="none" :stroke="colors.house" stroke-width="2" stroke-linejoin="round" stroke-linecap="round" />
      <!-- Walls -->
      <rect x="137" y="266" width="26" height="16" fill="none" :stroke="colors.house" stroke-width="2" stroke-linejoin="round" />
      <!-- Door -->
      <rect x="147" y="273" width="7" height="9" fill="none" :stroke="colors.house" stroke-width="1.5" />
    </g>

    <!-- Flow arrows: Solar → Inverter -->
    <line v-if="solarW > 0"
      x1="150" y1="62" x2="150" :y2="126 - arrowGap(solarW)"
      :stroke="colors.solar"
      :stroke-width="flowWidth(solarW)"
      :marker-end="arrowMarker('solar', solarW)"
    />
    <g v-if="solarW > 0" class="flow-label-group">
      <rect x="125" y="84" width="50" height="16" rx="3" class="flow-label-bg" />
      <text x="150" y="96" text-anchor="middle" class="flow-label">{{ formatPower(solarW) }}</text>
    </g>

    <!-- Flow arrows: Inverter ↔ Battery -->
    <!-- Positive batteryW = charging = arrow from inverter to battery (left) -->
    <line v-if="batteryW > 0"
      x1="126" y1="150" :x2="60 + arrowGap(batteryW)" y2="150"
      :stroke="colors.battery"
      :stroke-width="flowWidth(batteryW)"
      :marker-end="arrowMarker('battery', batteryW)"
    />
    <!-- Negative batteryW = discharging = arrow from battery to inverter (right) -->
    <line v-if="batteryW < 0"
      x1="60" y1="150" :x2="126 - arrowGap(batteryW)" y2="150"
      :stroke="colors.battery"
      :stroke-width="flowWidth(batteryW)"
      :marker-end="arrowMarker('battery', batteryW)"
    />
    <g v-if="batteryW !== 0" class="flow-label-group">
      <rect x="68" y="118" width="50" height="16" rx="3" class="flow-label-bg" />
      <text x="93" y="130" text-anchor="middle" class="flow-label">{{ formatPower(Math.abs(batteryW)) }}</text>
    </g>

    <!-- Flow arrows: Inverter ↔ Grid -->
    <!-- Positive gridW = importing = arrow from grid to inverter (left) -->
    <line v-if="gridW > 0"
      x1="240" y1="150" :x2="174 + arrowGap(gridW)" y2="150"
      :stroke="gridColor"
      :stroke-width="flowWidth(gridW)"
      :marker-end="arrowMarker('gridImport', gridW)"
    />
    <!-- Negative gridW = exporting = arrow from inverter to grid (right) -->
    <line v-if="gridW < 0"
      x1="174" y1="150" :x2="240 - arrowGap(gridW)" y2="150"
      :stroke="gridColor"
      :stroke-width="flowWidth(gridW)"
      :marker-end="arrowMarker('gridExport', gridW)"
    />
    <g v-if="gridW !== 0" class="flow-label-group">
      <rect x="182" y="118" width="50" height="16" rx="3" class="flow-label-bg" />
      <text x="207" y="130" text-anchor="middle" class="flow-label">{{ formatPower(Math.abs(gridW)) }}</text>
    </g>

    <!-- Flow arrows: Inverter → House -->
    <line v-if="houseW > 0"
      x1="150" y1="174" x2="150" :y2="240 - arrowGap(houseW)"
      :stroke="colors.house"
      :stroke-width="flowWidth(houseW)"
      :marker-end="arrowMarker('house', houseW)"
    />
    <g v-if="houseW > 0" class="flow-label-group">
      <rect x="125" y="199" width="50" height="16" rx="3" class="flow-label-bg" />
      <text x="150" y="211" text-anchor="middle" class="flow-label">{{ formatPower(houseW) }}</text>
    </g>
  </svg>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({
  solarW: { type: Number, default: 0 },
  batteryW: { type: Number, default: 0 },
  houseW: { type: Number, default: 0 },
  gridW: { type: Number, default: 0 },
  batterySoc: { type: Number, default: null }
})

const batteryFillWidth = computed(() => {
  if (props.batterySoc == null) return 9
  // Fill width: 0% → 0, 100% → 14 (full inner width of battery rect)
  return Math.max(0, Math.min(14, (props.batterySoc / 100) * 14))
})

const colors = {
  solar: '#f59e0b',
  house: '#3b82f6',
  battery: '#8b5cf6',
  inverter: '#6b7280',
  import: '#ef4444',
  export: '#22c55e'
}

const gridColor = computed(() => props.gridW > 0 ? colors.import : colors.export)

const markers = computed(() => {
  const list = []
  if (props.solarW > 0) {
    list.push({ id: 'arrow-solar', color: colors.solar, size: markerSize(props.solarW) })
  }
  if (props.batteryW !== 0) {
    list.push({ id: 'arrow-battery', color: colors.battery, size: markerSize(props.batteryW) })
  }
  if (props.gridW > 0) {
    list.push({ id: 'arrow-gridImport', color: colors.import, size: markerSize(props.gridW) })
  }
  if (props.gridW < 0) {
    list.push({ id: 'arrow-gridExport', color: colors.export, size: markerSize(props.gridW) })
  }
  if (props.houseW > 0) {
    list.push({ id: 'arrow-house', color: colors.house, size: markerSize(props.houseW) })
  }
  return list
})

function formatPower(watts) {
  if (Math.abs(watts) >= 1000) {
    return (watts / 1000).toFixed(1) + ' kW'
  }
  return Math.round(watts) + ' W'
}

function flowWidth(watts) {
  const absW = Math.abs(watts)
  if (absW < 10) return 1.5
  // Logarithmic scale: 10W → 1.5, ~5000W → 12
  const t = Math.min(1, Math.log10(absW / 10) / Math.log10(500))
  return 1.5 + t * 10.5
}

function markerSize(watts) {
  const absW = Math.abs(watts)
  if (absW < 10) return 8
  // Logarithmic scale: 10W → 8, ~5000W → 24 (absolute SVG units)
  const t = Math.min(1, Math.log10(absW / 10) / Math.log10(500))
  return 8 + t * 16
}

function arrowGap(watts) {
  // markerSize + small gap so arrow tip "just nearly" touches the target
  return markerSize(watts) + 2
}

function arrowMarker(name, watts) {
  if (Math.abs(watts) === 0) return ''
  return `url(#arrow-${name})`
}
</script>

<style scoped>
.energy-flow {
  width: 100%;
  max-width: 320px;
  margin: 0 auto;
  display: block;
}

.node-value {
  font-size: 11px;
  font-weight: 600;
  fill: var(--text-primary);
}

.led {
  animation: blink 2s infinite;
}

.led-1 {
  animation-delay: 0s;
}

.led-2 {
  animation-delay: 0.6s;
}

.led-3 {
  animation-delay: 1.2s;
}

@keyframes blink {
  0%, 40%, 100% { opacity: 1; }
  20% { opacity: 0.2; }
}

.flow-label-bg {
  fill: var(--bg-secondary);
  fill-opacity: 0.85;
  stroke: var(--border-color);
  stroke-width: 1;
}

.flow-label {
  font-size: 10px;
  font-weight: 600;
  fill: var(--text-primary);
}
</style>

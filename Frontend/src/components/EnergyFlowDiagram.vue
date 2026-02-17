<template>
  <svg class="energy-flow" viewBox="0 0 300 300" xmlns="http://www.w3.org/2000/svg">
    <defs>
      <marker v-for="m in markers" :key="m.id"
        :id="m.id" viewBox="0 0 10 10" refX="9" refY="5"
        :markerWidth="m.size" :markerHeight="m.size"
        orient="auto-start-reverse"
      >
        <path d="M 0 1 L 10 5 L 0 9 Z" :fill="m.color" />
      </marker>
    </defs>

    <!-- Solar (top centre) -->
    <g class="node">
      <circle cx="150" cy="32" r="28" fill="none" :stroke="colors.solar" stroke-width="2" />
      <circle cx="150" cy="28" r="8" :fill="colors.solar" />
      <line v-for="i in 8" :key="'ray-'+i"
        :x1="150 + 12 * Math.cos(i * Math.PI / 4)"
        :y1="28 + 12 * Math.sin(i * Math.PI / 4)"
        :x2="150 + 17 * Math.cos(i * Math.PI / 4)"
        :y2="28 + 17 * Math.sin(i * Math.PI / 4)"
        :stroke="colors.solar" stroke-width="2" stroke-linecap="round"
      />
      <text x="150" y="52" text-anchor="middle" class="node-value">{{ formatPower(solarW) }}</text>
    </g>

    <!-- Inverter (centre) -->
    <g class="node">
      <circle cx="150" cy="150" r="24" fill="none" :stroke="colors.inverter" stroke-width="2" />
      <text x="150" y="147" text-anchor="middle" :fill="colors.inverter" font-size="8" font-weight="600">INV</text>
      <text x="150" y="158" text-anchor="middle" :fill="colors.inverter" font-size="7">~</text>
    </g>

    <!-- Battery (left) -->
    <g class="node">
      <circle cx="32" cy="150" r="28" fill="none" :stroke="colors.battery" stroke-width="2" />
      <rect x="21" y="143" width="18" height="12" rx="2" fill="none" :stroke="colors.battery" stroke-width="2" />
      <rect x="39" y="146" width="3" height="6" rx="1" :fill="colors.battery" />
      <rect x="23" y="145" width="9" height="8" rx="1" :fill="colors.battery" opacity="0.4" />
      <text x="32" y="172" text-anchor="middle" class="node-value">{{ formatPower(Math.abs(batteryW)) }}</text>
    </g>

    <!-- Grid (right) -->
    <g class="node">
      <circle cx="268" cy="150" r="28" fill="none" :stroke="gridColor" stroke-width="2" />
      <line x1="261" y1="158" x2="268" y2="138" :stroke="gridColor" stroke-width="2" />
      <line x1="275" y1="158" x2="268" y2="138" :stroke="gridColor" stroke-width="2" />
      <line x1="263" y1="151" x2="273" y2="151" :stroke="gridColor" stroke-width="1.5" />
      <line x1="264" y1="146" x2="272" y2="146" :stroke="gridColor" stroke-width="1.5" />
      <line x1="265" y1="141" x2="271" y2="141" :stroke="gridColor" stroke-width="1.5" />
      <text x="268" y="172" text-anchor="middle" class="node-value">{{ formatPower(Math.abs(gridW)) }}</text>
    </g>

    <!-- House (bottom centre) -->
    <g class="node">
      <circle cx="150" cy="268" r="28" fill="none" :stroke="colors.house" stroke-width="2" />
      <path d="M138 270 L150 254 L162 270 Z" fill="none" :stroke="colors.house" stroke-width="2" stroke-linejoin="round" />
      <rect x="143" y="266" width="14" height="11" fill="none" :stroke="colors.house" stroke-width="2" />
      <text x="150" y="289" text-anchor="middle" class="node-value">{{ formatPower(houseW) }}</text>
    </g>

    <!-- Flow arrows: Solar → Inverter -->
    <line v-if="solarW > 0"
      x1="150" y1="62" x2="150" y2="124"
      :stroke="colors.solar"
      :stroke-width="flowWidth(solarW)"
      :marker-end="arrowMarker('solar', solarW)"
    />

    <!-- Flow arrows: Inverter ↔ Battery -->
    <!-- Positive batteryW = charging = arrow from inverter to battery (left) -->
    <line v-if="batteryW > 0"
      x1="124" y1="150" x2="62" y2="150"
      :stroke="colors.battery"
      :stroke-width="flowWidth(batteryW)"
      :marker-end="arrowMarker('battery', batteryW)"
    />
    <!-- Negative batteryW = discharging = arrow from battery to inverter (right) -->
    <line v-if="batteryW < 0"
      x1="62" y1="150" x2="124" y2="150"
      :stroke="colors.battery"
      :stroke-width="flowWidth(batteryW)"
      :marker-end="arrowMarker('battery', batteryW)"
    />

    <!-- Flow arrows: Inverter ↔ Grid -->
    <!-- Positive gridW = importing = arrow from grid to inverter (left) -->
    <line v-if="gridW > 0"
      x1="238" y1="150" x2="176" y2="150"
      :stroke="gridColor"
      :stroke-width="flowWidth(gridW)"
      :marker-end="arrowMarker('gridImport', gridW)"
    />
    <!-- Negative gridW = exporting = arrow from inverter to grid (right) -->
    <line v-if="gridW < 0"
      x1="176" y1="150" x2="238" y2="150"
      :stroke="gridColor"
      :stroke-width="flowWidth(gridW)"
      :marker-end="arrowMarker('gridExport', gridW)"
    />

    <!-- Flow arrows: Inverter → House -->
    <line v-if="houseW > 0"
      x1="150" y1="176" x2="150" y2="238"
      :stroke="colors.house"
      :stroke-width="flowWidth(houseW)"
      :marker-end="arrowMarker('house', houseW)"
    />
  </svg>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({
  solarW: { type: Number, default: 0 },
  batteryW: { type: Number, default: 0 },
  houseW: { type: Number, default: 0 },
  gridW: { type: Number, default: 0 }
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
  if (absW < 100) return 2
  if (absW < 500) return 3
  if (absW < 1500) return 4
  if (absW < 3000) return 6
  return 8
}

function markerSize(watts) {
  const absW = Math.abs(watts)
  if (absW < 100) return 4
  if (absW < 500) return 5
  if (absW < 1500) return 6
  if (absW < 3000) return 7
  return 8
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
</style>

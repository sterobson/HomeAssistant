<template>
  <div class="modal-overlay" @click.self="$emit('close')">
    <div class="modal-panel">
      <div class="modal-header">
        <h3>Logic circuit</h3>
        <button class="btn-close" @click="$emit('close')">
          <svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor">
            <path d="M2.146 2.854a.5.5 0 1 1 .708-.708L8 7.293l5.146-5.147a.5.5 0 0 1 .708.708L8.707 8l5.147 5.146a.5.5 0 0 1-.708.708L8 8.707l-5.146 5.147a.5.5 0 0 1-.708-.708L7.293 8z"/>
          </svg>
        </button>
      </div>

      <div class="modal-body">
        <svg
          v-if="diagramData.length > 0"
          :viewBox="`0 0 ${svgWidth} ${svgHeight}`"
          class="circuit-svg"
          preserveAspectRatio="xMidYMid meet"
        >
          <!-- Wires -->
          <path
            v-for="(wire, i) in wires"
            :key="'wire-' + i"
            :d="wire.path"
            fill="none"
            :stroke="wire.active ? '#2d6a4f' : '#666'"
            stroke-width="1.5"
            :stroke-dasharray="wire.active ? '' : '4,3'"
          />

          <!-- Rule nodes (left) -->
          <g v-for="rule in ruleNodes" :key="'rule-' + rule.id">
            <rect
              :x="rule.x"
              :y="rule.y"
              :width="ruleNodeWidth"
              :height="nodeHeight"
              rx="8"
              :fill="rule.active ? '#2d6a4f20' : 'var(--bg-secondary)'"
              :stroke="rule.active ? '#2d6a4f' : 'var(--border-color)'"
              stroke-width="1.5"
            />
            <text
              :x="rule.x + ruleNodeWidth / 2"
              :y="rule.y + nodeHeight / 2 + 1"
              text-anchor="middle"
              dominant-baseline="middle"
              :fill="rule.active ? '#2d6a4f' : 'var(--text-secondary)'"
              font-size="11"
              font-weight="500"
            >{{ truncate(rule.name, 14) }}</text>
            <text
              :x="rule.x + ruleNodeWidth / 2"
              :y="rule.y + 10"
              text-anchor="middle"
              :fill="rule.active ? '#2d6a4f' : 'var(--text-tertiary)'"
              font-size="8"
              font-weight="600"
              text-transform="uppercase"
            >{{ rule.type }}</text>
          </g>

          <!-- Gate symbols (center) -->
          <g v-for="gate in gateNodes" :key="'gate-' + gate.deviceId">
            <rect
              :x="gate.x"
              :y="gate.y"
              :width="gateWidth"
              :height="nodeHeight"
              rx="4"
              fill="var(--bg-tertiary)"
              stroke="var(--border-color)"
              stroke-width="1"
            />
            <text
              :x="gate.x + gateWidth / 2"
              :y="gate.y + nodeHeight / 2 + 1"
              text-anchor="middle"
              dominant-baseline="middle"
              fill="var(--text-secondary)"
              font-size="10"
              font-weight="700"
            >{{ gate.combinator === 0 ? 'AND' : 'OR' }}</text>
          </g>

          <!-- Device nodes (right) -->
          <g v-for="device in deviceNodes" :key="'device-' + device.id">
            <rect
              :x="device.x"
              :y="device.y"
              :width="deviceNodeWidth"
              :height="nodeHeight"
              rx="8"
              fill="var(--bg-secondary)"
              stroke="var(--color-primary)"
              stroke-width="1.5"
            />
            <text
              :x="device.x + deviceNodeWidth / 2"
              :y="device.y + nodeHeight / 2 + 1"
              text-anchor="middle"
              dominant-baseline="middle"
              fill="var(--text-primary)"
              font-size="11"
              font-weight="500"
            >{{ truncate(device.name, 14) }}</text>
          </g>
        </svg>

        <div v-else class="empty-state">
          <p>No devices with rules to display</p>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({
  rooms: {
    type: Array,
    default: () => []
  },
  rules: {
    type: Array,
    default: () => []
  }
})

defineEmits(['close'])

const ruleNodeWidth = 120
const deviceNodeWidth = 120
const gateWidth = 40
const nodeHeight = 36
const verticalGap = 12
const horizontalGap = 60

// Build diagram data: only devices that have rules bound
const diagramData = computed(() => {
  const result = []
  for (const room of props.rooms) {
    for (const device of room.devices) {
      if (device.ruleIds.length > 0) {
        const boundRules = device.ruleIds
          .map(id => props.rules.find(r => r.id === id))
          .filter(Boolean)
        if (boundRules.length > 0) {
          result.push({ device, boundRules, roomName: room.name })
        }
      }
    }
  }
  return result
})

// Collect unique rules used
const usedRuleIds = computed(() => {
  const ids = new Set()
  for (const item of diagramData.value) {
    for (const rule of item.boundRules) {
      ids.add(rule.id)
    }
  }
  return [...ids]
})

// Layout
const ruleNodes = computed(() => {
  return usedRuleIds.value.map((id, i) => {
    const rule = props.rules.find(r => r.id === id)
    return {
      ...rule,
      x: 10,
      y: i * (nodeHeight + verticalGap) + 10,
      active: false // could be driven by live state later
    }
  })
})

const deviceNodes = computed(() => {
  const x = ruleNodeWidth + horizontalGap + gateWidth + horizontalGap + 10
  return diagramData.value.map((item, i) => ({
    ...item.device,
    x,
    y: i * (nodeHeight + verticalGap) + 10
  }))
})

const gateNodes = computed(() => {
  const x = ruleNodeWidth + horizontalGap + 10
  return diagramData.value.map((item, i) => ({
    deviceId: item.device.id,
    combinator: item.device.ruleCombinator,
    x,
    y: i * (nodeHeight + verticalGap) + 10
  }))
})

const wires = computed(() => {
  const result = []
  const ruleMap = new Map(ruleNodes.value.map(r => [r.id, r]))

  for (let i = 0; i < diagramData.value.length; i++) {
    const item = diagramData.value[i]
    const gate = gateNodes.value[i]
    const device = deviceNodes.value[i]

    // Gate to device wire
    const gateRightX = gate.x + gateWidth
    const gateCenterY = gate.y + nodeHeight / 2
    const deviceLeftX = device.x
    const deviceCenterY = device.y + nodeHeight / 2
    result.push({
      path: `M ${gateRightX} ${gateCenterY} L ${deviceLeftX} ${deviceCenterY}`,
      active: false
    })

    // Rule to gate wires
    for (const rule of item.boundRules) {
      const ruleNode = ruleMap.get(rule.id)
      if (ruleNode) {
        const ruleRightX = ruleNode.x + ruleNodeWidth
        const ruleCenterY = ruleNode.y + nodeHeight / 2
        const gateLeftX = gate.x
        result.push({
          path: `M ${ruleRightX} ${ruleCenterY} C ${ruleRightX + 30} ${ruleCenterY}, ${gateLeftX - 30} ${gateCenterY}, ${gateLeftX} ${gateCenterY}`,
          active: false
        })
      }
    }
  }
  return result
})

const svgWidth = computed(() => ruleNodeWidth + horizontalGap + gateWidth + horizontalGap + deviceNodeWidth + 20)
const svgHeight = computed(() => {
  const maxItems = Math.max(usedRuleIds.value.length, diagramData.value.length, 1)
  return maxItems * (nodeHeight + verticalGap) + 20
})

function truncate(text, maxLen) {
  return text.length > maxLen ? text.slice(0, maxLen - 1) + '\u2026' : text
}
</script>

<style scoped>
.modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.5);
  z-index: 1000;
  display: flex;
  align-items: center;
  justify-content: center;
  animation: fadeIn 0.2s;
}

.modal-panel {
  background: var(--bg-primary);
  border-radius: 12px;
  width: 90%;
  max-width: 600px;
  max-height: 80vh;
  display: flex;
  flex-direction: column;
  animation: slideUp 0.2s;
}

.modal-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1rem 1.25rem;
  border-bottom: 1px solid var(--border-color);
}

.modal-header h3 {
  margin: 0;
  font-size: 1.1rem;
  font-weight: 600;
  color: var(--text-primary);
}

.btn-close {
  background: none;
  border: none;
  color: var(--text-tertiary);
  cursor: pointer;
  padding: 0.25rem;
  border-radius: 4px;
}

.btn-close:hover {
  color: var(--text-primary);
}

.modal-body {
  padding: 1.25rem;
  overflow: auto;
  flex: 1;
}

.circuit-svg {
  width: 100%;
  height: auto;
}

.empty-state {
  text-align: center;
  padding: 2rem;
  color: var(--text-tertiary);
}

@keyframes fadeIn {
  from { opacity: 0; }
  to { opacity: 1; }
}

@keyframes slideUp {
  from { transform: translateY(20px); opacity: 0; }
  to { transform: translateY(0); opacity: 1; }
}
</style>

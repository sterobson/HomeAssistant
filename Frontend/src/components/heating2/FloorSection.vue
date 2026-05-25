<template>
  <div
    class="floor-section"
    :class="{ 'drag-over': isDragOver }"
    @dragover.prevent="handleDragOver"
    @dragleave="handleDragLeave"
    @drop.prevent="handleDrop"
  >
    <div class="floor-rooms">
      <div
        v-for="(room, index) in rooms"
        :key="room.id"
        class="room-cell"
        :class="{
          'drag-source': draggedRoomId === room.id,
          'drop-before': dropIndex === index && dropIndex !== null,
          'expanded': expandedRoomId === room.id
        }"
        draggable="true"
        @dragstart="handleDragStart($event, room, index)"
        @dragend="handleDragEnd"
        @dragover.prevent.stop="handleRoomDragOver($event, index)"
        @click="$emit('toggle-expand', room.id)"
      >
        <div class="room-content">
          <span class="room-name">{{ room.name }}</span>
          <span v-if="roomTemperature(room)" class="room-temp">{{ roomTemperature(room) }}°</span>
          <div class="room-meta">
            <span class="priority-dots">
              <span v-for="n in 3" :key="n" class="dot" :class="{ active: n <= room.priority }"></span>
            </span>
          </div>
        </div>

        <!-- Expanded detail panel -->
        <div v-if="expandedRoomId === room.id" class="room-details" @click.stop>
          <div class="detail-section">
            <div class="section-header">
              <span>Devices</span>
              <button class="btn btn-add-sm" @click="$emit('add-device', room.id)">+ Add</button>
            </div>
            <div v-if="room.devices.length === 0" class="empty-detail">No devices</div>
            <div v-for="device in room.devices" :key="device.id" class="detail-item">
              <div class="detail-item-info">
                <span class="device-type-tag" :class="'type-' + device.type">{{ deviceTypeLabel(device.type) }}</span>
                <span class="detail-name">{{ device.name }}</span>
                <span v-if="device.ruleIds && device.ruleIds.length > 0" class="rule-count" @click.stop="$emit('edit-rule-bindings', { roomId: room.id, device })">
                  {{ device.ruleIds.length }} rule{{ device.ruleIds.length !== 1 ? 's' : '' }}
                </span>
              </div>
              <div class="detail-actions">
                <button class="btn-icon-xs" title="Rules" @click.stop="$emit('edit-rule-bindings', { roomId: room.id, device })">
                  <svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor"><path d="M6 0a.5.5 0 0 1 .5.5V3h3V.5a.5.5 0 0 1 1 0V3h1a.5.5 0 0 1 .5.5v3A3.5 3.5 0 0 1 8.5 10c-.002.434-.01.845-.04 1.22-.041.514-.126 1.003-.317 1.424a2.08 2.08 0 0 1-.97 1.028C6.725 13.9 6.169 14 5.5 14c-.998 0-1.61.33-1.974.718A1.92 1.92 0 0 0 3 16H2c0-.616.232-1.367.797-1.968C3.374 13.42 4.261 13 5.5 13c.581 0 .962-.088 1.218-.219.241-.123.4-.3.514-.55.121-.266.193-.621.23-1.09.027-.34.035-.718.037-1.141A3.5 3.5 0 0 1 4 6.5v-3a.5.5 0 0 1 .5-.5h1V.5A.5.5 0 0 1 6 0M5 4v2.5A2.5 2.5 0 0 0 7.5 9h1A2.5 2.5 0 0 0 11 6.5V4z"/></svg>
                </button>
                <button class="btn-icon-xs" title="Edit" @click.stop="$emit('edit-device', { roomId: room.id, device })">
                  <svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor"><path d="M12.146.146a.5.5 0 0 1 .708 0l3 3a.5.5 0 0 1 0 .708l-10 10a.5.5 0 0 1-.168.11l-5 2a.5.5 0 0 1-.65-.65l2-5a.5.5 0 0 1 .11-.168zM11.207 2.5 13.5 4.793 14.793 3.5 12.5 1.207zm1.586 3L10.5 3.207 4 9.707V10h.5a.5.5 0 0 1 .5.5v.5h.5a.5.5 0 0 1 .5.5v.5h.293z"/></svg>
                </button>
                <button class="btn-icon-xs btn-danger-xs" title="Delete" @click.stop="$emit('delete-device', { roomId: room.id, deviceId: device.id })">
                  <svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor"><path d="M2.146 2.854a.5.5 0 1 1 .708-.708L8 7.293l5.146-5.147a.5.5 0 0 1 .708.708L8.707 8l5.147 5.146a.5.5 0 0 1-.708.708L8 8.707l-5.146 5.147a.5.5 0 0 1-.708-.708L7.293 8z"/></svg>
                </button>
              </div>
            </div>
          </div>

          <div class="detail-section">
            <div class="section-header">
              <span>Schedules</span>
              <button class="btn btn-add-sm" @click="$emit('add-schedule', room.id)">+ Add</button>
            </div>
            <div v-if="room.schedules.length === 0" class="empty-detail">No schedules</div>
            <div v-for="schedule in sortedSchedules(room)" :key="schedule.id" class="detail-item">
              <div class="detail-item-info">
                <span class="schedule-time">{{ schedule.time }}</span>
                <span class="schedule-temp">{{ schedule.temperature }}°C</span>
                <span class="schedule-days">{{ formatDays(schedule.days) }}</span>
              </div>
              <div class="detail-actions">
                <button class="btn-icon-xs" title="Edit" @click.stop="$emit('edit-schedule', { roomId: room.id, schedule })">
                  <svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor"><path d="M12.146.146a.5.5 0 0 1 .708 0l3 3a.5.5 0 0 1 0 .708l-10 10a.5.5 0 0 1-.168.11l-5 2a.5.5 0 0 1-.65-.65l2-5a.5.5 0 0 1 .11-.168zM11.207 2.5 13.5 4.793 14.793 3.5 12.5 1.207zm1.586 3L10.5 3.207 4 9.707V10h.5a.5.5 0 0 1 .5.5v.5h.5a.5.5 0 0 1 .5.5v.5h.293z"/></svg>
                </button>
                <button class="btn-icon-xs btn-danger-xs" title="Delete" @click.stop="$emit('delete-schedule', { roomId: room.id, scheduleId: schedule.id })">
                  <svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor"><path d="M2.146 2.854a.5.5 0 1 1 .708-.708L8 7.293l5.146-5.147a.5.5 0 0 1 .708.708L8.707 8l5.147 5.146a.5.5 0 0 1-.708.708L8 8.707l-5.146 5.147a.5.5 0 0 1-.708-.708L7.293 8z"/></svg>
                </button>
              </div>
            </div>
          </div>

          <div class="room-footer-actions">
            <button class="btn btn-sm btn-secondary" @click.stop="$emit('edit-room', room)">Edit room</button>
            <button class="btn btn-sm btn-danger" @click.stop="$emit('delete-room', room.id)">Delete</button>
          </div>
        </div>
      </div>

      <!-- Drop zone at end -->
      <div
        v-if="isDragOver && rooms.length > 0"
        class="drop-zone-end"
        :class="{ 'drop-active': dropIndex === rooms.length }"
        @dragover.prevent.stop="dropIndex = rooms.length"
      ></div>
    </div>
  </div>
</template>

<script setup>
import { ref } from 'vue'

const props = defineProps({
  floor: {
    type: Number,
    required: true
  },
  rooms: {
    type: Array,
    required: true
  },
  expandedRoomId: {
    type: String,
    default: null
  },
  rules: {
    type: Array,
    default: () => []
  },
  getEntityState: {
    type: Function,
    default: () => null
  }
})

const emit = defineEmits([
  'edit-room',
  'delete-room',
  'toggle-expand',
  'add-device',
  'edit-device',
  'delete-device',
  'add-schedule',
  'edit-schedule',
  'delete-schedule',
  'edit-rule-bindings',
  'room-dropped'
])

const isDragOver = ref(false)
const dropIndex = ref(null)
const draggedRoomId = ref(null)
let dragOverCounter = 0

function roomTemperature(room) {
  // Prefer temperature sensor (type 2), fall back to climate entity (valve type 0)
  const tempSensor = room.devices.find(d => d.type === 2 && d.sensorEntityId)
  if (tempSensor) {
    const state = props.getEntityState(tempSensor.sensorEntityId)
    if (state != null && state !== 'unavailable' && state !== 'unknown') return state
  }
  const climateDevice = room.devices.find(d => d.type === 0 && d.targetEntityId)
  if (climateDevice) {
    const state = props.getEntityState(climateDevice.targetEntityId)
    if (state != null && state !== 'unavailable' && state !== 'unknown') return state
  }
  return null
}

function handleDragStart(event, room, index) {
  draggedRoomId.value = room.id
  event.dataTransfer.effectAllowed = 'move'
  event.dataTransfer.setData('application/x-room-id', room.id)
  event.dataTransfer.setData('application/x-source-floor', String(props.floor))
}

function handleDragEnd() {
  draggedRoomId.value = null
  isDragOver.value = false
  dropIndex.value = null
  dragOverCounter = 0
}

function handleDragOver(event) {
  dragOverCounter++
  isDragOver.value = true
  event.dataTransfer.dropEffect = 'move'
}

function handleDragLeave() {
  dragOverCounter--
  if (dragOverCounter <= 0) {
    isDragOver.value = false
    dropIndex.value = null
    dragOverCounter = 0
  }
}

function handleRoomDragOver(event, index) {
  isDragOver.value = true
  const rect = event.currentTarget.getBoundingClientRect()
  const midX = rect.left + rect.width / 2
  dropIndex.value = event.clientX < midX ? index : index + 1
}

function handleDrop(event) {
  const roomId = event.dataTransfer.getData('application/x-room-id')
  if (roomId) {
    const targetIndex = dropIndex.value ?? props.rooms.length
    emit('room-dropped', { roomId, targetFloor: props.floor, targetIndex })
  }
  isDragOver.value = false
  dropIndex.value = null
  dragOverCounter = 0
}

const DEVICE_TYPE_LABELS = {
  0: 'Valve', 1: 'Boiler', 2: 'Temp', 3: 'Humidity',
  4: 'Heater', 5: 'Presence', 6: 'Plug'
}

function deviceTypeLabel(type) {
  return DEVICE_TYPE_LABELS[type] || 'Unknown'
}

const DAY_FLAGS = { 1: 'M', 2: 'T', 4: 'W', 8: 'Th', 16: 'F', 32: 'Sa', 64: 'Su' }

function formatDays(daysBitmask) {
  if (daysBitmask === 127) return 'Every day'
  if (daysBitmask === 31) return 'Weekdays'
  if (daysBitmask === 96) return 'Weekends'
  const days = []
  for (const [flag, label] of Object.entries(DAY_FLAGS)) {
    if (daysBitmask & parseInt(flag)) days.push(label)
  }
  return days.join(', ') || 'None'
}

function sortedSchedules(room) {
  return [...room.schedules].sort((a, b) => a.time.localeCompare(b.time))
}
</script>

<style scoped>
.floor-section {
  position: relative;
  min-height: 100px;
  border-bottom: 2px solid var(--text-tertiary);
  transition: background 0.15s;
}

.floor-section:last-child {
  border-bottom: none;
}

.floor-section.drag-over {
  background: var(--color-primary)08;
}

.floor-rooms {
  display: flex;
  min-height: 100px;
}

.room-cell {
  flex: 1;
  min-width: 0;
  border-right: 2px solid var(--text-tertiary);
  display: flex;
  flex-direction: column;
  cursor: pointer;
  transition: background 0.15s, opacity 0.15s;
  position: relative;
  user-select: none;
}

.room-cell:last-child {
  border-right: none;
}

.room-cell:hover {
  background: var(--bg-secondary);
}

.room-cell.expanded {
  background: var(--bg-secondary);
}

.room-cell.drag-source {
  opacity: 0.3;
}

.room-cell.drop-before::before {
  content: '';
  position: absolute;
  left: -2px;
  top: 0;
  bottom: 0;
  width: 3px;
  background: var(--color-primary);
  z-index: 2;
}

.drop-zone-end {
  width: 3px;
  flex-shrink: 0;
  transition: width 0.15s;
}

.drop-zone-end.drop-active {
  width: 3px;
  background: var(--color-primary);
}

.room-content {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 1rem 0.5rem;
  min-height: 80px;
}

.room-name {
  font-weight: 500;
  font-size: 0.95rem;
  color: var(--text-primary);
  text-align: center;
  word-break: break-word;
  line-height: 1.3;
}

.room-temp {
  font-size: 1.4rem;
  font-weight: 600;
  color: var(--color-primary);
  margin-top: 0.2rem;
  font-variant-numeric: tabular-nums;
}

.room-meta {
  margin-top: 0.4rem;
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.priority-dots {
  display: flex;
  gap: 3px;
}

.dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: var(--border-color);
}

.dot.active {
  background: var(--color-primary);
}

/* Expanded details */
.room-details {
  padding: 0.75rem;
  border-top: 1px solid var(--border-color);
  background: var(--bg-primary);
}

.detail-section {
  margin-bottom: 0.75rem;
}

.section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 0.4rem;
}

.section-header span {
  font-size: 0.75rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--text-tertiary);
}

.btn-add-sm {
  font-size: 0.75rem;
  padding: 0.15rem 0.5rem;
  border: 1px dashed var(--border-color);
  border-radius: 4px;
  background: none;
  color: var(--text-tertiary);
  cursor: pointer;
}

.btn-add-sm:hover {
  border-color: var(--color-primary);
  color: var(--color-primary);
}

.empty-detail {
  font-size: 0.8rem;
  color: var(--text-tertiary);
  padding: 0.4rem 0;
}

.detail-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.35rem 0.5rem;
  border-radius: 4px;
  margin-bottom: 0.2rem;
}

.detail-item:hover {
  background: var(--bg-tertiary);
}

.detail-item-info {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  min-width: 0;
  flex: 1;
}

.device-type-tag {
  font-size: 0.6rem;
  font-weight: 600;
  padding: 0.1rem 0.3rem;
  border-radius: 3px;
  text-transform: uppercase;
  flex-shrink: 0;
}

.type-0 { background: #e8590720; color: #e85907; }
.type-1 { background: #c9252520; color: #c92525; }
.type-2 { background: #1971c220; color: #1971c2; }
.type-3 { background: #0c859920; color: #0c8599; }
.type-4 { background: #e8590720; color: #e85907; }
.type-5 { background: #2d6a4f20; color: #2d6a4f; }
.type-6 { background: #6741d920; color: #6741d9; }

.detail-name {
  font-size: 0.85rem;
  color: var(--text-primary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.rule-count {
  font-size: 0.7rem;
  padding: 0.1rem 0.35rem;
  border-radius: 8px;
  background: var(--color-primary);
  color: white;
  flex-shrink: 0;
  cursor: pointer;
}

.detail-actions {
  display: flex;
  gap: 0.15rem;
  flex-shrink: 0;
}

.btn-icon-xs {
  padding: 0.2rem;
  background: none;
  border: none;
  color: var(--text-tertiary);
  cursor: pointer;
  border-radius: 3px;
  display: flex;
  align-items: center;
}

.btn-icon-xs:hover {
  color: var(--text-primary);
  background: var(--bg-tertiary);
}

.btn-danger-xs:hover {
  color: var(--color-danger) !important;
}

.schedule-time {
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--text-primary);
  font-variant-numeric: tabular-nums;
}

.schedule-temp {
  font-size: 0.85rem;
  color: var(--color-primary);
  font-weight: 500;
}

.schedule-days {
  font-size: 0.75rem;
  color: var(--text-tertiary);
}

.room-footer-actions {
  display: flex;
  gap: 0.5rem;
  padding-top: 0.5rem;
  border-top: 1px solid var(--border-color);
}

.btn {
  padding: 0.4rem 0.75rem;
  border: none;
  border-radius: 6px;
  font-size: 0.8rem;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.15s;
}

.btn-sm {
  padding: 0.3rem 0.6rem;
  font-size: 0.8rem;
}

.btn-secondary {
  background: var(--bg-tertiary);
  color: var(--text-primary);
  border: 1px solid var(--border-color);
}

.btn-secondary:hover {
  border-color: var(--border-hover);
}

.btn-danger {
  background: none;
  color: var(--color-danger);
  border: 1px solid transparent;
}

.btn-danger:hover {
  background: #c9252510;
}
</style>

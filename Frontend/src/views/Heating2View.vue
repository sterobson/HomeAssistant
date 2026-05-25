<template>
  <div class="heating2-view">
    <div v-if="loading" class="loading-state">
      <div class="spinner"></div>
      <p>Loading heating configuration...</p>
    </div>

    <div v-else-if="error" class="error-state">
      <svg width="48" height="48" viewBox="0 0 16 16" fill="currentColor">
        <path d="M8 0a8 8 0 100 16A8 8 0 008 0zM7 4h2v5H7V4zm0 6h2v2H7v-2z"/>
      </svg>
      <p>{{ error }}</p>
      <button class="btn btn-retry" @click="retryLoad">Retry</button>
    </div>

    <div v-else class="house-container">
      <div class="house-header">
        <h2>Rooms</h2>
        <div class="header-actions">
          <button class="btn btn-icon" title="Manage rules" @click="showRulesPanel = true">
            <svg width="18" height="18" viewBox="0 0 16 16" fill="currentColor">
              <path d="M6 0a.5.5 0 0 1 .5.5V3h3V.5a.5.5 0 0 1 1 0V3h1a.5.5 0 0 1 .5.5v3A3.5 3.5 0 0 1 8.5 10c-.002.434-.01.845-.04 1.22-.041.514-.126 1.003-.317 1.424a2.08 2.08 0 0 1-.97 1.028C6.725 13.9 6.169 14 5.5 14c-.998 0-1.61.33-1.974.718A1.92 1.92 0 0 0 3 16H2c0-.616.232-1.367.797-1.968C3.374 13.42 4.261 13 5.5 13c.581 0 .962-.088 1.218-.219.241-.123.4-.3.514-.55.121-.266.193-.621.23-1.09.027-.34.035-.718.037-1.141A3.5 3.5 0 0 1 4 6.5v-3a.5.5 0 0 1 .5-.5h1V.5A.5.5 0 0 1 6 0M5 4v2.5A2.5 2.5 0 0 0 7.5 9h1A2.5 2.5 0 0 0 11 6.5V4z"/>
            </svg>
          </button>
          <button class="btn btn-primary btn-sm" @click="openAddRoom">
            <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor">
              <path d="M8 4a.5.5 0 0 1 .5.5v3h3a.5.5 0 0 1 0 1h-3v3a.5.5 0 0 1-1 0v-3h-3a.5.5 0 0 1 0-1h3v-3A.5.5 0 0 1 8 4"/>
            </svg>
            Add room
          </button>
        </div>
      </div>

      <div v-if="rooms.length === 0" class="empty-state">
        <svg width="64" height="64" viewBox="0 0 16 16" fill="currentColor">
          <path d="M8.707 1.5a1 1 0 0 0-1.414 0L.646 8.146a.5.5 0 0 0 .708.708L2 8.207V13.5A1.5 1.5 0 0 0 3.5 15h9a1.5 1.5 0 0 0 1.5-1.5V8.207l.646.647a.5.5 0 0 0 .708-.708L13 5.793V2.5a.5.5 0 0 0-.5-.5h-1a.5.5 0 0 0-.5.5v1.293zM13 7.207V13.5a.5.5 0 0 1-.5.5h-9a.5.5 0 0 1-.5-.5V7.207l5-5z"/>
        </svg>
        <p>No rooms configured yet</p>
        <p class="empty-hint">Add your first room to get started</p>
      </div>

      <div v-else class="house-wrapper">
        <!-- Roof -->
        <div class="house-roof">
          <svg viewBox="0 0 200 60" preserveAspectRatio="none" class="roof-svg">
            <polygon
              points="100,2 2,58 198,58"
              fill="var(--bg-primary)"
              stroke="var(--text-tertiary)"
              stroke-width="2"
              stroke-linejoin="round"
            />
            <!-- Chimney -->
            <rect x="140" y="18" width="16" height="40" fill="var(--bg-primary)" stroke="var(--text-tertiary)" stroke-width="2" />
            <rect x="140" y="58" width="16" height="2" fill="var(--bg-primary)" />
          </svg>
        </div>

        <!-- House body -->
        <div class="house-body">
          <FloorSection
            v-for="floorGroup in roomsByFloor"
            :key="floorGroup.floor"
            :floor="floorGroup.floor"
            :rooms="floorGroup.rooms"
            :expanded-room-id="expandedRoomId"
            :rules="rules"
            :get-entity-state="getEntityState"
            @edit-room="openEditRoom"
            @delete-room="handleDeleteRoom"
            @toggle-expand="handleToggleExpand"
            @add-device="openAddDevice"
            @edit-device="openEditDevice"
            @delete-device="handleDeleteDevice"
            @add-schedule="openAddSchedule"
            @edit-schedule="openEditSchedule"
            @delete-schedule="handleDeleteSchedule"
            @edit-rule-bindings="openRuleBindings"
            @room-dropped="handleRoomDropped"
          />
        </div>
      </div>
    </div>

    <RoomEditorModal
      v-if="showRoomEditor"
      :room="editingRoom"
      @save="handleSaveRoom"
      @close="showRoomEditor = false"
    />

    <DeviceEditorModal
      v-if="showDeviceEditor"
      :device="editingDevice"
      :room-id="editingRoomId"
      @save="handleSaveDevice"
      @close="showDeviceEditor = false"
    />

    <RuleEditorModal
      v-if="showRuleEditor"
      :rule="editingRule"
      @save="handleSaveRule"
      @close="showRuleEditor = false"
    />

    <RuleBindingPanel
      v-if="showRuleBindingPanel"
      :device="bindingDevice"
      :room-id="bindingRoomId"
      :rules="rules"
      @close="showRuleBindingPanel = false"
      @bind="handleBindRule"
      @unbind="handleUnbindRule"
      @set-combinator="handleSetCombinator"
      @add-rule="openAddRuleFromBinding"
    />

    <Heating2ScheduleEditor
      v-if="showScheduleEditor"
      :schedule="editingSchedule"
      :room-id="editingRoomId"
      @save="handleSaveSchedule"
      @close="showScheduleEditor = false"
    />

    <!-- Rules panel overlay -->
    <div v-if="showRulesPanel" class="rules-panel-overlay" @click.self="showRulesPanel = false">
      <div class="rules-panel">
        <div class="rules-panel-header">
          <h3>Automation rules</h3>
          <button class="btn btn-icon" @click="showRulesPanel = false">
            <svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor">
              <path d="M2.146 2.854a.5.5 0 1 1 .708-.708L8 7.293l5.146-5.147a.5.5 0 0 1 .708.708L8.707 8l5.147 5.146a.5.5 0 0 1-.708.708L8 8.707l-5.146 5.147a.5.5 0 0 1-.708-.708L7.293 8z"/>
            </svg>
          </button>
        </div>
        <div class="rules-list">
          <div v-if="rules.length === 0" class="empty-rules">
            <p>No rules defined yet</p>
          </div>
          <div v-for="rule in rules" :key="rule.id" class="rule-item">
            <div class="rule-info">
              <span class="rule-type-badge" :class="'rule-type-' + rule.type">{{ rule.type }}</span>
              <span class="rule-name">{{ rule.name }}</span>
            </div>
            <div class="rule-actions">
              <button class="btn btn-icon-sm" title="Edit" @click="openEditRule(rule)">
                <svg width="14" height="14" viewBox="0 0 16 16" fill="currentColor">
                  <path d="M12.146.146a.5.5 0 0 1 .708 0l3 3a.5.5 0 0 1 0 .708l-10 10a.5.5 0 0 1-.168.11l-5 2a.5.5 0 0 1-.65-.65l2-5a.5.5 0 0 1 .11-.168zM11.207 2.5 13.5 4.793 14.793 3.5 12.5 1.207zm1.586 3L10.5 3.207 4 9.707V10h.5a.5.5 0 0 1 .5.5v.5h.5a.5.5 0 0 1 .5.5v.5h.293z"/>
                </svg>
              </button>
              <button class="btn btn-icon-sm btn-danger-icon" title="Delete" @click="handleDeleteRule(rule.id)">
                <svg width="14" height="14" viewBox="0 0 16 16" fill="currentColor">
                  <path d="M5.5 5.5A.5.5 0 0 1 6 6v6a.5.5 0 0 1-1 0V6a.5.5 0 0 1 .5-.5m2.5 0a.5.5 0 0 1 .5.5v6a.5.5 0 0 1-1 0V6a.5.5 0 0 1 .5-.5m3 .5a.5.5 0 0 0-1 0v6a.5.5 0 0 0 1 0z"/>
                  <path d="M14.5 3a1 1 0 0 1-1 1H13v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V4h-.5a1 1 0 0 1-1-1V2a1 1 0 0 1 1-1H5.5l1-1h3l1 1h2.5a1 1 0 0 1 1 1zM4.118 4 4 4.059V13a1 1 0 0 0 1 1h6a1 1 0 0 0 1-1V4.059L11.882 4zM2.5 3h11V2h-11z"/>
                </svg>
              </button>
            </div>
          </div>
        </div>
        <button class="btn btn-primary btn-full" @click="openAddRule">
          <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor">
            <path d="M8 4a.5.5 0 0 1 .5.5v3h3a.5.5 0 0 1 0 1h-3v3a.5.5 0 0 1-1 0v-3h-3a.5.5 0 0 1 0-1h3v-3A.5.5 0 0 1 8 4"/>
          </svg>
          Add rule
        </button>
      </div>
    </div>

    <LogicCircuitDiagram
      v-if="showLogicDiagram"
      :rooms="rooms"
      :rules="rules"
      @close="showLogicDiagram = false"
    />

    <Toast
      v-if="toast.visible"
      :message="toast.message"
      :type="toast.type"
      :duration="toast.duration"
      @close="toast.visible = false"
    />
  </div>
</template>

<script setup>
import { ref, onMounted, onUnmounted, reactive, computed } from 'vue'
import FloorSection from '../components/heating2/FloorSection.vue'
import RoomEditorModal from '../components/heating2/RoomEditorModal.vue'
import DeviceEditorModal from '../components/heating2/DeviceEditorModal.vue'
import RuleEditorModal from '../components/heating2/RuleEditorModal.vue'
import RuleBindingPanel from '../components/heating2/RuleBindingPanel.vue'
import Heating2ScheduleEditor from '../components/heating2/Heating2ScheduleEditor.vue'
import LogicCircuitDiagram from '../components/heating2/LogicCircuitDiagram.vue'
import Toast from '../components/Toast.vue'
import { useHeatingConfig } from '../composables/useHeatingConfig.js'
import { useDeviceSettings } from '../composables/useDeviceSettings.js'
import { useSignalR } from '../composables/useSignalR.js'
import { getHouseId } from '../utils/cookies.js'

const {
  loading,
  saving,
  error,
  rooms,
  rules,
  roomsByFloor,
  loadConfig,
  forceReloadConfig,
  addRoom,
  updateRoom,
  deleteRoom,
  addDevice,
  updateDevice,
  deleteDevice,
  addRule,
  updateRule,
  deleteRule,
  bindRuleToDevice,
  unbindRuleFromDevice,
  setDeviceRuleCombinator,
  addSchedule,
  updateSchedule,
  deleteSchedule,
  reorderRoomOnFloor,
  getRuleById
} = useHeatingConfig()

const { entities, loadEntities } = useDeviceSettings()

function getEntityState(entityId) {
  if (!entityId) return null
  const entity = entities.value.find(e => e.entityId === entityId)
  return entity?.state ?? null
}

const houseId = computed(() => getHouseId())
const expandedRoomId = ref(null)
const lastHiddenTime = ref(null)

// Modal state
const showRoomEditor = ref(false)
const editingRoom = ref(null)

const showDeviceEditor = ref(false)
const editingDevice = ref(null)
const editingRoomId = ref(null)

const showRuleEditor = ref(false)
const editingRule = ref(null)

const showRuleBindingPanel = ref(false)
const bindingDevice = ref(null)
const bindingRoomId = ref(null)

const showScheduleEditor = ref(false)
const editingSchedule = ref(null)

const showRulesPanel = ref(false)
const showLogicDiagram = ref(false)

const toast = reactive({
  visible: false,
  message: '',
  type: 'success',
  duration: 3000
})

// SignalR
let signalR = null

const initializeConnection = async () => {
  if (!houseId.value) return

  if (signalR) {
    signalR.off('heating-config-changed')
    await signalR.disconnect()
  }

  signalR = useSignalR(houseId.value)

  try {
    await signalR.connect()
    signalR.on('heating-config-changed', () => {
      forceReloadConfig()
      showToast('Configuration updated', 'info', 2000)
    })
  } catch (err) {
    console.error('Failed to connect to SignalR:', err)
  }
}

// Visibility API
const handleVisibilityChange = async () => {
  if (document.hidden) {
    lastHiddenTime.value = Date.now()
  } else {
    if (lastHiddenTime.value) {
      const hiddenDuration = (Date.now() - lastHiddenTime.value) / 1000
      if (hiddenDuration > 30) {
        if (signalR && houseId.value) {
          try {
            await signalR.ensureConnected()
          } catch (err) {
            console.error('Failed to ensure SignalR connection:', err)
          }
        }
        await forceReloadConfig()
        showToast('Data refreshed', 'info', 2000)
      }
    }
    lastHiddenTime.value = null
  }
}

onMounted(async () => {
  if (!houseId.value) {
    return
  }
  await Promise.all([loadConfig(), loadEntities()])
  await initializeConnection()
  document.addEventListener('visibilitychange', handleVisibilityChange)
})

onUnmounted(async () => {
  document.removeEventListener('visibilitychange', handleVisibilityChange)
  if (signalR) {
    signalR.off('heating-config-changed')
    await signalR.disconnect()
  }
})

const retryLoad = () => {
  forceReloadConfig()
}

// Room handlers
const openAddRoom = () => {
  editingRoom.value = null
  showRoomEditor.value = true
}

const openEditRoom = (room) => {
  editingRoom.value = { ...room }
  showRoomEditor.value = true
}

const handleSaveRoom = async (roomData) => {
  try {
    if (roomData.id) {
      await updateRoom(roomData.id, roomData)
      showToast('Room updated', 'success')
    } else {
      await addRoom(roomData)
      showToast('Room added', 'success')
    }
    showRoomEditor.value = false
  } catch (err) {
    showToast('Failed to save room', 'error')
  }
}

const handleDeleteRoom = async (roomId) => {
  try {
    await deleteRoom(roomId)
    if (expandedRoomId.value === roomId) {
      expandedRoomId.value = null
    }
    showToast('Room deleted', 'success')
  } catch (err) {
    showToast('Failed to delete room', 'error')
  }
}

const handleToggleExpand = (roomId) => {
  expandedRoomId.value = expandedRoomId.value === roomId ? null : roomId
}

const handleRoomDropped = async ({ roomId, targetFloor, targetIndex }) => {
  try {
    await reorderRoomOnFloor(roomId, targetFloor, targetIndex)
    showToast('Room moved', 'success', 2000)
  } catch (err) {
    showToast('Failed to move room', 'error')
  }
}

// Device handlers
const openAddDevice = (roomId) => {
  editingDevice.value = null
  editingRoomId.value = roomId
  showDeviceEditor.value = true
}

const openEditDevice = ({ roomId, device }) => {
  editingDevice.value = { ...device }
  editingRoomId.value = roomId
  showDeviceEditor.value = true
}

const handleSaveDevice = async (deviceData) => {
  try {
    if (deviceData.id && editingDevice.value) {
      await updateDevice(editingRoomId.value, deviceData.id, deviceData)
      showToast('Device updated', 'success')
    } else {
      await addDevice(editingRoomId.value, deviceData)
      showToast('Device added', 'success')
    }
    showDeviceEditor.value = false
  } catch (err) {
    showToast('Failed to save device', 'error')
  }
}

const handleDeleteDevice = async ({ roomId, deviceId }) => {
  try {
    await deleteDevice(roomId, deviceId)
    showToast('Device deleted', 'success')
  } catch (err) {
    showToast('Failed to delete device', 'error')
  }
}

// Rule handlers
const openAddRule = () => {
  editingRule.value = null
  showRuleEditor.value = true
}

const openAddRuleFromBinding = () => {
  editingRule.value = null
  showRuleEditor.value = true
}

const openEditRule = (rule) => {
  editingRule.value = { ...rule }
  showRuleEditor.value = true
}

const handleSaveRule = async (ruleData) => {
  try {
    if (ruleData.id && editingRule.value) {
      await updateRule(ruleData.id, ruleData)
      showToast('Rule updated', 'success')
    } else {
      await addRule(ruleData)
      showToast('Rule added', 'success')
    }
    showRuleEditor.value = false
  } catch (err) {
    showToast('Failed to save rule', 'error')
  }
}

const handleDeleteRule = async (ruleId) => {
  try {
    await deleteRule(ruleId)
    showToast('Rule deleted', 'success')
  } catch (err) {
    showToast('Failed to delete rule', 'error')
  }
}

// Rule binding handlers
const openRuleBindings = ({ roomId, device }) => {
  bindingDevice.value = device
  bindingRoomId.value = roomId
  showRuleBindingPanel.value = true
}

const handleBindRule = async ({ ruleId }) => {
  try {
    await bindRuleToDevice(bindingRoomId.value, bindingDevice.value.id, ruleId)
    // Update local reference
    bindingDevice.value = rooms.value
      .find(r => r.id === bindingRoomId.value)
      ?.devices.find(d => d.id === bindingDevice.value.id)
  } catch (err) {
    showToast('Failed to bind rule', 'error')
  }
}

const handleUnbindRule = async ({ ruleId }) => {
  try {
    await unbindRuleFromDevice(bindingRoomId.value, bindingDevice.value.id, ruleId)
    bindingDevice.value = rooms.value
      .find(r => r.id === bindingRoomId.value)
      ?.devices.find(d => d.id === bindingDevice.value.id)
  } catch (err) {
    showToast('Failed to unbind rule', 'error')
  }
}

const handleSetCombinator = async ({ combinator }) => {
  try {
    await setDeviceRuleCombinator(bindingRoomId.value, bindingDevice.value.id, combinator)
    bindingDevice.value = rooms.value
      .find(r => r.id === bindingRoomId.value)
      ?.devices.find(d => d.id === bindingDevice.value.id)
  } catch (err) {
    showToast('Failed to update combinator', 'error')
  }
}

// Schedule handlers
const openAddSchedule = (roomId) => {
  editingSchedule.value = null
  editingRoomId.value = roomId
  showScheduleEditor.value = true
}

const openEditSchedule = ({ roomId, schedule }) => {
  editingSchedule.value = { ...schedule }
  editingRoomId.value = roomId
  showScheduleEditor.value = true
}

const handleSaveSchedule = async (scheduleData) => {
  try {
    if (scheduleData.id && editingSchedule.value) {
      await updateSchedule(editingRoomId.value, scheduleData.id, scheduleData)
      showToast('Schedule updated', 'success')
    } else {
      await addSchedule(editingRoomId.value, scheduleData)
      showToast('Schedule added', 'success')
    }
    showScheduleEditor.value = false
  } catch (err) {
    showToast('Failed to save schedule', 'error')
  }
}

const handleDeleteSchedule = async ({ roomId, scheduleId }) => {
  try {
    await deleteSchedule(roomId, scheduleId)
    showToast('Schedule deleted', 'success')
  } catch (err) {
    showToast('Failed to delete schedule', 'error')
  }
}

const showToast = (message, type = 'success', duration = 3000) => {
  toast.message = message
  toast.type = type
  toast.duration = duration
  toast.visible = true
}
</script>

<style scoped>
.heating2-view {
  width: 100%;
}

.loading-state,
.error-state,
.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 3rem 1rem;
  text-align: center;
  color: var(--text-secondary);
}

.spinner {
  width: 48px;
  height: 48px;
  border: 4px solid var(--border-color);
  border-top: 4px solid var(--color-primary);
  border-radius: 50%;
  animation: spin 1s linear infinite;
}

@keyframes spin {
  0% { transform: rotate(0deg); }
  100% { transform: rotate(360deg); }
}

.error-state svg {
  color: var(--color-danger);
  margin-bottom: 1rem;
}

.error-state p {
  margin-bottom: 1rem;
  font-size: 1.1rem;
}

.empty-state svg {
  color: var(--text-tertiary);
  margin-bottom: 1rem;
}

.empty-state p {
  font-size: 1.1rem;
}

.empty-hint {
  font-size: 0.9rem !important;
  color: var(--text-tertiary);
  margin-top: 0.25rem;
}

.house-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 1rem;
}

.house-header h2 {
  font-size: 1.2rem;
  font-weight: 600;
  color: var(--text-primary);
  margin: 0;
}

.header-actions {
  display: flex;
  gap: 0.5rem;
  align-items: center;
}

.house-wrapper {
  position: relative;
}

.house-roof {
  position: relative;
  margin: 0 -2px;
  z-index: 1;
}

.roof-svg {
  display: block;
  width: 100%;
  height: auto;
}

.house-body {
  display: flex;
  flex-direction: column;
  border: 2px solid var(--text-tertiary);
  border-top: none;
  margin-top: -2px;
  overflow: hidden;
}

.btn {
  padding: 0.5rem 1rem;
  border: none;
  border-radius: 6px;
  font-size: 0.9rem;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.4rem;
}

.btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.btn-primary {
  background-color: var(--color-primary);
  color: white;
}

.btn-primary:hover:not(:disabled) {
  background-color: var(--color-primary-hover);
}

.btn-sm {
  padding: 0.4rem 0.75rem;
  font-size: 0.85rem;
}

.btn-full {
  width: 100%;
}

.btn-retry {
  background-color: var(--color-primary);
  color: white;
  padding: 0.75rem 1.5rem;
  font-size: 1rem;
}

.btn-retry:hover:not(:disabled) {
  background-color: var(--color-primary-hover);
}

.btn-icon {
  padding: 0.4rem;
  background: var(--bg-tertiary);
  color: var(--text-secondary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
}

.btn-icon:hover {
  color: var(--text-primary);
  border-color: var(--border-hover);
}

.btn-icon-sm {
  padding: 0.25rem;
  background: none;
  border: none;
  color: var(--text-tertiary);
  cursor: pointer;
  border-radius: 4px;
}

.btn-icon-sm:hover {
  color: var(--text-primary);
  background: var(--bg-tertiary);
}

.btn-danger-icon:hover {
  color: var(--color-danger) !important;
}

/* Rules panel */
.rules-panel-overlay {
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

.rules-panel {
  background: var(--bg-primary);
  border-radius: 12px;
  width: 90%;
  max-width: 500px;
  max-height: 80vh;
  display: flex;
  flex-direction: column;
  animation: slideUp 0.2s;
}

.rules-panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1rem 1.25rem;
  border-bottom: 1px solid var(--border-color);
}

.rules-panel-header h3 {
  margin: 0;
  font-size: 1.1rem;
  font-weight: 600;
  color: var(--text-primary);
}

.rules-list {
  flex: 1;
  overflow-y: auto;
  padding: 0.75rem;
}

.empty-rules {
  text-align: center;
  padding: 2rem;
  color: var(--text-tertiary);
}

.rule-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.6rem 0.75rem;
  border-radius: 8px;
  background: var(--bg-secondary);
  margin-bottom: 0.5rem;
}

.rule-info {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  min-width: 0;
}

.rule-type-badge {
  font-size: 0.7rem;
  font-weight: 600;
  text-transform: uppercase;
  padding: 0.15rem 0.4rem;
  border-radius: 4px;
  flex-shrink: 0;
}

.rule-type-presence {
  background: #2d6a4f20;
  color: #2d6a4f;
}

.rule-type-power {
  background: #e8590720;
  color: #e85907;
}

.rule-type-time {
  background: #1971c220;
  color: #1971c2;
}

.rule-name {
  font-size: 0.9rem;
  color: var(--text-primary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.rule-actions {
  display: flex;
  gap: 0.25rem;
  flex-shrink: 0;
}

.rules-panel > .btn-full {
  margin: 0.75rem;
  margin-top: 0;
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

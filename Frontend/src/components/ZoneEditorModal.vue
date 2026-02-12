<template>
  <div class="zone-overlay" @click.self="handleCancel">
    <div class="zone-modal">
      <div class="zone-header">
        <h3>{{ isEditing ? 'Edit zone' : 'Add zone' }}</h3>
        <button class="close-btn" @click="handleCancel">
          <svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor">
            <path d="M4.646 4.646a.5.5 0 01.708 0L10 9.293l4.646-4.647a.5.5 0 01.708.708L10.707 10l4.647 4.646a.5.5 0 01-.708.708L10 10.707l-4.646 4.647a.5.5 0 01-.708-.708L9.293 10 4.646 5.354a.5.5 0 010-.708z"/>
          </svg>
        </button>
      </div>

      <div class="zone-form">
        <!-- Start time -->
        <div class="form-group">
          <label>Start time</label>
          <div class="inline-group">
            <select v-model="startRuleTypeKey" class="rule-select">
              <option
                v-for="ruleType in groupedRuleTypes.ungrouped"
                :key="getRuleTypeKey(ruleType)"
                :value="getRuleTypeKey(ruleType)"
              >
                {{ ruleType.label }}
              </option>
              <optgroup label="Import rate">
                <option
                  v-for="ruleType in groupedRuleTypes.import"
                  :key="getRuleTypeKey(ruleType)"
                  :value="getRuleTypeKey(ruleType)"
                >
                  {{ ruleType.label }}
                </option>
              </optgroup>
              <optgroup label="Export rate">
                <option
                  v-for="ruleType in groupedRuleTypes.export"
                  :key="getRuleTypeKey(ruleType)"
                  :value="getRuleTypeKey(ruleType)"
                >
                  {{ ruleType.label }}
                </option>
              </optgroup>
            </select>
            <div v-if="startRuleTypeKey === 'fixed-time'" class="time-editor">
              <select v-model.number="startHour" class="time-select">
                <option v-for="h in 24" :key="h - 1" :value="h - 1">
                  {{ String(h - 1).padStart(2, '0') }}
                </option>
              </select>
              <span class="time-separator">:</span>
              <select v-model.number="startMinute" class="time-select">
                <option v-for="m in minuteOptions" :key="m" :value="m">
                  {{ String(m).padStart(2, '0') }}
                </option>
              </select>
            </div>
            <template v-if="isExceedsType(startRuleTypeKey)">
              <span class="threshold-label">by</span>
              <div class="action-toggle threshold-toggle">
                <button type="button" class="toggle-btn" :class="{ active: startThresholdType === 'absolute' }" @click="startThresholdType = 'absolute'">£</button>
                <button type="button" class="toggle-btn" :class="{ active: startThresholdType === 'percent' }" @click="startThresholdType = 'percent'">%</button>
              </div>
              <input
                v-if="startThresholdType === 'absolute'"
                type="number"
                step="0.01"
                min="0.01"
                v-model.number="startThresholdAbsolute"
                class="threshold-input"
              />
              <input
                v-if="startThresholdType === 'percent'"
                type="number"
                step="1"
                min="1"
                v-model.number="startThresholdPercent"
                class="threshold-input"
              />
            </template>
          </div>
          <span v-if="selectedStartRuleType" class="rule-description">
            {{ selectedStartRuleType.description }}
          </span>
          <span v-if="startEffectiveMatchCount > 1" class="rule-match-count">
            Matches {{ startEffectiveMatchCount }} times today
          </span>
          <span v-else-if="startEffectiveMatchCount === 0 && isExceedsType(startRuleTypeKey)" class="rule-match-count rule-match-warn">
            No matches today with this threshold
          </span>
        </div>

        <!-- End time -->
        <div class="form-group">
          <label>End time</label>
          <div class="inline-group">
            <select v-model="endRuleTypeKey" class="rule-select">
              <option
                v-for="ruleType in groupedRuleTypes.ungrouped"
                :key="getRuleTypeKey(ruleType)"
                :value="getRuleTypeKey(ruleType)"
              >
                {{ ruleType.label }}
              </option>
              <optgroup label="Import rate">
                <option
                  v-for="ruleType in groupedRuleTypes.import"
                  :key="getRuleTypeKey(ruleType)"
                  :value="getRuleTypeKey(ruleType)"
                >
                  {{ ruleType.label }}
                </option>
              </optgroup>
              <optgroup label="Export rate">
                <option
                  v-for="ruleType in groupedRuleTypes.export"
                  :key="getRuleTypeKey(ruleType)"
                  :value="getRuleTypeKey(ruleType)"
                >
                  {{ ruleType.label }}
                </option>
              </optgroup>
            </select>
            <div v-if="endRuleTypeKey === 'fixed-time'" class="time-editor">
              <select v-model.number="endHour" class="time-select">
                <option v-for="h in 24" :key="h - 1" :value="h - 1">
                  {{ String(h - 1).padStart(2, '0') }}
                </option>
              </select>
              <span class="time-separator">:</span>
              <select v-model.number="endMinute" class="time-select">
                <option v-for="m in minuteOptions" :key="m" :value="m">
                  {{ String(m).padStart(2, '0') }}
                </option>
              </select>
            </div>
            <template v-if="isExceedsType(endRuleTypeKey)">
              <span class="threshold-label">by</span>
              <div class="action-toggle threshold-toggle">
                <button type="button" class="toggle-btn" :class="{ active: endThresholdType === 'absolute' }" @click="endThresholdType = 'absolute'">£</button>
                <button type="button" class="toggle-btn" :class="{ active: endThresholdType === 'percent' }" @click="endThresholdType = 'percent'">%</button>
              </div>
              <input
                v-if="endThresholdType === 'absolute'"
                type="number"
                step="0.01"
                min="0.01"
                v-model.number="endThresholdAbsolute"
                class="threshold-input"
              />
              <input
                v-if="endThresholdType === 'percent'"
                type="number"
                step="1"
                min="1"
                v-model.number="endThresholdPercent"
                class="threshold-input"
              />
            </template>
          </div>
          <span v-if="selectedEndRuleType" class="rule-description">
            {{ selectedEndRuleType.description }}
          </span>
          <span v-if="endEffectiveMatchCount > 1" class="rule-match-count">
            Matches {{ endEffectiveMatchCount }} times today
          </span>
          <span v-else-if="endEffectiveMatchCount === 0 && isExceedsType(endRuleTypeKey)" class="rule-match-count rule-match-warn">
            No matches today with this threshold
          </span>
        </div>

        <!-- Action -->
        <div class="form-group">
          <label>Action</label>
          <div class="action-toggle">
            <button
              type="button"
              class="toggle-btn"
              :class="{ active: action === 'import' }"
              @click="action = 'import'"
            >
              Import
            </button>
            <button
              type="button"
              class="toggle-btn"
              :class="{ active: action === 'export' }"
              @click="action = 'export'"
            >
              Export
            </button>
          </div>
        </div>

        <!-- Target -->
        <div class="form-group">
          <label for="target-slider">Target</label>
          <div class="charge-selector">
            <input
              id="target-slider"
              type="range"
              min="0"
              max="100"
              step="5"
              v-model.number="targetPercent"
              class="charge-slider"
            />
            <div class="charge-display">
              <span class="charge-value">{{ targetPercent }}</span>
              <span class="charge-unit">%</span>
            </div>
          </div>
        </div>

        <!-- Rate -->
        <div class="form-group">
          <label>Rate</label>
          <div class="action-toggle">
            <button
              type="button"
              class="toggle-btn"
              :class="{ active: rateMode === 'by-end' }"
              @click="rateMode = 'by-end'"
            >
              By end of zone
            </button>
            <button
              type="button"
              class="toggle-btn"
              :class="{ active: rateMode === 'fixed' }"
              @click="rateMode = 'fixed'"
            >
              At {{ rateKw.toFixed(1) }} kW
            </button>
          </div>
          <div v-if="rateMode === 'fixed'" class="charge-selector rate-slider">
            <input
              type="range"
              min="0.1"
              max="7.0"
              step="0.1"
              v-model.number="rateKw"
              class="charge-slider"
            />
            <div class="charge-display">
              <span class="charge-value charge-value-sm">{{ rateKw.toFixed(1) }}</span>
              <span class="charge-unit">kW</span>
            </div>
          </div>
        </div>

        <!-- Footer -->
        <div class="form-actions">
          <button v-if="isEditing" type="button" class="btn btn-danger" @click="handleDelete">
            <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor">
              <path d="M5.5 5.5A.5.5 0 016 6v6a.5.5 0 01-1 0V6a.5.5 0 01.5-.5zm2.5 0a.5.5 0 01.5.5v6a.5.5 0 01-1 0V6a.5.5 0 01.5-.5zm3 .5a.5.5 0 00-1 0v6a.5.5 0 001 0V6z"/>
              <path fill-rule="evenodd" d="M14.5 3a1 1 0 01-1 1H13v9a2 2 0 01-2 2H5a2 2 0 01-2-2V4h-.5a1 1 0 01-1-1V2a1 1 0 011-1H5.5l1-1h3l1 1h2.5a1 1 0 011 1v1zM4.118 4L4 4.059V13a1 1 0 001 1h6a1 1 0 001-1V4.059L11.882 4H4.118zM2.5 3V2h11v1h-11z"/>
            </svg>
            Delete
          </button>
          <div class="action-spacer"></div>
          <button type="button" class="btn btn-secondary" @click="handleCancel">
            Cancel
          </button>
          <button type="button" class="btn btn-primary" @click="handleSave">
            Save
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { getAllRuleTypes, findExportExceedsImportCrossovers, findImportExceedsExportCrossovers } from '../utils/priceAnalysis.js'

const props = defineProps({
  startMinutes: {
    type: Number,
    required: true
  },
  endMinutes: {
    type: Number,
    required: true
  },
  editingRule: {
    type: Object,
    default: null
  },
  pricingData: {
    type: Array,
    required: true
  },
  suggestedAction: {
    type: String,
    default: null
  },
  suggestedTargetPercent: {
    type: Number,
    default: null
  },
  suggestedStartRuleTypeKey: {
    type: String,
    default: null
  },
  suggestedEndRuleTypeKey: {
    type: String,
    default: null
  }
})

const emit = defineEmits(['save', 'delete', 'cancel'])

const isEditing = computed(() => props.editingRule !== null)

// Form state
const action = ref('import')
const targetPercent = ref(50)
const rateMode = ref('by-end')
const rateKw = ref(3.0)
const startRuleTypeKey = ref('fixed-time')
const endRuleTypeKey = ref('fixed-time')

// Threshold state for "exceeds" conditions
const startThresholdType = ref('absolute')
const startThresholdAbsolute = ref(0.05)
const startThresholdPercent = ref(10)
const endThresholdType = ref('absolute')
const endThresholdAbsolute = ref(0.05)
const endThresholdPercent = ref(10)

// Editable time state (hour + minute)
const startHour = ref(Math.floor(props.startMinutes / 60))
const startMinute = ref(props.startMinutes % 60)
const endHour = ref(Math.floor(props.endMinutes / 60))
const endMinute = ref(props.endMinutes % 60)

const minuteOptions = [0, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55]

// Computed total minutes from the editable fields
const editedStartMinutes = computed(() => startHour.value * 60 + startMinute.value)
const editedEndMinutes = computed(() => endHour.value * 60 + endMinute.value)

// All rule types (always the full set, computed from pricing data)
const allRuleTypes = computed(() => getAllRuleTypes(props.pricingData))

const groupedRuleTypes = computed(() => {
  const types = allRuleTypes.value
  return {
    ungrouped: types.filter(rt => !rt.priceType),
    import: types.filter(rt => rt.priceType === 'import'),
    export: types.filter(rt => rt.priceType === 'export')
  }
})

const selectedStartRuleType = computed(() =>
  allRuleTypes.value.find(rt => getRuleTypeKey(rt) === startRuleTypeKey.value)
)
const selectedEndRuleType = computed(() =>
  allRuleTypes.value.find(rt => getRuleTypeKey(rt) === endRuleTypeKey.value)
)

function isExceedsType(key) {
  return key === 'export-exceeds-import:export' || key === 'import-exceeds-export:import'
}

function buildThreshold(thresholdType, absoluteVal, percentVal) {
  return {
    type: thresholdType,
    value: thresholdType === 'absolute' ? absoluteVal : percentVal
  }
}

function getCrossoverCount(ruleTypeKey, threshold) {
  if (ruleTypeKey === 'export-exceeds-import:export') {
    return findExportExceedsImportCrossovers(props.pricingData, threshold).length
  }
  return findImportExceedsExportCrossovers(props.pricingData, threshold).length
}

const startEffectiveMatchCount = computed(() => {
  if (!isExceedsType(startRuleTypeKey.value)) {
    return selectedStartRuleType.value?.matchCount ?? 0
  }
  return getCrossoverCount(
    startRuleTypeKey.value,
    buildThreshold(startThresholdType.value, startThresholdAbsolute.value, startThresholdPercent.value)
  )
})

const endEffectiveMatchCount = computed(() => {
  if (!isExceedsType(endRuleTypeKey.value)) {
    return selectedEndRuleType.value?.matchCount ?? 0
  }
  return getCrossoverCount(
    endRuleTypeKey.value,
    buildThreshold(endThresholdType.value, endThresholdAbsolute.value, endThresholdPercent.value)
  )
})

// Initialize from editing rule or suggestions
onMounted(() => {
  if (props.editingRule) {
    action.value = props.editingRule.action || 'import'
    targetPercent.value = props.editingRule.targetPercent ?? 50
    rateMode.value = props.editingRule.rateMode || 'by-end'
    rateKw.value = props.editingRule.rateKw ?? 3.0

    if (props.editingRule.startTime) {
      startRuleTypeKey.value = getRuleTypeKey(props.editingRule.startTime)
      if (props.editingRule.startTime.fixedTimeMinutes != null) {
        startHour.value = Math.floor(props.editingRule.startTime.fixedTimeMinutes / 60)
        startMinute.value = props.editingRule.startTime.fixedTimeMinutes % 60
      }
      if (props.editingRule.startTime.thresholdType) {
        startThresholdType.value = props.editingRule.startTime.thresholdType
        if (props.editingRule.startTime.thresholdType === 'absolute') {
          startThresholdAbsolute.value = props.editingRule.startTime.thresholdValue ?? 0.05
        } else {
          startThresholdPercent.value = props.editingRule.startTime.thresholdValue ?? 10
        }
      }
    }
    if (props.editingRule.endTime) {
      endRuleTypeKey.value = getRuleTypeKey(props.editingRule.endTime)
      if (props.editingRule.endTime.fixedTimeMinutes != null) {
        endHour.value = Math.floor(props.editingRule.endTime.fixedTimeMinutes / 60)
        endMinute.value = props.editingRule.endTime.fixedTimeMinutes % 60
      }
      if (props.editingRule.endTime.thresholdType) {
        endThresholdType.value = props.editingRule.endTime.thresholdType
        if (props.editingRule.endTime.thresholdType === 'absolute') {
          endThresholdAbsolute.value = props.editingRule.endTime.thresholdValue ?? 0.05
        } else {
          endThresholdPercent.value = props.editingRule.endTime.thresholdValue ?? 10
        }
      }
    }
  } else {
    // New zone: apply smart suggestions if provided
    if (props.suggestedAction) {
      action.value = props.suggestedAction
    }
    if (props.suggestedTargetPercent != null) {
      targetPercent.value = props.suggestedTargetPercent
    }
    if (props.suggestedStartRuleTypeKey) {
      // Verify the suggested key is actually available
      const available = allRuleTypes.value.find(rt => getRuleTypeKey(rt) === props.suggestedStartRuleTypeKey)
      if (available) {
        startRuleTypeKey.value = props.suggestedStartRuleTypeKey
      }
    }
    if (props.suggestedEndRuleTypeKey) {
      const available = allRuleTypes.value.find(rt => getRuleTypeKey(rt) === props.suggestedEndRuleTypeKey)
      if (available) {
        endRuleTypeKey.value = props.suggestedEndRuleTypeKey
      }
    }
  }
})

function getRuleTypeKey(ruleType) {
  let key = ruleType.type
  if (ruleType.priceType) key += ':' + ruleType.priceType
  if (ruleType.regionType) key += ':' + ruleType.regionType
  if (ruleType.extremaType) key += ':' + ruleType.extremaType
  return key
}

function buildTimeDef(ruleTypeKey, fallbackMinutes, availableTypes, threshold) {
  const selected = availableTypes.find(rt => getRuleTypeKey(rt) === ruleTypeKey)
    || availableTypes[0]

  const timeDef = {
    type: selected.type,
    fixedTimeMinutes: fallbackMinutes
  }

  if (selected.priceType) timeDef.priceType = selected.priceType
  if (selected.regionType) timeDef.regionType = selected.regionType
  if (selected.extremaType) timeDef.extremaType = selected.extremaType
  if (threshold) {
    timeDef.thresholdType = threshold.type
    timeDef.thresholdValue = threshold.value
  }

  return timeDef
}

function handleSave() {
  const startThreshold = isExceedsType(startRuleTypeKey.value)
    ? buildThreshold(startThresholdType.value, startThresholdAbsolute.value, startThresholdPercent.value)
    : null
  const endThreshold = isExceedsType(endRuleTypeKey.value)
    ? buildThreshold(endThresholdType.value, endThresholdAbsolute.value, endThresholdPercent.value)
    : null

  const ruleData = {
    startTime: buildTimeDef(startRuleTypeKey.value, editedStartMinutes.value, allRuleTypes.value, startThreshold),
    endTime: buildTimeDef(endRuleTypeKey.value, editedEndMinutes.value, allRuleTypes.value, endThreshold),
    action: action.value,
    targetPercent: targetPercent.value,
    rateMode: rateMode.value,
    rateKw: rateMode.value === 'fixed' ? rateKw.value : undefined
  }

  emit('save', {
    ruleData,
    editingRuleId: props.editingRule?.id || null
  })
}

function handleDelete() {
  if (props.editingRule) {
    emit('delete', props.editingRule.id)
  }
}

function handleCancel() {
  emit('cancel')
}
</script>

<style scoped>
.zone-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background-color: var(--overlay);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
  padding: 1rem;
  animation: fadeIn 0.2s;
}

@keyframes fadeIn {
  from { opacity: 0; }
  to { opacity: 1; }
}

.zone-modal {
  background: var(--bg-secondary);
  border-radius: 12px;
  box-shadow: 0 8px 32px var(--shadow-md);
  width: 100%;
  max-width: 480px;
  max-height: 90vh;
  overflow-y: auto;
  animation: slideUp 0.3s;
}

@keyframes slideUp {
  from {
    transform: translateY(20px);
    opacity: 0;
  }
  to {
    transform: translateY(0);
    opacity: 1;
  }
}

.zone-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1.5rem;
  border-bottom: 1px solid var(--border-color);
}

.zone-header h3 {
  font-size: 1.25rem;
  font-weight: 600;
  color: var(--text-primary);
  margin: 0;
}

.close-btn {
  background: none;
  border: none;
  cursor: pointer;
  color: var(--icon-color);
  padding: 0.25rem;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 4px;
  transition: all 0.2s;
}

.close-btn:hover {
  background-color: var(--hover-bg);
}

.zone-form {
  padding: 1.5rem;
}

.inline-group {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.inline-group .rule-select {
  flex: 1;
  min-width: 0;
}

.form-group {
  margin-bottom: 1.5rem;
}

.form-group label {
  display: block;
  margin-bottom: 0.5rem;
  font-weight: 500;
  color: var(--text-primary);
  font-size: 0.9rem;
}

.time-editor {
  display: flex;
  align-items: center;
  gap: 0.25rem;
}

.time-select {
  font-size: 1.1rem;
  font-weight: 600;
  color: var(--color-primary);
  background: var(--bg-tertiary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  padding: 0.3rem 0.4rem;
  cursor: pointer;
  appearance: none;
  -webkit-appearance: none;
  text-align: center;
  min-width: 3rem;
}

.time-select:focus {
  outline: none;
  border-color: var(--color-primary);
}

.time-separator {
  font-size: 1.1rem;
  font-weight: 600;
  color: var(--color-primary);
}

/* Action toggle */
.action-toggle {
  display: flex;
  gap: 0;
  border: 1px solid var(--border-color);
  border-radius: 8px;
  overflow: hidden;
}

.toggle-btn {
  flex: 1;
  padding: 0.75rem;
  background: var(--bg-tertiary);
  border: none;
  cursor: pointer;
  font-size: 0.9rem;
  font-weight: 600;
  color: var(--text-secondary);
  transition: all 0.2s;
}

.toggle-btn:first-child {
  border-right: 1px solid var(--border-color);
}

.toggle-btn.active {
  background: var(--color-primary);
  color: white;
}

/* Charge slider */
.charge-selector {
  display: flex;
  align-items: center;
  gap: 1rem;
}

.charge-slider {
  flex: 1;
  -webkit-appearance: none;
  appearance: none;
  height: 8px;
  border-radius: 4px;
  background: var(--border-color);
  outline: none;
}

.charge-slider::-webkit-slider-thumb {
  -webkit-appearance: none;
  appearance: none;
  width: 24px;
  height: 24px;
  border-radius: 50%;
  background: var(--color-primary);
  cursor: pointer;
  border: 2px solid var(--bg-secondary);
  box-shadow: 0 2px 4px var(--shadow);
}

.charge-slider::-moz-range-thumb {
  width: 24px;
  height: 24px;
  border-radius: 50%;
  background: var(--color-primary);
  cursor: pointer;
  border: 2px solid var(--bg-secondary);
  box-shadow: 0 2px 4px var(--shadow);
}

.charge-display {
  display: flex;
  align-items: baseline;
  min-width: 70px;
  justify-content: flex-end;
}

.charge-value {
  font-size: 2rem;
  font-weight: 700;
  color: var(--color-primary);
  line-height: 1;
}

.charge-value-sm {
  font-size: 1.5rem;
}

.rate-slider {
  margin-top: 0.75rem;
}

.charge-unit {
  font-size: 1.25rem;
  font-weight: 500;
  color: var(--text-secondary);
}

/* Rule type dropdown */
.rule-select {
  width: 100%;
  padding: 0.6rem 0.75rem;
  font-size: 0.9rem;
  font-weight: 500;
  color: var(--text-primary);
  background: var(--bg-tertiary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  cursor: pointer;
  transition: border-color 0.2s;
}

.rule-select:focus {
  outline: none;
  border-color: var(--color-primary);
}

.rule-description {
  display: block;
  margin-top: 0.5rem;
  font-size: 0.8rem;
  color: var(--text-secondary);
  line-height: 1.4;
}

.rule-match-count {
  display: block;
  margin-top: 0.25rem;
  font-size: 0.75rem;
  color: var(--color-primary);
  font-weight: 500;
}

.rule-match-warn {
  color: var(--color-danger, #e74c3c);
}

/* Threshold controls */
.threshold-label {
  font-size: 0.85rem;
  font-weight: 500;
  color: var(--text-secondary);
}

.threshold-toggle {
  flex: 0 0 auto;
}

.threshold-toggle .toggle-btn {
  padding: 0.35rem 0.65rem;
  font-size: 0.85rem;
}

.threshold-input {
  width: 80px;
  padding: 0.35rem 0.5rem;
  font-size: 0.9rem;
  font-weight: 500;
  color: var(--text-primary);
  background: var(--bg-tertiary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  text-align: center;
}

.threshold-input:focus {
  outline: none;
  border-color: var(--color-primary);
}

/* Actions */
.form-actions {
  display: flex;
  gap: 0.75rem;
  align-items: center;
  padding-top: 0.5rem;
  border-top: 1px solid var(--border-color);
}

.action-spacer {
  flex: 1;
}

.btn {
  padding: 0.75rem 1.25rem;
  border: none;
  border-radius: 8px;
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.5rem;
}

.btn-primary {
  background-color: var(--color-primary);
  color: white;
}

.btn-primary:hover {
  background-color: var(--color-primary-hover);
}

.btn-secondary {
  background-color: var(--btn-secondary-bg);
  color: var(--text-primary);
}

.btn-secondary:hover {
  background-color: var(--btn-secondary-hover);
}

.btn-danger {
  background-color: var(--color-danger, #e74c3c);
  color: white;
}

.btn-danger:hover {
  background-color: var(--color-danger-hover, #c0392b);
}

.btn:active {
  transform: scale(0.98);
}

@media (max-width: 600px) {
  .zone-modal {
    border-radius: 0;
    max-height: 100vh;
  }

  .zone-header {
    padding: 1rem;
  }

  .zone-form {
    padding: 1rem;
  }

  .form-actions {
    flex-wrap: wrap;
  }

  .action-spacer {
    display: none;
  }

  .btn-danger {
    flex: 1 1 100%;
    order: 3;
  }
}
</style>

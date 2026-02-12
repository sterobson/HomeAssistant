<template>
  <div class="searchable-select" ref="containerRef">
    <button
      class="select-button"
      :class="{ open: isOpen, disabled }"
      :disabled="disabled"
      @click="toggleDropdown"
      type="button"
      ref="buttonRef"
    >
      <span v-if="selectedOption" class="selected-label">{{ selectedOption.name }}</span>
      <span v-else class="placeholder">{{ placeholder }}</span>
      <svg class="chevron" width="12" height="12" viewBox="0 0 12 12" fill="currentColor">
        <path d="M2.146 4.646a.5.5 0 01.708 0L6 7.793l3.146-3.147a.5.5 0 01.708.708l-3.5 3.5a.5.5 0 01-.708 0l-3.5-3.5a.5.5 0 010-.708z"/>
      </svg>
    </button>

    <Teleport to="body">
      <div v-if="isOpen" ref="dropdownRef" class="dropdown" :style="dropdownStyle">
        <div class="search-container">
          <input
            ref="searchInput"
            v-model="searchQuery"
            class="search-input"
            placeholder="Search..."
            @keydown.escape="closeDropdown"
          />
        </div>
        <div class="options-list">
          <div
            v-for="option in filteredOptions"
            :key="option.entityId"
            class="option"
            :class="{ selected: option.entityId === modelValue }"
            @click="selectOption(option)"
          >
            <div class="option-header">
              <span class="option-name">{{ option.name }}</span>
              <span v-if="option.state != null" class="option-state">{{ option.state }}</span>
            </div>
            <span class="option-id">{{ option.entityId }}</span>
          </div>
          <div v-if="filteredOptions.length === 0" class="no-results">
            No matching entities
          </div>
        </div>
      </div>
    </Teleport>
  </div>
</template>

<script setup>
import { ref, computed, nextTick, onMounted, onBeforeUnmount } from 'vue'

const props = defineProps({
  modelValue: { type: String, default: '' },
  options: { type: Array, default: () => [] },
  placeholder: { type: String, default: 'Select an entity...' },
  disabled: { type: Boolean, default: false }
})

const emit = defineEmits(['update:modelValue'])

const isOpen = ref(false)
const searchQuery = ref('')
const containerRef = ref(null)
const buttonRef = ref(null)
const searchInput = ref(null)
const dropdownRef = ref(null)
const dropdownStyle = ref({})

const selectedOption = computed(() => {
  if (!props.modelValue) return null
  return props.options.find(o => o.entityId === props.modelValue) || null
})

const filteredOptions = computed(() => {
  if (!searchQuery.value) return props.options
  const q = searchQuery.value.toLowerCase()
  return props.options.filter(o =>
    o.name.toLowerCase().includes(q) || o.entityId.toLowerCase().includes(q)
  )
})

function updateDropdownPosition() {
  if (!buttonRef.value) return
  const rect = buttonRef.value.getBoundingClientRect()
  dropdownStyle.value = {
    position: 'fixed',
    top: `${rect.bottom + 4}px`,
    left: `${rect.left}px`,
    width: `${rect.width}px`,
    zIndex: 1100
  }
}

function toggleDropdown() {
  if (props.disabled) return
  isOpen.value = !isOpen.value
  if (isOpen.value) {
    searchQuery.value = ''
    updateDropdownPosition()
    nextTick(() => searchInput.value?.focus())
  }
}

function closeDropdown() {
  isOpen.value = false
}

function selectOption(option) {
  emit('update:modelValue', option.entityId)
  closeDropdown()
}

function handleClickOutside(event) {
  if (containerRef.value?.contains(event.target)) return
  if (dropdownRef.value?.contains(event.target)) return
  closeDropdown()
}

function handleScroll() {
  if (isOpen.value) {
    updateDropdownPosition()
  }
}

onMounted(() => {
  document.addEventListener('click', handleClickOutside)
  document.addEventListener('scroll', handleScroll, true)
})

onBeforeUnmount(() => {
  document.removeEventListener('click', handleClickOutside)
  document.removeEventListener('scroll', handleScroll, true)
})
</script>

<style scoped>
.searchable-select {
  position: relative;
  width: 100%;
}

.select-button {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  padding: 0.5rem 0.75rem;
  background: var(--bg-tertiary);
  color: var(--text-primary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  font-size: 0.85rem;
  cursor: pointer;
  text-align: left;
  transition: border-color 0.15s;
}

.select-button:hover:not(.disabled) {
  border-color: var(--border-hover);
}

.select-button.open {
  border-color: var(--color-primary);
}

.select-button.disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.placeholder {
  color: var(--text-secondary);
}

.selected-label {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  flex: 1;
  min-width: 0;
}

.chevron {
  flex-shrink: 0;
  margin-left: 0.5rem;
  opacity: 0.6;
}
</style>

<style>
/* Unscoped so styles apply to the teleported dropdown */
.dropdown {
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  box-shadow: 0 8px 24px var(--shadow-md);
  animation: searchableSelectFadeIn 0.15s;
}

.dropdown .search-container {
  padding: 0.5rem;
  border-bottom: 1px solid var(--border-color);
}

.dropdown .search-input {
  width: 100%;
  padding: 0.4rem 0.6rem;
  background: var(--bg-tertiary);
  color: var(--text-primary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  font-size: 0.85rem;
  outline: none;
  box-sizing: border-box;
}

.dropdown .search-input:focus {
  border-color: var(--color-primary);
}

.dropdown .options-list {
  max-height: 240px;
  overflow-y: auto;
}

.dropdown .option {
  display: flex;
  flex-direction: column;
  padding: 0.5rem 0.75rem;
  cursor: pointer;
  transition: background 0.1s;
}

.dropdown .option:hover {
  background: var(--hover-bg);
}

.dropdown .option.selected {
  background: var(--hover-bg);
  border-left: 2px solid var(--color-primary);
}

.dropdown .option-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
}

.dropdown .option-name {
  font-size: 0.85rem;
  color: var(--text-primary);
}

.dropdown .option-state {
  font-size: 0.75rem;
  color: var(--color-primary);
  white-space: nowrap;
  flex-shrink: 0;
}

.dropdown .option-id {
  font-size: 0.72rem;
  color: var(--text-tertiary);
  margin-top: 1px;
}

.dropdown .no-results {
  padding: 1rem;
  text-align: center;
  color: var(--text-secondary);
  font-size: 0.85rem;
}

@keyframes searchableSelectFadeIn {
  from { opacity: 0; transform: translateY(-4px); }
  to { opacity: 1; transform: translateY(0); }
}
</style>

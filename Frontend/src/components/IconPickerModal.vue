<template>
  <div class="icon-overlay" :style="{ zIndex }" @click.self="handleCancel">
    <div class="icon-modal">
      <div class="icon-header">
        <h3>{{ title }}</h3>
        <button class="close-btn" @click="handleCancel" aria-label="Close">
          <svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor">
            <path d="M4.646 4.646a.5.5 0 01.708 0L10 9.293l4.646-4.647a.5.5 0 01.708.708L10.707 10l4.647 4.646a.5.5 0 01-.708.708L10 10.707l-4.646 4.647a.5.5 0 01-.708-.708L9.293 10 4.646 5.354a.5.5 0 010-.708z"/>
          </svg>
        </button>
      </div>

      <form class="icon-form" @submit.prevent="handleSubmit">
        <div v-if="editableName" class="name-row">
          <label for="group-name">Name</label>
          <input
            id="group-name"
            ref="nameRef"
            v-model="name"
            type="text"
            class="name-input"
            :class="{ invalid: !!nameProblem }"
            maxlength="60"
            autocomplete="off"
            :aria-invalid="!!nameProblem"
          />
          <p v-if="nameProblem" class="name-problem">{{ nameProblem }}</p>
        </div>

        <div class="preview-row">
          <span class="preview" :class="{ empty: !chosen }">{{ chosen || '–' }}</span>
          <input
            ref="inputRef"
            v-model="typed"
            type="text"
            class="icon-input"
            autocomplete="off"
            placeholder="Type or paste any emoji"
            aria-label="Emoji"
            @input="onTyped"
          />
        </div>

        <div class="suggestions" role="listbox" aria-label="Suggested icons">
          <button
            v-for="emoji in suggestions"
            :key="emoji"
            type="button"
            class="suggestion"
            :class="{ selected: emoji === chosen }"
            role="option"
            :aria-selected="emoji === chosen"
            @click="pick(emoji)"
          >{{ emoji }}</button>
        </div>

        <div v-if="deletable" class="delete-row">
          <button type="button" class="btn-delete" @click="emit('delete')">Delete group</button>
          <span class="delete-note">Its rooms and groups are kept and move up one level.</span>
        </div>

        <div class="form-actions">
          <button type="button" class="btn btn-secondary" @click="handleCancel">Cancel</button>
          <button type="button" class="btn btn-secondary" :disabled="!hadIcon && !chosen" @click="handleRemove">
            Remove icon
          </button>
          <button type="submit" class="btn btn-primary" :disabled="!changed || !!nameProblem">Save</button>
        </div>
      </form>
    </div>
  </div>
</template>

<script setup>
import { computed, nextTick, onMounted, ref } from 'vue'
import { normaliseIcon } from '../utils/roomLayout.js'

const props = defineProps({
  title: {
    type: String,
    default: 'Choose an icon'
  },
  currentIcon: {
    type: String,
    default: ''
  },
  zIndex: {
    type: Number,
    default: 1000
  },
  // Offer a name field too (used for groups; a room's name has its own rename dialog).
  editableName: {
    type: Boolean,
    default: false
  },
  currentName: {
    type: String,
    default: ''
  },
  // Offer a "Delete group" action (an existing group, not a new one).
  deletable: {
    type: Boolean,
    default: false
  }
})

const emit = defineEmits(['save', 'cancel', 'delete'])

// A starting set for a house; the text box takes anything else.
const suggestions = [
  '🛏️', '🛋️', '🍽️', '🍳', '🛁', '🚿', '🚽', '🎮',
  '💻', '🖥️', '📚', '🎨', '🧸', '🎵', '📺', '🏠',
  '🚪', '🧺', '🪴', '☕', '🍷', '🧘', '🏋️', '🔧',
  '🚗', '🔥', '❄️', '☀️', '🌙', '⭐', '❤️', '👶'
]

const hadIcon = computed(() => !!normaliseIcon(props.currentIcon))
const chosen = ref(normaliseIcon(props.currentIcon))
const typed = ref(props.currentIcon ?? '')
const inputRef = ref(null)
const nameRef = ref(null)

const name = ref(props.currentName ?? '')

const nameProblem = computed(() => (props.editableName && !name.value.trim() ? 'Enter a name.' : ''))

// Whether what would be saved differs from what is there now.
const iconChanged = computed(() => (chosen.value ?? null) !== (normaliseIcon(props.currentIcon) ?? null))
const nameChanged = computed(() => props.editableName && name.value.trim() !== (props.currentName ?? '').trim())
const changed = computed(() => iconChanged.value || nameChanged.value)

onMounted(async () => {
  await nextTick()
  // Start in whichever field the person most likely wants: the name when there is one.
  const field = props.editableName ? nameRef.value : inputRef.value
  field?.focus()
  field?.select()
})

const onTyped = () => {
  chosen.value = normaliseIcon(typed.value)
}

const pick = (emoji) => {
  chosen.value = emoji
  typed.value = emoji
}

const nameToSave = () => (props.editableName ? name.value.trim() : undefined)

const handleSubmit = () => {
  emit('save', { icon: chosen.value, name: nameToSave() })
}

// Removes only the icon; a name change typed in the same dialog is kept.
const handleRemove = () => {
  emit('save', { icon: null, name: nameToSave() })
}

const handleCancel = () => {
  emit('cancel')
}
</script>

<style scoped>
.icon-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background-color: var(--overlay);
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 1rem;
  animation: fadeIn 0.2s;
}

@keyframes fadeIn {
  from { opacity: 0; }
  to { opacity: 1; }
}

.icon-modal {
  background: var(--bg-secondary);
  border-radius: 12px;
  box-shadow: 0 8px 32px var(--shadow-md);
  width: 100%;
  max-width: 420px;
  max-height: 90vh;
  overflow-y: auto;
  animation: slideUp 0.3s;
}

@keyframes slideUp {
  from { transform: translateY(20px); opacity: 0; }
  to { transform: translateY(0); opacity: 1; }
}

.icon-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1.25rem 1.5rem;
  border-bottom: 1px solid var(--border-color);
}

.icon-header h3 {
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

.icon-form {
  padding: 1.25rem 1.5rem 1.5rem;
}

.name-row {
  margin-bottom: 1rem;
}

.name-row label {
  display: block;
  margin-bottom: 0.5rem;
  font-weight: 500;
  font-size: 0.9rem;
  color: var(--text-primary);
}

.name-input {
  width: 100%;
  padding: 0.75rem;
  border: 1px solid var(--border-color);
  border-radius: 8px;
  background-color: var(--bg-primary);
  color: var(--text-primary);
  font-size: 1rem;
  font-family: inherit;
  box-sizing: border-box;
}

.name-input:focus {
  outline: none;
  border-color: var(--color-primary);
}

.name-input.invalid {
  border-color: var(--color-error, #e74c3c);
}

.name-problem {
  margin: 0.5rem 0 0;
  font-size: 0.8rem;
  color: var(--color-error, #e74c3c);
}

.preview-row {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-bottom: 1rem;
}

.preview {
  flex: 0 0 56px;
  height: 56px;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 2rem;
  line-height: 1;
  background-color: var(--bg-primary);
  border: 1px solid var(--border-color);
  border-radius: 10px;
}

.preview.empty {
  color: var(--text-tertiary);
  font-size: 1.25rem;
}

.icon-input {
  flex: 1;
  min-width: 0;
  padding: 0.75rem;
  border: 1px solid var(--border-color);
  border-radius: 8px;
  background-color: var(--bg-primary);
  color: var(--text-primary);
  font-size: 1rem;
  font-family: inherit;
  box-sizing: border-box;
}

.icon-input:focus {
  outline: none;
  border-color: var(--color-primary);
}

.suggestions {
  display: grid;
  grid-template-columns: repeat(8, 1fr);
  gap: 0.25rem;
  margin-bottom: 1.25rem;
}

.suggestion {
  aspect-ratio: 1;
  font-size: 1.4rem;
  line-height: 1;
  background: none;
  border: 2px solid transparent;
  border-radius: 8px;
  cursor: pointer;
  padding: 0;
  transition: background-color 0.15s;
}

.suggestion:hover {
  background-color: var(--hover-bg);
}

.suggestion.selected {
  border-color: var(--color-primary);
  background-color: var(--hover-bg);
}

.delete-row {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-bottom: 1rem;
}

.btn-delete {
  background: none;
  border: 1px solid var(--color-error, #e74c3c);
  border-radius: 8px;
  color: var(--color-error, #e74c3c);
  cursor: pointer;
  font-size: 0.85rem;
  font-weight: 600;
  padding: 0.4rem 0.8rem;
  white-space: nowrap;
}

.btn-delete:hover {
  background-color: var(--color-error, #e74c3c);
  color: white;
}

.delete-note {
  font-size: 0.75rem;
  color: var(--text-secondary);
}

.form-actions {
  display: flex;
  gap: 0.75rem;
}

.btn {
  flex: 1;
  padding: 0.75rem 0.5rem;
  border: none;
  border-radius: 8px;
  font-size: 0.95rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s;
}

.btn-secondary {
  background-color: var(--btn-secondary-bg);
  color: var(--text-primary);
}

.btn-secondary:hover:not(:disabled) {
  background-color: var(--btn-secondary-hover);
}

.btn-primary {
  background-color: var(--color-primary);
  color: white;
}

.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

@media (max-width: 600px) {
  .icon-modal {
    border-radius: 0;
    max-height: 100vh;
  }

  .suggestions {
    grid-template-columns: repeat(6, 1fr);
  }
}
</style>

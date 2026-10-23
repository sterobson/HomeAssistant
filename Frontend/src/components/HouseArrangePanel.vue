<template>
  <aside class="arrange-panel" aria-label="Arrange">
    <div class="panel-head">
      <h3>Arrange</h3>
      <button type="button" class="link-btn" @click="emit('done')">Done</button>
    </div>

    <section class="picked" aria-live="polite">
      <template v-if="picked">
        <p class="picked-title">{{ picked.title }}</p>
        <p v-if="picked.subtitle" class="picked-subtitle">{{ picked.subtitle }}</p>
        <div class="actions">
          <button v-for="action in actions" :key="action.id" type="button" class="action" :class="{ danger: action.danger }" @click="emit('action', action.id)">
            {{ action.label }}
          </button>
        </div>
      </template>
      <p v-else class="hint">
        Tap a device, a piece of furniture, a door or a window to pick it, and drag it to move it. Doors and windows
        stay on a wall.
      </p>
    </section>

    <section class="json">
      <div class="json-head">
        <h4>Layout</h4>
        <div class="json-buttons">
          <button type="button" class="action" @click="copy">{{ copied ? 'Copied' : 'Copy' }}</button>
          <button v-if="!confirmingReset" type="button" class="action" @click="confirmingReset = true">Start again</button>
          <template v-else>
            <button type="button" class="action danger" @click="emit('reset'); confirmingReset = false">Discard my changes</button>
            <button type="button" class="action" @click="confirmingReset = false">Keep them</button>
          </template>
        </div>
      </div>
      <p class="hint">Every device, piece of furniture and opening as it is now. Copy it and send it over to make it the house's layout.</p>
      <textarea ref="textarea" class="json-text" readonly spellcheck="false" :value="json" aria-label="Layout as JSON"></textarea>
    </section>
  </aside>
</template>

<script setup>
import { ref, computed } from 'vue'

const props = defineProps({
  // { kind: 'device' | 'furniture' | 'opening', title, subtitle, door, sliding }, or null.
  picked: { type: Object, default: null },
  json: { type: String, default: '' },
})
const emit = defineEmits(['action', 'reset', 'done'])

const textarea = ref(null)
const copied = ref(false)
const confirmingReset = ref(false)

const actions = computed(() => {
  const p = props.picked
  if (!p) return []
  const turn = [
    { id: 'rotate-left', label: 'Turn left 45°' },
    { id: 'rotate-right', label: 'Turn right 45°' },
  ]
  if (p.kind === 'device') {
    return [...turn, { id: 'higher', label: 'Higher' }, { id: 'lower', label: 'Lower' }]
  }
  if (p.kind === 'furniture') {
    return [
      ...turn,
      { id: 'longer', label: 'Longer' },
      { id: 'shorter', label: 'Shorter' },
      { id: 'deeper', label: 'Deeper' },
      { id: 'shallower', label: 'Shallower' },
      { id: 'duplicate', label: 'Duplicate' },
      { id: 'delete', label: 'Delete', danger: true },
    ]
  }
  const door = p.door
    ? p.sliding
      ? [{ id: 'flip-hinge', label: 'Slide other way' }, { id: 'flip-swing', label: 'Other side of the wall' }]
      : [{ id: 'flip-hinge', label: 'Hinge on other side' }, { id: 'flip-swing', label: 'Open other way' }]
    : []
  return [
    ...door,
    { id: 'wider', label: 'Wider' },
    { id: 'narrower', label: 'Narrower' },
    { id: 'duplicate', label: 'Duplicate' },
    { id: 'delete', label: 'Delete', danger: true },
  ]
})

async function copy() {
  try {
    await navigator.clipboard.writeText(props.json)
  } catch {
    // Some browsers refuse the clipboard; leave the text selected for Ctrl+C instead.
    textarea.value?.select()
    return
  }
  copied.value = true
  setTimeout(() => { copied.value = false }, 2000)
}
</script>

<style scoped>
.arrange-panel {
  position: absolute;
  top: 3.6rem;
  right: max(0.75rem, env(safe-area-inset-right, 0px));
  bottom: 3rem;
  width: min(360px, calc(100% - 1.5rem));
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 12px;
  box-shadow: 0 8px 24px var(--shadow-md);
  padding: 0.85rem 1rem;
  color: var(--text-primary);
  overflow: hidden;
}

@media (max-width: 600px) {
  .arrange-panel {
    top: auto;
    left: max(0.5rem, env(safe-area-inset-left, 0px));
    right: max(0.5rem, env(safe-area-inset-right, 0px));
    bottom: max(0.5rem, env(safe-area-inset-bottom, 0px));
    width: auto;
    max-height: 48%;
  }
}

.panel-head,
.json-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
  flex-wrap: wrap;
}

h3 {
  margin: 0;
  font-size: 1.05rem;
}

h4 {
  margin: 0;
  font-size: 0.8rem;
  letter-spacing: 0.05em;
  text-transform: uppercase;
  color: var(--text-secondary);
}

.link-btn {
  background: none;
  border: none;
  color: var(--color-primary);
  font: inherit;
  font-weight: 600;
  cursor: pointer;
  padding: 0.2rem 0.4rem;
}

.picked-title {
  margin: 0;
  font-weight: 600;
}

.picked-subtitle,
.hint {
  margin: 0.15rem 0 0;
  font-size: 0.8rem;
  color: var(--text-secondary);
}

.actions,
.json-buttons {
  display: flex;
  flex-wrap: wrap;
  gap: 0.35rem;
  margin-top: 0.5rem;
}

.json-buttons {
  margin-top: 0;
}

.action {
  font: inherit;
  font-size: 0.82rem;
  padding: 0.3rem 0.65rem;
  border-radius: 6px;
  border: 1px solid var(--border-color);
  background: var(--bg-tertiary);
  color: var(--text-primary);
  cursor: pointer;
}

.action:hover {
  border-color: var(--color-primary);
}

.action.danger {
  color: var(--color-danger);
}

.action:focus-visible,
.link-btn:focus-visible {
  outline: 2px solid var(--color-primary);
  outline-offset: 2px;
}

.json {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
  min-height: 0;
  flex: 1;
}

.json-text {
  flex: 1;
  min-height: 6rem;
  width: 100%;
  resize: none;
  font-family: ui-monospace, 'Cascadia Mono', Consolas, monospace;
  font-size: 0.72rem;
  line-height: 1.4;
  color: var(--text-primary);
  background: var(--bg-tertiary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  padding: 0.5rem;
  white-space: pre;
}
</style>

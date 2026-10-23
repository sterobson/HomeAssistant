<template>
  <div class="device-overlay" :class="{ docked }" :style="{ zIndex }" @click.self="docked || emit('close')">
    <div class="device-modal" role="dialog" aria-modal="true" :aria-label="device.name">
      <div class="device-header">
        <div class="device-heading">
          <h3>{{ device.name }}</h3>
          <p class="device-where">{{ where }}</p>
        </div>
        <button class="close-btn" aria-label="Close" @click="emit('close')">
          <svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor">
            <path d="M4.646 4.646a.5.5 0 01.708 0L10 9.293l4.646-4.647a.5.5 0 01.708.708L10.707 10l4.647 4.646a.5.5 0 01-.708.708L10 10.707l-4.646 4.647a.5.5 0 01-.708-.708L9.293 10 4.646 5.354a.5.5 0 010-.708z"/>
          </svg>
        </button>
      </div>

      <div class="device-body">
        <p class="device-status" :data-tone="status.tone">
          <span class="status-dot" aria-hidden="true"></span>{{ status.text || 'No status' }}
        </p>

        <section v-if="groups.controls.length" class="device-section">
          <h4>Controls</h4>
          <ul class="entity-list">
            <li v-for="entity in groups.controls" :key="entity.entityId" class="entity-row">
              <div class="entity-main">
                <span class="entity-name">{{ labelFor(entity) }}</span>
                <span v-if="entity.controlledBy" class="entity-note">{{ entity.controlledBy }}</span>
                <span v-else-if="pending[entity.entityId]" class="entity-note" :class="`pending-${pending[entity.entityId].state}`">
                  {{ pendingText(pending[entity.entityId]) }}
                </span>
              </div>
              <template v-if="isPress(entity)">
                <button
                  type="button"
                  class="action-btn"
                  :disabled="!entity.actions.includes('press') || isSending(entity)"
                  @click="emit('command', entity, 'press')"
                >
                  Press
                </button>
              </template>
              <template v-else>
                <button
                  type="button"
                  class="toggle"
                  role="switch"
                  :aria-checked="entity.state === 'on'"
                  :aria-label="`${labelFor(entity)}: ${stateText(entity)}`"
                  :disabled="entity.actions.length === 0 || isSending(entity) || isUnavailable(entity)"
                  @click="emit('command', entity, entity.state === 'on' ? 'turn_off' : entity.state === 'off' ? 'turn_on' : 'toggle')"
                >
                  <span class="toggle-track"><span class="toggle-thumb"></span></span>
                  <span class="toggle-text">{{ stateText(entity) }}</span>
                </button>
              </template>
            </li>
          </ul>
        </section>

        <section v-if="groups.readings.length" class="device-section">
          <h4>Readings</h4>
          <dl class="readings">
            <template v-for="entity in groups.readings" :key="entity.entityId">
              <dt>{{ labelFor(entity) }}</dt>
              <dd :class="{ unavailable: isUnavailable(entity) }">{{ stateText(entity) }}</dd>
            </template>
          </dl>
        </section>

        <section v-if="signal" class="device-section">
          <h4>Zigbee signal</h4>
          <template v-if="signal.route">
            <p class="signal-caption">Likely route to the hub</p>
            <ol class="route">
              <li v-for="(step, i) in signal.route" :key="step.id">
                <span v-if="i > 0" class="lqi" :class="step.band">{{ step.lqi }}</span>
                <span class="route-name">{{ step.name }}</span>
              </li>
            </ol>
          </template>
          <p v-else class="signal-caption">No route to the hub in ZHA's last scan.</p>
          <p class="signal-caption">Hears ({{ signal.links.length }})</p>
          <ul class="links">
            <li v-for="link in signal.links" :key="link.id">
              <span class="lqi" :class="link.band">{{ link.lqi }}</span>
              <span class="link-name">{{ link.name }}</span>
              <span v-if="link.relationship === 'parent' || link.relationship === 'child'" class="relationship">{{ link.relationship }}</span>
            </li>
          </ul>
          <p class="hint">LQI 0–255, the weaker direction of each link. ZHA reports who hears whom, not the routes messages take, so the route is worked out from these.</p>
        </section>

        <details v-if="groups.other.length" class="device-section other">
          <summary>Settings and other states ({{ groups.other.length }})</summary>
          <dl class="readings">
            <template v-for="entity in groups.other" :key="entity.entityId">
              <dt>{{ labelFor(entity) }}</dt>
              <dd :class="{ unavailable: isUnavailable(entity) }">{{ stateText(entity) }}</dd>
            </template>
          </dl>
          <p class="hint">These can be changed in Home Assistant.</p>
        </details>

        <p v-if="device.manufacturer || device.model" class="device-model">
          {{ [device.manufacturer, device.model].filter(Boolean).join(' ') }}
        </p>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed, onMounted, onBeforeUnmount } from 'vue'
import { deviceStatus, entityLabel, entityStateText, groupEntities, domainOf } from '../utils/houseModel.js'
import { meshIndex, linksOf, routeToHub, lqiBand } from '../utils/zigbee.js'

const props = defineProps({
  device: { type: Object, required: true },
  roomName: { type: String, default: '' },
  // entityId -> { state: 'sending' | 'done' | 'failed', message }
  pending: { type: Object, default: () => ({}) },
  zIndex: { type: Number, default: 1000 },
  // Docked at the side with no backdrop, so the 3D house stays visible and can be tapped (its signal lines show).
  docked: { type: Boolean, default: false },
  // The Zigbee mesh ({ nodes, links }) and every device's name by id, for the signal section.
  zigbee: { type: Object, default: null },
  deviceNames: { type: Object, default: () => ({}) },
})
const emit = defineEmits(['command', 'close'])

const status = computed(() => deviceStatus(props.device))
const groups = computed(() => groupEntities(props.device))
const where = computed(() => props.device.area ? props.roomName || props.device.area : 'No room set in Home Assistant')

/** The device's place in the Zigbee mesh, or null for a device that isn't on Zigbee. */
const signal = computed(() => {
  if (!props.zigbee) return null
  const index = meshIndex(props.zigbee)
  if (!index.roles.has(props.device.id)) return null
  const name = (id) => props.deviceNames[id] ?? 'Unknown device'
  const route = routeToHub(props.zigbee, props.device.id, index)
  return {
    route: route?.map((step) => ({ ...step, name: name(step.id), band: step.lqi == null ? null : lqiBand(step.lqi) })),
    links: linksOf(props.zigbee, props.device.id, index).map((link) => ({ ...link, name: name(link.id), band: lqiBand(link.lqi) })),
  }
})

const labelFor = (entity) => entityLabel(entity, props.device) || (domainOf(entity.entityId) === 'light' ? 'Light' : 'Power')
const stateText = (entity) => entityStateText(entity)
const isPress = (entity) => domainOf(entity.entityId) === 'button'
const isUnavailable = (entity) => entity.state === 'unavailable'
const isSending = (entity) => props.pending[entity.entityId]?.state === 'sending'

function pendingText(p) {
  if (p.state === 'sending') return 'Sending…'
  if (p.state === 'done') return 'Done'
  return p.message || "That didn't work."
}

function onKey(event) {
  if (event.key === 'Escape') emit('close')
}
onMounted(() => window.addEventListener('keydown', onKey))
onBeforeUnmount(() => window.removeEventListener('keydown', onKey))
</script>

<style scoped>
.device-overlay {
  position: fixed;
  inset: 0;
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

/* Docked: no backdrop, the panel at the right (at the bottom on a phone), the house still usable around it. */
.device-overlay.docked {
  background: none;
  pointer-events: none;
  align-items: flex-start;
  justify-content: flex-end;
  padding: 4rem max(0.75rem, env(safe-area-inset-right, 0px)) 3rem 0.75rem;
}

.device-overlay.docked .device-modal {
  pointer-events: auto;
  max-width: 380px;
  max-height: 100%;
}

@media (max-width: 600px) {
  .device-overlay.docked {
    align-items: flex-end;
    justify-content: center;
    padding: 0 0.5rem max(0.5rem, env(safe-area-inset-bottom, 0px));
  }

  .device-overlay.docked .device-modal {
    max-height: 55%;
  }
}

.signal-caption {
  margin: 0.25rem 0 0.35rem;
  font-size: 0.8rem;
  color: var(--text-secondary);
}

.route,
.links {
  list-style: none;
  margin: 0 0 0.5rem;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.route li,
.links li {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.85rem;
  min-width: 0;
}

.route li + li {
  padding-left: 0.6rem;
  border-left: 2px solid var(--border-color);
  margin-left: 0.4rem;
}

.route-name,
.link-name {
  overflow-wrap: anywhere;
  color: var(--text-primary);
}

.relationship {
  font-size: 0.72rem;
  color: var(--text-tertiary);
}

.lqi {
  flex: 0 0 auto;
  min-width: 2.2rem;
  text-align: center;
  font-size: 0.72rem;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
  color: #fff;
  border-radius: 999px;
  padding: 0.05rem 0.4rem;
}

.lqi.strong { background: #2a9d5c; }
.lqi.fair { background: #b9820f; }
.lqi.weak { background: #c9402c; }

.device-modal {
  background: var(--bg-secondary);
  border-radius: 12px;
  box-shadow: 0 8px 32px var(--shadow-md);
  width: 100%;
  max-width: 440px;
  max-height: 90vh;
  display: flex;
  flex-direction: column;
  animation: slideUp 0.3s;
}

@keyframes slideUp {
  from { transform: translateY(20px); opacity: 0; }
  to { transform: translateY(0); opacity: 1; }
}

.device-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  padding: 1.25rem 1.5rem 1rem;
  border-bottom: 1px solid var(--border-color);
}

.device-heading {
  min-width: 0;
}

.device-header h3 {
  font-size: 1.2rem;
  font-weight: 600;
  color: var(--text-primary);
  margin: 0;
  overflow-wrap: anywhere;
}

.device-where {
  margin: 0.2rem 0 0;
  font-size: 0.85rem;
  color: var(--text-secondary);
}

.close-btn {
  background: none;
  border: none;
  cursor: pointer;
  color: var(--icon-color);
  padding: 0.25rem;
  display: flex;
  border-radius: 4px;
  flex: 0 0 auto;
}

.close-btn:hover {
  background-color: var(--hover-bg);
}

.device-body {
  padding: 1rem 1.5rem 1.25rem;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.device-status {
  margin: 0;
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 1.05rem;
  font-weight: 600;
  color: var(--text-primary);
  font-variant-numeric: tabular-nums;
}

.status-dot {
  width: 0.7rem;
  height: 0.7rem;
  border-radius: 50%;
  background: var(--color-primary);
  flex: 0 0 auto;
}

.device-status[data-tone="on"] .status-dot { background: var(--color-warning); }
.device-status[data-tone="off"] .status-dot { background: var(--text-tertiary); }
.device-status[data-tone="alert"] .status-dot { background: var(--color-danger); }

.device-section h4,
.device-section summary {
  margin: 0 0 0.5rem;
  font-size: 0.8rem;
  font-weight: 600;
  letter-spacing: 0.05em;
  text-transform: uppercase;
  color: var(--text-secondary);
}

.device-section summary {
  cursor: pointer;
  margin-bottom: 0;
}

.device-section.other[open] summary {
  margin-bottom: 0.5rem;
}

.entity-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.entity-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
  padding: 0.5rem 0.75rem;
  border: 1px solid var(--border-color);
  border-radius: 8px;
}

.entity-main {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
  min-width: 0;
}

.entity-name {
  color: var(--text-primary);
  font-weight: 500;
}

.entity-note {
  font-size: 0.8rem;
  color: var(--text-secondary);
}

.entity-note.pending-done { color: var(--color-success); }
.entity-note.pending-failed { color: var(--color-danger); }

.action-btn {
  border: 1px solid var(--color-primary);
  color: var(--color-primary);
  background: none;
  border-radius: 6px;
  padding: 0.35rem 0.9rem;
  font: inherit;
  font-weight: 600;
  cursor: pointer;
  flex: 0 0 auto;
}

.action-btn:disabled {
  opacity: 0.45;
  cursor: default;
}

.toggle {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  background: none;
  border: none;
  padding: 0.2rem;
  font: inherit;
  color: var(--text-secondary);
  cursor: pointer;
  flex: 0 0 auto;
}

.toggle:disabled {
  cursor: default;
  opacity: 0.55;
}

.toggle-track {
  position: relative;
  width: 2.6rem;
  height: 1.5rem;
  border-radius: 999px;
  background: var(--btn-secondary-bg);
  transition: background-color 0.2s;
}

.toggle-thumb {
  position: absolute;
  top: 0.15rem;
  left: 0.15rem;
  width: 1.2rem;
  height: 1.2rem;
  border-radius: 50%;
  background: var(--bg-secondary);
  box-shadow: 0 1px 3px var(--shadow-md);
  transition: transform 0.2s;
}

.toggle[aria-checked="true"] .toggle-track { background: var(--color-warning); }
.toggle[aria-checked="true"] .toggle-thumb { transform: translateX(1.1rem); }

.toggle-text {
  min-width: 2rem;
  text-align: left;
  font-size: 0.85rem;
}

.toggle:focus-visible,
.action-btn:focus-visible,
.close-btn:focus-visible {
  outline: 2px solid var(--color-primary);
  outline-offset: 2px;
}

.readings {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 0.35rem 1rem;
  margin: 0;
}

.readings dt {
  color: var(--text-secondary);
  overflow-wrap: anywhere;
}

.readings dd {
  margin: 0;
  text-align: right;
  color: var(--text-primary);
  font-variant-numeric: tabular-nums;
}

.readings dd.unavailable {
  color: var(--color-danger);
}

.hint,
.device-model {
  margin: 0.5rem 0 0;
  font-size: 0.8rem;
  color: var(--text-tertiary);
}

@media (prefers-reduced-motion: reduce) {
  .device-overlay, .device-modal { animation: none; }
  .toggle-track, .toggle-thumb { transition: none; }
}
</style>

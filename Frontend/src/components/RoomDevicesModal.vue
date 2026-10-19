<template>
  <div class="devices-overlay" :style="{ zIndex }" @click.self="emit('close')">
    <div class="devices-modal" role="dialog" aria-modal="true" :aria-label="title">
      <div class="devices-header">
        <h3>{{ title }}</h3>
        <button class="close-btn" aria-label="Close" @click="emit('close')">
          <svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor">
            <path d="M4.646 4.646a.5.5 0 01.708 0L10 9.293l4.646-4.647a.5.5 0 01.708.708L10.707 10l4.647 4.646a.5.5 0 01-.708.708L10 10.707l-4.646 4.647a.5.5 0 01-.708-.708L9.293 10 4.646 5.354a.5.5 0 010-.708z"/>
          </svg>
        </button>
      </div>

      <div class="devices-body">
        <p class="devices-hint">
          What the heating uses here, with each device's state when the house last reported.
        </p>

        <section v-for="section in sections" :key="section.roomId" class="devices-section">
          <h4 v-if="showSectionTitles" class="section-title">
            <span v-if="section.icon" class="section-icon" aria-hidden="true">{{ section.icon }}</span>{{ section.title }}
          </h4>

          <p v-if="section.devices.length === 0" class="no-devices">
            No devices. Nothing is bound to this name, or the house has not reported yet.
          </p>

          <ul v-else class="device-list">
            <li v-for="device in section.devices" :key="device.entityId" class="device" :class="{ unavailable: isUnavailable(device) }">
              <div class="device-main">
                <span class="device-name">{{ deviceName(device) }}</span>
                <span class="device-state">{{ describeState(device, formatTempWithUnit) }}</span>
              </div>
              <div class="device-meta">
                <span class="device-role">{{ roleLabel(device.role) }}</span>
                <code class="device-entity">{{ device.entityId }}</code>
              </div>
            </li>
          </ul>
        </section>
      </div>

      <div class="devices-actions">
        <button type="button" class="btn btn-secondary" @click="emit('close')">Close</button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed } from 'vue'
import { useFormatting } from '../composables/useFormatting.js'
import { describeState, deviceName, isUnavailable, roleLabel } from '../utils/roomDevices.js'

const { formatTempWithUnit } = useFormatting()

const props = defineProps({
  title: {
    type: String,
    default: 'Devices'
  },
  // [{ roomId, title, icon, devices }] from deviceSections() in utils/roomDevices.js
  sections: {
    type: Array,
    required: true
  },
  zIndex: {
    type: Number,
    default: 1000
  }
})

const emit = defineEmits(['close'])

// A single zone needs no heading of its own; the modal title already names it.
const showSectionTitles = computed(() => props.sections.length > 1)
</script>

<style scoped>
.devices-overlay {
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
  cursor: default;
}

@keyframes fadeIn {
  from { opacity: 0; }
  to { opacity: 1; }
}

.devices-modal {
  background: var(--bg-secondary);
  border-radius: 12px;
  box-shadow: 0 8px 32px var(--shadow-md);
  width: 100%;
  max-width: 480px;
  max-height: 90vh;
  display: flex;
  flex-direction: column;
  animation: slideUp 0.3s;
}

@keyframes slideUp {
  from { transform: translateY(20px); opacity: 0; }
  to { transform: translateY(0); opacity: 1; }
}

.devices-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 1.25rem 1.5rem;
  border-bottom: 1px solid var(--border-color);
}

.devices-header h3 {
  font-size: 1.25rem;
  font-weight: 600;
  color: var(--text-primary);
  margin: 0;
  overflow-wrap: anywhere;
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

.devices-body {
  padding: 1rem 1.5rem;
  overflow-y: auto;
}

.devices-hint {
  margin: 0 0 0.75rem;
  font-size: 0.85rem;
  color: var(--text-secondary);
}

.devices-section + .devices-section {
  margin-top: 1rem;
}

.section-title {
  margin: 0 0 0.5rem;
  font-size: 0.95rem;
  font-weight: 600;
  color: var(--text-primary);
}

.section-icon {
  margin-right: 0.35rem;
}

.no-devices {
  margin: 0;
  font-size: 0.85rem;
  color: var(--text-secondary);
}

.device-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}

.device {
  padding: 0.625rem 0.75rem;
  background: var(--bg-primary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
}

.device.unavailable {
  border-color: var(--color-danger, #e74c3c);
}

/* On a narrow screen the state and the entity ID drop onto their own line rather than squeezing the name. */
.device-main,
.device-meta {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  justify-content: space-between;
  column-gap: 0.75rem;
  min-width: 0;
}

.device-name {
  flex: 1 1 10rem;
  font-weight: 500;
  color: var(--text-primary);
  overflow-wrap: anywhere;
}

.device-state {
  margin-left: auto;
  font-size: 0.9rem;
  color: var(--text-primary);
  text-align: right;
}

.device.unavailable .device-state {
  color: var(--color-danger, #e74c3c);
  font-weight: 600;
}

.device-meta {
  margin-top: 0.2rem;
  font-size: 0.75rem;
  color: var(--text-secondary);
}

.device-entity {
  margin-left: auto;
  font-size: 0.7rem;
  color: var(--text-secondary);
  overflow-wrap: anywhere;
  text-align: right;
}

.devices-actions {
  padding: 1rem 1.5rem 1.25rem;
  border-top: 1px solid var(--border-color);
}

.btn {
  width: 100%;
  padding: 0.75rem;
  border: none;
  border-radius: 8px;
  font-size: 0.95rem;
  font-weight: 600;
  cursor: pointer;
}

.btn-secondary {
  background-color: var(--btn-secondary-bg);
  color: var(--text-primary);
}

@media (max-width: 600px) {
  .devices-modal {
    border-radius: 0;
    max-height: 100vh;
    height: 100%;
    max-width: none;
  }

  .devices-body {
    padding: 1rem;
  }
}
</style>

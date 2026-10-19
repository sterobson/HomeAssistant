<template>
  <div class="share-overlay" :style="{ zIndex }" @click.self="emit('close')">
    <div class="share-modal" role="dialog" aria-modal="true" :aria-label="`Share ${zoneName}`">
      <div class="share-header">
        <h3>Share {{ zoneName }}</h3>
        <button class="close-btn" aria-label="Close" @click="emit('close')">
          <svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor">
            <path d="M4.646 4.646a.5.5 0 01.708 0L10 9.293l4.646-4.647a.5.5 0 01.708.708L10.707 10l4.647 4.646a.5.5 0 01-.708.708L10 10.707l-4.646 4.647a.5.5 0 01-.708-.708L9.293 10 4.646 5.354a.5.5 0 010-.708z"/>
          </svg>
        </button>
      </div>

      <div class="share-body">
        <p class="share-hint">
          Anyone who scans this code can set {{ group ? 'every zone in this group' : 'this zone' }} to between
          {{ DEFAULT_LIMITS.minTemperature }} and {{ DEFAULT_LIMITS.maxTemperature }} °C for up to
          {{ formatDuration(DEFAULT_LIMITS.maxMinutes) }}, and change or cancel it at any time. They can't see or
          change the schedules.
        </p>

        <p v-if="loading" class="share-status">Making the link…</p>

        <div v-else-if="loadError" class="share-error" role="alert">
          <p>{{ loadError }}</p>
          <button type="button" class="btn btn-secondary" @click="loadShare">Try again</button>
        </div>

        <template v-else-if="token">
          <!-- This block is all that gets printed. -->
          <div class="share-print-area">
            <p class="print-name">{{ zoneName }}</p>
            <img v-if="qrDataUrl" :src="qrDataUrl" class="qr-image" :alt="`QR code to set the temperature of ${zoneName}`" width="256" height="256">
          </div>

          <div class="link-row">
            <input class="link-input" type="text" readonly :value="shareUrl" aria-label="Link" @focus="$event.target.select()">
            <button type="button" class="btn btn-primary" @click="copyLink">{{ copied ? 'Copied' : 'Copy link' }}</button>
          </div>
          <p v-if="copyError" class="share-error-text" role="alert">{{ copyError }}</p>

          <div v-if="confirmingRevoke" class="revoke-confirm" role="alertdialog" aria-label="Make a new link">
            <p>
              The current QR code and link will stop working straight away. Anyone who has them will need the new one.
            </p>
            <div class="revoke-actions">
              <button type="button" class="btn btn-secondary" :disabled="revoking" @click="confirmingRevoke = false">Keep current link</button>
              <button type="button" class="btn btn-danger" :disabled="revoking" @click="revoke">
                {{ revoking ? 'Making a new link…' : 'Make a new link' }}
              </button>
            </div>
            <p v-if="revokeError" class="share-error-text" role="alert">{{ revokeError }}</p>
          </div>
        </template>
      </div>

      <div v-if="token && !confirmingRevoke" class="share-actions">
        <button type="button" class="btn btn-secondary" @click="printCode">Print</button>
        <button type="button" class="btn btn-link-danger" @click="startRevoke">Revoke and make a new link</button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue'
import QRCode from 'qrcode'
import { roomShareApi } from '../services/roomShareApi.js'
import { roomLabel } from '../utils/rooms.js'
import { DEFAULT_LIMITS, formatDuration } from '../utils/guestOverride.js'

const props = defineProps({
  // The heating zone being shared (needs id, name and optionally displayName). Give this or `group`.
  room: {
    type: Object,
    default: null
  },
  // A layout group being shared ({ id, name }): the link boosts every zone inside it.
  group: {
    type: Object,
    default: null
  },
  zIndex: {
    type: Number,
    default: 1000
  }
})

const emit = defineEmits(['close'])

const zoneName = computed(() => (props.group ? props.group.name : roomLabel(props.room)))
const target = computed(() => (props.group ? { groupId: props.group.id } : { roomId: props.room.id }))

const token = ref('')
const qrDataUrl = ref('')
const loading = ref(true)
const loadError = ref('')
const copied = ref(false)
const copyError = ref('')
const confirmingRevoke = ref(false)
const revoking = ref(false)
const revokeError = ref('')

const shareUrl = computed(() => (token.value ? `${window.location.origin}/r/${token.value}` : ''))

const applyShare = async (share) => {
  token.value = share.token
  qrDataUrl.value = await QRCode.toDataURL(`${window.location.origin}/r/${share.token}`, {
    width: 512,
    margin: 2,
    errorCorrectionLevel: 'M',
    color: { dark: '#000000', light: '#ffffff' }
  })
}

const loadShare = async () => {
  loading.value = true
  loadError.value = ''
  try {
    await applyShare(await roomShareApi.createShare(target.value))
  } catch (error) {
    console.error('Error loading share link:', error)
    loadError.value = 'The link could not be made. Check your connection and try again.'
  } finally {
    loading.value = false
  }
}

onMounted(loadShare)

let copiedTimer = null

const copyLink = async () => {
  copyError.value = ''
  try {
    await navigator.clipboard.writeText(shareUrl.value)
    copied.value = true
    clearTimeout(copiedTimer)
    copiedTimer = setTimeout(() => { copied.value = false }, 2000)
  } catch {
    copyError.value = 'The link could not be copied. Select it and copy it by hand.'
  }
}

const printCode = () => window.print()

const startRevoke = () => {
  revokeError.value = ''
  confirmingRevoke.value = true
}

const revoke = async () => {
  revoking.value = true
  revokeError.value = ''
  try {
    await applyShare(await roomShareApi.revokeShare(target.value))
    confirmingRevoke.value = false
    copied.value = false
  } catch (error) {
    console.error('Error replacing share link:', error)
    revokeError.value = 'A new link could not be made. The current one still works.'
  } finally {
    revoking.value = false
  }
}
</script>

<style scoped>
.share-overlay {
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

.share-modal {
  background: var(--bg-secondary);
  border-radius: 12px;
  box-shadow: 0 8px 32px var(--shadow-md);
  width: 100%;
  max-width: 420px;
  max-height: 90vh;
  display: flex;
  flex-direction: column;
  animation: slideUp 0.3s;
}

@keyframes slideUp {
  from { transform: translateY(20px); opacity: 0; }
  to { transform: translateY(0); opacity: 1; }
}

.share-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 1.25rem 1.5rem;
  border-bottom: 1px solid var(--border-color);
}

.share-header h3 {
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

.share-body {
  padding: 1rem 1.5rem;
  overflow-y: auto;
}

.share-hint {
  margin: 0 0 1rem;
  font-size: 0.85rem;
  color: var(--text-secondary);
}

.share-status {
  margin: 2rem 0;
  text-align: center;
  color: var(--text-secondary);
}

.share-print-area {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 1rem;
}

.print-name {
  margin: 0;
  font-size: 1.1rem;
  font-weight: 600;
  color: var(--text-primary);
  text-align: center;
  overflow-wrap: anywhere;
}

/* White behind the code whatever the theme, or a dark theme makes it unreadable to a camera. */
.qr-image {
  width: 100%;
  max-width: 256px;
  height: auto;
  background: #ffffff;
  border-radius: 8px;
  image-rendering: pixelated;
}

.link-row {
  display: flex;
  gap: 0.5rem;
}

.link-input {
  flex: 1 1 auto;
  min-width: 0;
  padding: 0.5rem 0.625rem;
  border: 1px solid var(--border-color);
  border-radius: 6px;
  background: var(--bg-primary);
  color: var(--text-primary);
  font-size: 0.85rem;
  font-family: inherit;
}

.revoke-confirm {
  margin-top: 1rem;
  padding: 0.875rem;
  border: 1px solid var(--color-danger);
  border-radius: 8px;
  font-size: 0.9rem;
  color: var(--text-primary);
}

.revoke-confirm p {
  margin: 0 0 0.75rem;
}

.revoke-actions {
  display: flex;
  gap: 0.5rem;
}

.share-error,
.share-error-text {
  color: var(--color-danger);
  font-size: 0.85rem;
}

.share-error p {
  margin: 0 0 0.75rem;
}

.share-error-text {
  margin: 0.5rem 0 0;
}

.share-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
  padding: 1rem 1.5rem 1.25rem;
  border-top: 1px solid var(--border-color);
}

.btn {
  padding: 0.625rem 1rem;
  border: none;
  border-radius: 8px;
  font-size: 0.9rem;
  font-weight: 600;
  font-family: inherit;
  cursor: pointer;
}

.btn:disabled {
  opacity: 0.6;
  cursor: default;
}

.btn-primary {
  background-color: var(--color-primary);
  color: white;
  white-space: nowrap;
}

.btn-primary:hover {
  background-color: var(--color-primary-hover);
}

.btn-secondary {
  background-color: var(--btn-secondary-bg);
  color: var(--text-primary);
}

.btn-secondary:hover:not(:disabled) {
  background-color: var(--btn-secondary-hover);
}

.btn-danger {
  background-color: var(--color-danger);
  color: white;
}

.btn-danger:hover:not(:disabled) {
  background-color: var(--color-danger-hover);
}

.btn-link-danger {
  background: none;
  border: none;
  padding: 0.25rem 0;
  color: var(--color-danger);
  font-size: 0.85rem;
  font-family: inherit;
  cursor: pointer;
}

.btn-link-danger:hover {
  text-decoration: underline;
}

@media (max-width: 600px) {
  .share-header,
  .share-body,
  .share-actions {
    padding-left: 1rem;
    padding-right: 1rem;
  }
}
</style>

<!-- Not scoped: printing hides the whole page except the code and the zone name. -->
<style>
@media print {
  body * {
    visibility: hidden;
  }

  .share-print-area,
  .share-print-area * {
    visibility: visible;
  }

  .share-print-area {
    position: fixed;
    top: 0;
    left: 0;
    right: 0;
    margin: 0;
    padding-top: 2cm;
    color: #000000;
  }

  .share-print-area .print-name {
    font-size: 28pt;
    color: #000000;
  }

  .share-print-area .qr-image {
    max-width: 12cm;
  }
}
</style>

import { ref } from 'vue'

const errors = ref([])
let nextId = 0

const AUTO_DISMISS_MS = 8000

/**
 * Add an API error to the global error list.
 * Deduplicates by message — if the same message is already showing, it won't add again.
 */
export function reportApiError(message) {
  // Deduplicate: don't add if same message is already visible
  if (errors.value.some(e => e.message === message)) return

  const id = nextId++
  errors.value.push({ id, message })

  setTimeout(() => {
    dismissError(id)
  }, AUTO_DISMISS_MS)
}

export function dismissError(id) {
  errors.value = errors.value.filter(e => e.id !== id)
}

export function useApiErrors() {
  return {
    errors,
    dismissError
  }
}

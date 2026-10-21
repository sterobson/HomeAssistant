// Today's date, kept current across midnight for anything that greys out by day (schedule tracks). A singleton: one
// timer for the whole page, checking once a minute, and the value only changes when the date does.
import { ref } from 'vue'

const today = ref(new Date())
let timer = null

const tick = () => {
  const now = new Date()
  if (now.toDateString() !== today.value.toDateString()) today.value = now
}

export function useToday() {
  if (timer === null) timer = setInterval(tick, 60000)
  return { today }
}

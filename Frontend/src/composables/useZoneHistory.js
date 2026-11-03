import { ref } from 'vue'
import { heatingApi } from '../services/heatingApi.js'
import { boilerSegments } from '../utils/boilerHistory.js'
import { mergeGroupHistory } from '../utils/groupHistory.js'
import { genuineHeating, heatSources, unionStretches } from '../utils/heatingStretches.js'

/**
 * Loads the temperature history for one zone, or for the zones in a group, for TemperatureHistoryModal. Unlike the
 * other composables this is not a singleton: each modal calls it for its own state.
 *
 * - `points` is the zone's own history, or for a group the range across its zones (utils/groupHistory.js).
 * - `heatingStretches` is when the zone, or anything in the group, was genuinely heating (utils/heatingStretches.js).
 * - `heatingSources` is the same by source, `[{ key, label, stretches }]`, for the kinds of heat the zone or group has.
 *   They overlap, so their totals can add up to more than the total.
 * - `splitIncomplete` is whether some of the heating could not be given to a source.
 *
 * Where the boiler's history is not known, or fails to load, the boiler's heat is just when the zone was calling for
 * it, which is how the chart worked before the boiler was taken into account.
 */
export const useZoneHistory = () => {
  const points = ref([])
  const heatingStretches = ref([])
  const heatingSources = ref([])
  const splitIncomplete = ref(false)
  const usesBoilerHistory = ref(false)
  const loading = ref(false)
  const error = ref(null)

  // A newer load supersedes an older one still in flight (the user stepped to another day).
  let latest = 0

  const clear = () => {
    points.value = []
    heatingStretches.value = []
    heatingSources.value = []
    splitIncomplete.value = false
    usesBoilerHistory.value = false
  }

  /**
   * @param {object[]} rooms - the zones, as room states (`id`, `devices`)
   * @param {boolean} isGroup - merge the zones into a range rather than show the only one
   * @param {Date} startDate
   * @param {Date} endDate
   */
  const load = async (rooms, isGroup, startDate, endDate) => {
    const request = ++latest
    loading.value = true
    error.value = null
    clear()

    try {
      const anyBoiler = rooms.some(room => heatSources(room.devices).boiler)
      // The boiler's history only goes back from now; a missing or failed one must not spoil the chart.
      const hours = Math.max(1, Math.ceil((Date.now() - startDate.getTime()) / 3600000))
      const [histories, boilerHistory] = await Promise.all([
        Promise.all(rooms.map(room => heatingApi.getRoomHistory(room.id, startDate, endDate))),
        anyBoiler ? heatingApi.getBoilerHistory(hours).catch(() => null) : Promise.resolve(null)
      ])
      if (request !== latest) return

      const segments = boilerHistory ? boilerSegments(boilerHistory, new Date(boilerHistory.from), new Date(boilerHistory.to)) : null
      const results = histories.map((history, i) => genuineHeating(history.points, segments, rooms[i].devices))

      const sources = []
      const boiler = results.map(result => result.boiler).filter(Boolean)
      const electric = results.map(result => result.electric).filter(Boolean)
      if (boiler.length) sources.push({ key: 'boiler', label: 'Boiler', stretches: unionStretches(boiler) })
      if (electric.length) sources.push({ key: 'electric', label: 'Electric heater', stretches: unionStretches(electric) })

      points.value = isGroup ? mergeGroupHistory(histories.map(history => history.points)) : (histories[0]?.points ?? [])
      heatingStretches.value = unionStretches(results.map(result => result.stretches))
      heatingSources.value = sources
      splitIncomplete.value = results.some(result => result.unsplit)
      usesBoilerHistory.value = results.some(result => result.usesBoilerHistory)
    } catch (err) {
      if (request !== latest) return
      console.error('Error fetching temperature history:', err)
      error.value = 'Failed to load temperature history. Please try again.'
    } finally {
      if (request === latest) loading.value = false
    }
  }

  return { points, heatingStretches, heatingSources, splitIncomplete, usesBoilerHistory, loading, error, load }
}

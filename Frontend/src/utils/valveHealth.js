// Radiator valve health (owner, 2026-10-23): what the NetDaemon app has seen each valve do wrong, published as
// `valveHealth` on the valve's room-state device. The app only watches; it never moves a valve to test it, so a
// finding is confirmed or cleared by the boiler's own runs. See ValveHealthMonitor in Backend/App.

import { formatMinutes } from './warmUp.js'

export const VALVE_HEALTH_KINDS = {
  leaksWhenShut: 'leaksWhenShut',
  doesNotWarm: 'doesNotWarm'
}

const LABELS = {
  leaksWhenShut: 'Lets hot water through while shut',
  doesNotWarm: 'Stays cold while open'
}

/** The short name of a kind of fault, e.g. "Lets hot water through while shut". */
export const valveIssueLabel = (kind) => LABELS[kind] ?? 'Valve problem'

const isValve = (device) => device?.role === 'radiatorValve'

/** A valve's faults that are seen and not cleared since. */
export const flaggedIssues = (device) => (isValve(device) ? (device.valveHealth ?? []).filter(issue => issue.flagged) : [])

/** A valve's faults seen in the past but cleared by a later boiler run. */
export const clearedIssues = (device) =>
  (isValve(device) ? (device.valveHealth ?? []).filter(issue => !issue.flagged && issue.count > 0) : [])

/** Every flagged fault among some devices, as { device, issue }, for a badge on a room card. */
export const flaggedValveIssues = (devices) =>
  (devices ?? []).flatMap(device => flaggedIssues(device).map(issue => ({ device, issue })))

const sameDay = (a, b) => a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate()

/**
 * When something happened, for a sentence: "today at 15:05", "yesterday at 15:05", or "on 20 Oct at 15:05".
 * `formatTime24` turns "HH:mm" into the user's time format.
 */
export const formatWhen = (iso, now = new Date(), formatTime24 = (time) => time) => {
  if (!iso) return ''
  const date = new Date(iso)
  const time = formatTime24(`${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`)
  const yesterday = new Date(now)
  yesterday.setDate(now.getDate() - 1)
  if (sameDay(date, now)) return `today at ${time}`
  if (sameDay(date, yesterday)) return `yesterday at ${time}`
  return `on ${date.toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })} at ${time}`
}

const times = (count) => (count === 1 ? 'once' : count === 2 ? 'twice' : `${count} times`)

/**
 * What was seen, in a sentence, with temperatures in the user's unit:
 * "Its radiator warmed from 20.1°C to 27.1°C while the valve was shut and hot water was flowing."
 * Falls back to the app's own wording when the numbers are missing.
 */
export const describeWhatWasSeen = (issue, formatTemp) => {
  const { fromTemperature: from, toTemperature: to, minutes } = issue
  if (issue.kind === VALVE_HEALTH_KINDS.leaksWhenShut && from != null && to != null) {
    return `Its radiator warmed from ${formatTemp(from)} to ${formatTemp(to)} while the valve was shut and hot water was flowing.`
  }

  if (issue.kind === VALVE_HEALTH_KINDS.doesNotWarm && from != null && minutes != null) {
    const stayed = to != null && to > from ? `only got from ${formatTemp(from)} to ${formatTemp(to)}` : `stayed at ${formatTemp(from)}`
    return `It was open for ${formatMinutes(minutes)} while another radiator warmed, but its radiator ${stayed}.`
  }

  return issue.lastSeenDetail ? `${issue.lastSeenDetail}.` : ''
}

/** "Seen twice, last today at 15:05." plus, for a flagged fault, that no run has cleared it yet. */
export const describeHistory = (issue, when) => {
  const seen = `Seen ${times(issue.count)}, last ${when(issue.lastSeenAt)}.`
  if (issue.flagged) {
    return issue.lastOkAt
      ? `${seen} It looked fine ${when(issue.lastOkAt)}, before that.`
      : seen
  }

  return `${seen} Looked fine ${when(issue.lastOkAt)}, so it is not flagged now.`
}

/**
 * What to check for each fault, from the 2026-10-23 investigation (HOME-ASSISTANT.md → Radiator TRVs). Only for
 * flagged faults.
 */
export const suggestion = (kind) => {
  if (kind === VALVE_HEALTH_KINDS.leaksWhenShut) {
    return 'Usually the head has learnt its valve travel wrongly. Take it off, take the batteries out and back in, refit it firmly, then short-press the middle button so it relearns ("Ad"). If it still leaks, the valve itself may need a plumber.'
  }

  if (kind === VALVE_HEALTH_KINDS.doesNotWarm) {
    return 'The valve pin may be stuck. Take the head off, press the pin in and out a few times until it moves freely, then refit the head and let it relearn.'
  }

  return ''
}

/** The tooltip for a room card's badge: each flagged valve and what it does, one per line. */
export const badgeTitle = (flagged, nameOf) =>
  flagged.map(({ device, issue }) => `${nameOf(device)}: ${valveIssueLabel(issue.kind).toLowerCase()}`).join('\n')

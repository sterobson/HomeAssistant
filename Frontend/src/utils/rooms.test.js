import { describe, it, expect } from 'vitest'
import { roomLabel } from './rooms.js'

describe('roomLabel', () => {
  it('uses the display name when one is set', () => {
    expect(roomLabel({ name: 'Bedroom 1', displayName: "Ruth and Ste's room" })).toBe(
      "Ruth and Ste's room"
    )
  })

  it('falls back to the canonical name when there is no display name', () => {
    expect(roomLabel({ name: 'Kitchen' })).toBe('Kitchen')
  })

  it.each([null, '', '   '])('falls back when the display name is %j', (displayName) => {
    expect(roomLabel({ name: 'Bedroom 2', displayName })).toBe('Bedroom 2')
  })

  it('trims a padded display name rather than rendering the padding', () => {
    expect(roomLabel({ name: 'Bedroom 3', displayName: "  Ellie's room  " })).toBe("Ellie's room")
  })

  it('never returns undefined for a malformed room', () => {
    expect(roomLabel(null)).toBe('')
    expect(roomLabel(undefined)).toBe('')
    expect(roomLabel({})).toBe('')
  })
})

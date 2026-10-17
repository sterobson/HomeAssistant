// Room naming helpers.
//
// A room has two names. `name` is the canonical one and is the join key between
// configuration and hardware: the NetDaemon app matches on it (lower-cased, trimmed) to
// decide which plugs, valves and sensors the room is wired to, so renaming it silently
// unbinds the room. `displayName` is UI-only and safe to change at any time.
//
// Always render rooms through roomLabel() so the two never get confused.

/**
 * The name to show the user for a room: its display name when set, otherwise its
 * canonical name.
 * @param {{ name?: string, displayName?: string }} room
 * @returns {string}
 */
export const roomLabel = (room) => {
  if (!room) {
    return ''
  }

  const displayName = room.displayName?.trim()
  return displayName ? displayName : (room.name ?? '')
}

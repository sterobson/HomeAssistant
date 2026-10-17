// The key HeatingView provides its layout state and handlers under, and LayoutNode injects them by.
//
// A layout is a tree of any depth, rendered by a component that renders itself. Passing every room
// event and every layout handler down through each level as props would be a lot of plumbing for a
// recursive component, so they travel by provide/inject instead. The shape is:
//
//   roomsById        Map of room id to room, for looking a node's room up
//   editLayout       ref(boolean)   reordering / icon mode
//   occupancyFilter  ref(string|null)
//   expandedRoomId   ref(number|null)  one room's schedule open at a time
//   expandedGroupIds ref(Set<string>)  any number of groups open at once
//   canMove(key, delta) -> boolean
//   onMove(key, delta), onMoveTo(key), onEditRoomIcon(room), onEditGroup(groupNode), onToggleGroup(id)
//   onUpdateSchedule, onDeleteSchedule, onAddSchedule, onBoost, onCancelBoost, onToggleExpand, onRename
export const HEATING_LAYOUT = Symbol('heating-layout')

<template>
  <!-- A group: its own card, holding more nodes. This component renders itself for each child, which is
       what lets groups nest to any depth. -->
  <RoomGroupCard
    v-if="node.type === 'group'"
    :node="node"
    :rooms-by-id="ctx.roomsById.value"
    :is-expanded="ctx.expandedGroupIds.value.has(node.id)"
    :edit-mode="ctx.editLayout.value"
    :can-move-up="ctx.canMove(node.key, -1)"
    :can-move-down="ctx.canMove(node.key, 1)"
    :locked-by="groupLock"
    :low-batteries="groupLowBatteries"
    @toggle-expand="ctx.onToggleGroup(node.id)"
    @move="(delta) => ctx.onMove(node.key, delta)"
    @move-to="ctx.onMoveTo(node.key)"
    @edit="ctx.onEditGroup(node)"
    @boost="(boostData) => ctx.onBoostGroup(node, boostData)"
    @cancel-boost="ctx.onCancelGroupBoost(node)"
  >
    <LayoutNode v-for="child in node.children" :key="child.key" :node="child" />
  </RoomGroupCard>

  <!-- A room. A node naming a room that is not loaded yet renders nothing rather than failing. -->
  <RoomCard
    v-else-if="room"
    :room="room"
    :is-expanded="ctx.expandedRoomId.value === room.id"
    :occupancy-filter="ctx.occupancyFilter.value"
    :edit-mode="ctx.editLayout.value"
    :can-move-up="ctx.canMove(node.key, -1)"
    :can-move-down="ctx.canMove(node.key, 1)"
    :locked-by="roomLock"
    :low-batteries="roomLowBatteries"
    @update-schedule="ctx.onUpdateSchedule"
    @delete-schedule="ctx.onDeleteSchedule"
    @add-schedule="ctx.onAddSchedule"
    @boost="ctx.onBoost"
    @cancel-boost="ctx.onCancelBoost"
    @toggle-expand="ctx.onToggleExpand"
    @move="(delta) => ctx.onMove(node.key, delta)"
    @move-to="ctx.onMoveTo(node.key)"
    @edit="ctx.onEditRoom(room)"
  />
</template>

<script setup>
import { computed, inject } from 'vue'
import RoomCard from './RoomCard.vue'
import RoomGroupCard from './RoomGroupCard.vue'
import { HEATING_LAYOUT } from '../utils/layoutContext.js'
import { groupLockedBy, roomLockedBy } from '../utils/boosts.js'
import { batteriesForGroup, batteriesForRoom, isLow } from '../utils/batteries.js'

const props = defineProps({
  // A node of the layout tree: a room ({ type: 'room', roomId }) or a group ({ type: 'group', children }).
  node: {
    type: Object,
    required: true
  }
})

const ctx = inject(HEATING_LAYOUT)

const room = computed(() => (props.node.type === 'room' ? ctx.roomsById.value.get(props.node.roomId) : null))

// While a group's boost runs, nothing inside it can be boosted on its own: these name the group in charge.
const roomLock = computed(() => (room.value ? roomLockedBy(ctx.layoutTree.value, room.value)?.name ?? null : null))
const groupLock = computed(() =>
  props.node.type === 'group' ? groupLockedBy(ctx.layoutTree.value, props.node, ctx.roomsById.value)?.name ?? null : null)

// Low batteries to warn about here (utils/batteries.js): a zone's own; a group's own, plus everything inside it while
// it is collapsed, so folding a group away cannot hide a warning.
const roomLowBatteries = computed(() =>
  room.value ? batteriesForRoom(ctx.batteryPlacement.value, room.value.id).filter(isLow) : [])
const groupLowBatteries = computed(() =>
  props.node.type === 'group'
    ? batteriesForGroup(ctx.batteryPlacement.value, props.node, !ctx.expandedGroupIds.value.has(props.node.id)).filter(isLow)
    : [])
</script>

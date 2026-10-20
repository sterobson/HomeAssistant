<template>
  <div class="room-group">
    <div class="group-header" :class="{ 'any-heating': heating }" @click="emit('toggle-expand')">
      <div class="header-left">
        <div class="group-name-wrapper">
          <h2 class="group-name"><span v-if="node.icon" class="group-icon" aria-hidden="true">{{ node.icon }}</span>{{ node.name }}</h2>
          <button class="collapse-btn" :class="{ collapsed: !isExpanded }" aria-label="Show or hide the contents" @click.stop="emit('toggle-expand')">
            <svg width="20" height="20" viewBox="0 0 20 20" fill="currentColor">
              <path d="M5 7l5 5 5-5H5z"/>
            </svg>
          </button>
        </div>
        <div class="group-status">
          <div v-if="heating" class="heating-status active" title="Heating active">
            <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor">
              <path d="M8 16c3.314 0 6-2 6-5.5 0-1.5-.5-4-2.5-6 .25 1.5-1.25 2-1.25 2C11 4 9 .5 6 0c.357 2 .5 4-2 6-1.25 1-2 2.729-2 4.5C2 14 4.686 16 8 16m0-1c-1.657 0-3-1-3-2.75 0-.75.25-2 1.25-3C6.125 10 7 10.5 7 10.5c-.375-1.25.5-3.25 2-3.5-.179 1-.25 2 1 3 .625.5 1 1.364 1 2.25C11 14 9.657 15 8 15"/>
            </svg>
          </div>
          <!-- Before the temperature, so temperatures stay in one column down the page. -->
          <BatteryBadge v-if="lowestBattery" :battery="lowestBattery" :others-low="lowBatteries.length - 1" />
          <span v-if="temperatureText" class="current-temp">{{ temperatureText }}</span>
        </div>
      </div>

      <!-- Normally the width of a room's three icon buttons, so the temperature sits in the same column as
           every other card and the header is the same height. -->
      <div v-if="!editMode" class="actions-area">
        <!-- Same column as each zone's boost button. Boosting again changes a running group boost, and a
             group's boost supersedes any inside it. Disabled while a group around this one is boosted. -->
        <button
          v-if="canBoost"
          class="icon-btn boost-btn"
          :disabled="!!lockedBy"
          :title="boostButtonTitle"
          :aria-label="boostButtonTitle"
          @click.stop="showBoost = true"
        >
          <svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
            <path d="M11.251.068a.5.5 0 01.227.58L9.677 6.5H13a.5.5 0 01.364.843l-8 8.5a.5.5 0 01-.842-.49L6.323 9.5H3a.5.5 0 01-.364-.843l8-8.5a.5.5 0 01.615-.09z"/>
          </svg>
        </button>
        <span class="room-count">{{ zoneCountLabel }}</span>
      </div>
      <div v-else class="edit-actions" @click.stop>
        <button class="icon-btn edit-btn" title="Edit name and icon" aria-label="Edit name and icon" @click.stop="emit('edit')">
          <svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
            <path d="M12.146.146a.5.5 0 01.708 0l3 3a.5.5 0 010 .708l-10 10a.5.5 0 01-.168.11l-5 2a.5.5 0 01-.65-.65l2-5a.5.5 0 01.11-.168l10-10zM11.207 2.5L13.5 4.793 14.793 3.5 12.5 1.207 11.207 2.5zm1.586 3L10.5 3.207 4 9.707V10h.5a.5.5 0 01.5.5v.5h.5a.5.5 0 01.5.5v.5h.293l6.5-6.5zm-9.761 5.175l-.106.106-1.528 3.821 3.821-1.528.106-.106A.5.5 0 015 12.5V12h-.5a.5.5 0 01-.5-.5V11h-.5a.5.5 0 01-.468-.325z"/>
          </svg>
        </button>
        <button class="icon-btn edit-btn" :disabled="!canMoveUp" title="Move up" aria-label="Move up" @click.stop="emit('move', -1)">
          <svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true"><path d="M7.646 4.646a.5.5 0 01.708 0l6 6a.5.5 0 01-.708.708L8 5.707l-5.646 5.647a.5.5 0 01-.708-.708l6-6z"/></svg>
        </button>
        <button class="icon-btn edit-btn" :disabled="!canMoveDown" title="Move down" aria-label="Move down" @click.stop="emit('move', 1)">
          <svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true"><path d="M1.646 4.646a.5.5 0 01.708 0L8 10.293l5.646-5.647a.5.5 0 01.708.708l-6 6a.5.5 0 01-.708 0l-6-6a.5.5 0 010-.708z"/></svg>
        </button>
        <button class="icon-btn edit-btn" title="Move to…" aria-label="Move to" @click.stop="emit('move-to')">
          <svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true"><path d="M1 3.5A1.5 1.5 0 012.5 2h2.764c.958 0 1.76.56 2.311 1.184C7.985 3.648 8.48 4 9 4h4.5A1.5 1.5 0 0115 5.5v7a1.5 1.5 0 01-1.5 1.5h-11A1.5 1.5 0 011 12.5v-9zM2.5 3a.5.5 0 00-.5.5V6h12v-.5a.5.5 0 00-.5-.5H9c-.964 0-1.71-.629-2.174-1.154C6.374 3.334 5.82 3 5.264 3H2.5zM14 7H2v5.5a.5.5 0 00.5.5h11a.5.5 0 00.5-.5V7z"/></svg>
        </button>
      </div>
    </div>

    <div v-show="isExpanded" class="group-contents">
      <div v-if="empty" class="empty-group">Nothing in this group yet. In Edit layout, use Move to on a room or group.</div>
      <!-- Only once the group is opened, so the collapsed list stays as plain as it was. -->
      <template v-else>
        <div class="group-links">
          <button type="button" class="devices-link" @click="showDevices = true">
            <svg width="14" height="14" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
              <path d="M6 0a.5.5 0 01.5.5V3h3V.5a.5.5 0 011 0V3h1a.5.5 0 01.5.5v3A3.5 3.5 0 018.5 10c-.002.434-.01.845-.04 1.22-.041.514-.126 1.003-.317 1.424a2.083 2.083 0 01-.97 1.028C6.725 13.9 6.169 14 5.5 14c-.998 0-1.61.33-1.974.718A1.922 1.922 0 003 16H2c0-.616.232-1.367.797-1.968C3.374 13.42 4.261 13 5.5 13c.581 0 .962-.088 1.218-.219.241-.123.4-.3.514-.55.121-.266.193-.621.23-1.09.027-.34.035-.718.037-1.141A3.5 3.5 0 014 6.5v-3a.5.5 0 01.5-.5h1V.5A.5.5 0 016 0z"/>
            </svg>
            Devices in {{ node.name }} ({{ deviceCount }})
          </button>
          <!-- A QR code that lets a guest set every zone in this group; see ShareRoomModal. -->
          <button v-if="canBoost" type="button" class="devices-link" @click="showShare = true">
            <svg width="14" height="14" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
              <path d="M2 2h5v5H2V2zm1 1v3h3V3H3zm6-1h5v5H9V2zm1 1v3h3V3h-3zM2 9h5v5H2V9zm1 1v3h3v-3H3zm7-1h1v1h-1V9zm2 0h2v1h-1v1h-1V9zm-2 2h1v1h-1v-1zm2 1h1v2h-2v-1h1v-1zm-2 1h1v1h-1v-1z"/>
            </svg>
            Share
          </button>
        </div>

        <div v-if="lockedBy" class="group-boost">
          <span>Boosted with {{ lockedBy }}. Change or cancel it there.</span>
        </div>
        <div v-else-if="ownBoost" class="group-boost">
          <span>Boost running: {{ formatTempWithUnit(ownBoost.temperature) }} until {{ formatTime(ownBoost.endTime) }}</span>
          <button type="button" class="group-boost-cancel" @click="emit('cancel-boost')">Cancel boost</button>
        </div>
      </template>
      <slot />
    </div>

    <RoomDevicesModal
      v-if="showDevices"
      :title="`Devices in ${node.name}`"
      :sections="groupDeviceSections"
      :presence-zones="layoutContext?.presenceZones.value ?? []"
      :all-devices="layoutContext?.drivableDevices.value ?? []"
      :all-zones="layoutContext?.zones.value ?? []"
      @close="showDevices = false"
    />

    <BoostModal
      v-if="showBoost"
      :room-name="node.name"
      is-group
      @boost="(boostData) => { showBoost = false; emit('boost', boostData) }"
      @cancel="showBoost = false"
    />

    <ShareRoomModal
      v-if="showShare"
      :group="node"
      @close="showShare = false"
    />
  </div>
</template>

<script setup>
import { computed, inject, ref } from 'vue'
import RoomDevicesModal from './RoomDevicesModal.vue'
import BoostModal from './BoostModal.vue'
import ShareRoomModal from './ShareRoomModal.vue'
import BatteryBadge from './BatteryBadge.vue'
import { useFormatting } from '../composables/useFormatting.js'
import { batteriesForRoom, lowestLow } from '../utils/batteries.js'
import { anyHeating, roomIdsIn, temperatureSummary } from '../utils/roomLayout.js'
import { groupBoostActive } from '../utils/boosts.js'
import { deviceSections, distinctDeviceCount } from '../utils/roomDevices.js'
import { HEATING_LAYOUT } from '../utils/layoutContext.js'

const { formatTemp, formatTempWithUnit, formatTimeDisplay } = useFormatting()

// A boost's end (an ISO instant) as a local clock time, in the user's 12/24-hour format.
const formatTime = (iso) => {
  const end = new Date(iso)
  return formatTimeDisplay(`${String(end.getHours()).padStart(2, '0')}:${String(end.getMinutes()).padStart(2, '0')}`)
}

// Where the presence zones and the house's drivable devices come from; absent outside the heating view.
const layoutContext = inject(HEATING_LAYOUT, null)

const props = defineProps({
  // The group node from the layout tree: { type: 'group', id, name, icon, children }.
  node: {
    type: Object,
    required: true
  },
  // Every room by id, so a group can say how warm it is and whether anything inside is heating.
  roomsById: {
    type: Map,
    required: true
  },
  isExpanded: {
    type: Boolean,
    default: false
  },
  editMode: {
    type: Boolean,
    default: false
  },
  canMoveUp: {
    type: Boolean,
    default: false
  },
  canMoveDown: {
    type: Boolean,
    default: false
  },
  // The name of the group around this one whose boost is running, which locks this group's boost
  // controls (utils/boosts.js). Null when nothing around it is boosted.
  lockedBy: {
    type: String,
    default: null
  },
  // Batteries at or below the warning level to show here: the group's own, plus everything inside while it is
  // collapsed (utils/batteries.js). The lowest shows in the header.
  lowBatteries: {
    type: Array,
    default: () => []
  }
})

const emit = defineEmits(['toggle-expand', 'move', 'move-to', 'edit', 'boost', 'cancel-boost'])

// Lit whenever ANY room anywhere inside is heating, expanded or not, so a group never looks idle
// while something in it is on.
const heating = computed(() => anyHeating(props.node, props.roomsById))

// One value when every room reads the same (rooms sharing a thermometer do), otherwise the range,
// which is more honest than an average of unrelated rooms. Nothing until a room has reported.
const temperatureText = computed(() => {
  const summary = temperatureSummary(props.node, props.roomsById)
  if (!summary) return ''
  if (summary.shared) return formatTempWithUnit(summary.low)
  return `${formatTemp(summary.low)}–${formatTempWithUnit(summary.high)}`
})

// What sits directly inside, rooms and groups alike: "Downstairs" holding the Dining room group, a Kitchen and a
// Hallway is 3 zones, not however many rooms are nested below.
const zoneCount = computed(() => props.node.children.length)
const zoneCountLabel = computed(() => (zoneCount.value === 1 ? '1 zone' : `${zoneCount.value} zones`))
const empty = computed(() => props.node.children.length === 0)

// Every zone at any depth below this group, in layout order, each listed separately.
const showDevices = ref(false)
const roomsInGroup = computed(() => roomIdsIn(props.node).map(id => props.roomsById.get(id)).filter(Boolean))
const lowestBattery = computed(() => lowestLow(props.lowBatteries))

// Batteries placed by HA area on this group or on groups inside it, each group in layout order, as sections of
// their own ahead of the zones; and each zone's own batteries in its section.
const placement = computed(() => layoutContext?.batteryPlacement.value ?? null)
const groupBatterySections = computed(() => {
  if (!placement.value) return []
  const sections = []
  const walk = (group) => {
    const own = placement.value.byGroup.get(String(group.id)) ?? []
    if (own.length > 0) {
      sections.push({ roomId: `group:${group.id}`, title: group.name, icon: group.icon, devices: [], occupancy: null, batteries: own })
    }
    group.children.filter(child => child.type === 'group').forEach(walk)
  }
  walk(props.node)
  return sections
})
const groupDeviceSections = computed(() => [
  ...groupBatterySections.value,
  ...deviceSections(roomsInGroup.value).map(section => ({
    ...section,
    batteries: placement.value ? batteriesForRoom(placement.value, section.roomId) : []
  }))
])
// Every distinct heating device and battery device inside, a shared one counted once.
const deviceCount = computed(() => {
  const ids = new Set()
  for (const section of groupDeviceSections.value) {
    section.devices.forEach(device => ids.add(device.entityId))
    section.batteries.forEach(battery => {
      if (!battery.deviceEntityIds.some(id => ids.has(id))) ids.add(battery.entityId)
    })
  }
  return ids.size || distinctDeviceCount(roomsInGroup.value)
})

// Boosting and sharing need at least one zone inside that can set a temperature (capability flag 1).
const showBoost = ref(false)
const showShare = ref(false)
const boostableRooms = computed(() => roomsInGroup.value.filter(room => ((room.capabilities ?? 0) & 1) !== 0))
const canBoost = computed(() => boostableRooms.value.length > 0)

// This group's own boost: the running boost its zones carry, tagged with this group. A zone's own boost, or
// a smaller group's, does not count; this group's boost would supersede them.
const ownBoost = computed(() => {
  if (!groupBoostActive(props.node, props.roomsById)) return null
  return roomsInGroup.value.map(room => room.boost).find(boost => boost?.groupId === props.node.id) ?? null
})

const boostButtonTitle = computed(() => {
  if (props.lockedBy) return `Boosted with ${props.lockedBy}: change it there`
  return ownBoost.value ? 'Change the boost for this group' : 'Boost every zone in this group'
})
</script>

<!-- Sizing and highlighting deliberately mirror RoomCard so a group reads as one more room. If
     RoomCard's header changes, change this to match. -->
<style scoped>
.room-group {
  background: var(--bg-secondary);
  border-radius: 8px;
  box-shadow: 0 2px 4px var(--shadow);
  margin-bottom: 0.5rem;
  overflow: hidden;
}

.group-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1rem;
  background-color: var(--bg-secondary);
  border-bottom: 1px solid var(--border-color);
  /* No accent of its own: a group's header looks like any zone's (owner, 2026-10-20). */
  cursor: pointer;
  user-select: none;
  transition: all 0.2s;
}

.group-header:hover {
  background-color: var(--hover-bg);
}

/* Same treatment as a room that is heating while collapsed. */
.group-header.any-heating {
  background: linear-gradient(135deg, rgba(243, 156, 18, 0.1) 0%, rgba(230, 126, 34, 0.1) 100%);
  border: 2px solid #f39c12;
  border-radius: 8px;
}

.header-left {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 0.25rem 0.5rem;
  flex: 1;
  min-width: 0;
}

.group-name-wrapper {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  flex: 1 1 auto;
  min-width: 0;
}

.group-name {
  font-size: 1.25rem;
  font-weight: 600;
  color: var(--text-primary);
  margin: 0;
  min-width: 0;
  overflow-wrap: anywhere;
}

.group-icon {
  margin-right: 0.5rem;
}

.collapse-btn {
  background: none;
  border: none;
  cursor: pointer;
  color: var(--icon-color);
  padding: 0;
  display: flex;
  align-items: center;
  transition: transform 0.2s;
}

.collapse-btn.collapsed {
  transform: rotate(-90deg);
}

.group-status {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex: 0 0 auto;
}

.heating-status {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 0.25rem;
  border-radius: 4px;
  color: var(--text-tertiary);
  transition: all 0.3s;
}

.heating-status.active {
  color: #ff6b35;
  animation: flicker 2s ease-in-out infinite;
}

@keyframes flicker {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.8; }
}

.current-temp {
  font-size: 1rem;
  font-weight: 600;
  color: var(--text-primary);
  margin-right: 0.5rem;
  white-space: nowrap;
}

/* Two 36px icon buttons (a room's boost and history) and the 0.5rem gap between them. This also fixes
   the header height at a room's (36px of content plus padding). */
.actions-area {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  flex: 0 0 auto;
  width: calc(36px * 2 + 0.5rem);
  min-height: 36px;
}

.room-count {
  font-size: 0.8rem;
  color: var(--text-secondary);
  white-space: nowrap;
}

/* Beside the boost button the count takes the history button's slot, so it may wrap rather than push the
   temperature out of line with the zone cards. */
.icon-btn + .room-count {
  width: 36px;
  margin-left: 0.5rem;
  font-size: 0.7rem;
  line-height: 1.1;
  white-space: normal;
  text-align: center;
}

/* Same look as RoomCard's boost button. */
.icon-btn {
  color: white;
  border: none;
  padding: 0.5rem;
  border-radius: 8px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.2s;
  width: 36px;
  height: 36px;
  flex: 0 0 auto;
}

.boost-btn {
  background: linear-gradient(135deg, var(--color-warning) 0%, #e67e22 100%);
  box-shadow: 0 2px 4px rgba(243, 156, 18, 0.2);
}

.boost-btn:hover:not(:disabled) {
  transform: translateY(-1px);
  box-shadow: 0 4px 8px rgba(243, 156, 18, 0.3);
}

/* Locked by the boost of a group around this one (see utils/boosts.js). */
.boost-btn:disabled {
  opacity: 0.35;
  cursor: not-allowed;
  box-shadow: none;
}

.group-links {
  display: flex;
  flex-wrap: wrap;
  gap: 0 0.5rem;
}

.group-boost {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
  margin: 0 0 0.5rem;
  padding: 0.5rem 0.75rem;
  border-radius: 8px;
  border: 1px solid var(--color-warning);
  font-size: 0.85rem;
  color: var(--text-primary);
}

.group-boost-cancel {
  background: none;
  border: none;
  padding: 0;
  color: var(--color-danger);
  font-size: 0.85rem;
  font-family: inherit;
  cursor: pointer;
}

.group-boost-cancel:hover {
  text-decoration: underline;
}

/* In edit mode there are four buttons, so the temperature shifts left by two buttons for that mode. */
.edit-actions {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex: 0 0 auto;
}

.icon-btn {
  border: none;
  padding: 0.5rem;
  border-radius: 8px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.2s;
  width: 36px;
  height: 36px;
}

.edit-btn {
  background-color: var(--btn-secondary-bg);
  color: var(--icon-color);
}

.edit-btn:hover:not(:disabled) {
  background-color: var(--btn-secondary-hover);
  transform: translateY(-1px);
}

.edit-btn:disabled {
  opacity: 0.35;
  cursor: not-allowed;
}

/* What is inside sits indented, so the nesting reads at a glance at any depth. */
.group-contents {
  padding: 0.75rem 0.75rem 0.25rem 1rem;
  display: flex;
  flex-direction: column;
}

.empty-group {
  padding: 0.5rem 0.25rem 0.75rem;
  font-size: 0.85rem;
  color: var(--text-secondary);
}

/* The same quiet link RoomCard puts above its schedules. */
.devices-link {
  align-self: flex-start;
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  margin: 0 0 0.5rem;
  padding: 0.25rem 0.5rem;
  background: none;
  border: none;
  border-radius: 6px;
  color: var(--text-secondary);
  font-size: 0.85rem;
  font-family: inherit;
  text-align: left;
  cursor: pointer;
}

.devices-link:hover {
  color: var(--color-primary);
  background-color: var(--hover-bg);
}

/* Same phone adjustments as RoomCard's header. */
@media (max-width: 600px) {
  .group-header {
    padding: 0.75rem;
  }

  .group-name {
    font-size: 1.1rem;
  }

  .group-contents {
    padding: 0.5rem 0.5rem 0 0.75rem;
  }
}
</style>

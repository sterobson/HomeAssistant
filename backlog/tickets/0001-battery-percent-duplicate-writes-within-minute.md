# #1 Investigate duplicate battery % writes within the same minute

**Status**: Backlog
**Priority**: Medium
**Type**: Spike
**Tags**: Battery, Storage, Data quality

## Description

Battery percentage data is sometimes written to storage multiple times within the same minute. We don't currently know the root cause, frequency, or impact. Before deciding whether to fix it (and how), we need a short investigation to understand what's happening.

Why this matters:

- **Storage cost** grows linearly with write rate. Azure Tables is cheap per row but the cost is cumulative — at 1 write/minute, a year of data is ~525k rows; at 5 writes/minute, it's 2.6M.
- **Read performance**: every `GetHistoryAsync` for a day pulls all rows in the partition. Extra rows = slower battery chart loads.
- **Chart noise**: the frontend may render duplicate or near-duplicate points (the dedup story is unclear).
- **Code smell**: duplicate writes often indicate a control-flow bug — e.g. a SignalR event being handled twice, a watcher firing on every state read, or two writers racing.

## Acceptance criteria

- [ ] Root cause identified — which code path(s) are emitting the duplicates and under what conditions.
- [ ] Frequency quantified — sample a representative day from production storage and report: total writes vs. unique minutes, max writes-per-minute, distribution.
- [ ] Decision recorded: fix, accept as WAI, or defer. If fix → spawn a follow-up ticket with a concrete change. If WAI → document why in this ticket's Notes and close.
- [ ] If fixing, the follow-up should include a regression test (unit or integration) that pins the expected single-write-per-state-change behaviour.

## Notes

Likely suspects (verify, don't assume):

- `BatteryHistoryStorageService.GenerateRowKey` uses inverted ticks at sub-second precision, so storage doesn't dedupe — every push lands as a distinct row. That's by design for ordering, but it means upstream is the only line of defence.
- `BatteryHistoryPushService` (daemon) — does it push on every HA state change, or on a timer? If state-change, a flapping sensor would explain it.
- `PowerMonitorService` — separate writer that may also publish battery state.
- SignalR `battery-state-changed` — the Function endpoint may be invoked, broadcast, and re-handled in a loop somewhere.

Useful first step: pick one day in prod storage, query the `batteryhistory` table for that day's partition, group by `RecordedAt` truncated to minute, look at the count distribution. That's a ~10-minute investigation that tells us whether this is "occasional bursts" or "happens every minute" — very different problems.

Storage path reference: [BatteryHistoryStorageService.cs](Backend/HomeAssistant.Functions/Services/BatteryHistoryStorageService.cs)
Daemon writer: [BatteryHistoryPushService](Backend/App/Services/Energy/BatteryHistoryPushService.cs)
Function endpoint: [BatteryHistoryFunctions.cs](Backend/HomeAssistant.Functions/BatteryHistoryFunctions.cs)

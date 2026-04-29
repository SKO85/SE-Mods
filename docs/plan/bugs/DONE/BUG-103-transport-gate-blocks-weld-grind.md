# BUG-103: Cosmetic transport timer gates weld/grind work, ~80% throughput penalty

## Status: Fixed
## Severity: High (throughput) / Low (safety)
## Version: v2.5.4
## Found In: `NanobotSystem.Operations.cs`, `NanobotSystem.Grinding.cs`

## Description

Player report: "BaRs seem always in *Welding (Transport)* mode. They target a block for welding, but keep transporting or doing something without actually currently welding it."

Discovered via `thenebula4` profile (v2.5.4, 31 BaRs ON, 120 s):

- 170 of 198 transport-active entries showed `transportTimeMs=5000-10000` (5-10 second transports).
- `ServerDoWeld` ran only 185 times across all 31 BaRs in 120 s (~6 welds/sec **combined**).
- `ServerDoGrind` ran only 5 times (warmup-only — almost zero grinding).
- BaR effective duty cycle ≈ 15-20 % of work cycles; the rest was spent waiting on a cosmetic timer.

## Steps to Reproduce

1. Place a BaR with default settings within range of a weld target ~100 m away.
2. Watch the terminal `State:` field. It cycles through `Welding (Transporting)` for 5-6 seconds at a time, with occasional brief `Welding` flashes.
3. Watch the target block integrity progress — measurable rate is far slower than the work cycle would suggest.

## Root Cause

The transport timer (`State.CurrentTransportTime`) is sized by `2 × distance / WeldTransportSpeed`. With default `WeldTransportSpeed = 40 m/s` and a target at 100 m, that's a **5-second wait** per transport. The timer's only consumers are:

- `Effects.cs:393` — interpolates the visual particle position.
- `IsTransportRunning` (`NanobotSystem.Inventory.cs:339-380`) — wall-clock comparison only.

The actual items are picked from the source inventory **synchronously** in `ServerFindMissingComponents` (welding) or moved in `ServerEmptyTransportInventory` (grinding). After picking, items are immediately available for the welder to consume; no functional reason to wait.

But two gates blocked actual weld/grind work for the duration of the cosmetic timer:

1. **`NanobotSystem.Operations.cs:125`** — `if (!transporting)` wrapped the entire work-mode dispatch (`switch (Settings.WorkMode)`). When a transport from a prior tick was still running, the welding and grinding loops were skipped entirely.
2. **`NanobotSystem.Grinding.cs:155`** — `if (transporting) return false;` short-circuited `ServerDoGrind` whenever the timer was running, even after `ServerEmptyTransportInventory` had already drained any in-flight items into the welder inventory.

Combined effect: each weld/grind cycle was followed by a 5-6 second idle period during which the BaR did nothing. The user-visible state remained `Welding (Transporting)` because `State.Transporting=true` was set from the timer-driven `IsTransportRunning` result.

## Fix

Both gates removed. Visual particles still play (driven by `State.Transporting` reflecting the timer), but the underlying weld/grind loops proceed every work cycle.

### `NanobotSystem.Operations.cs:125`

Removed the `if (!transporting)` wrapper around the work-mode dispatch. The contained logic (`State.MissingComponents.Clear()`, `RebuildSaturatedGrids()`, the `switch (Settings.WorkMode)` block, and `State.MissingComponents.RebuildHash()`) now runs every tick. `transportBlocked = transporting` is still recorded for profiler diagnostics.

The inner `ServerFindMissingComponents` (`Welding.cs:552`) already handles in-flight transports correctly — returns `true` (transport active) without restarting the timer, so welding proceeds via `ServerDoWeld(targetData)` using items already in the welder.

### `NanobotSystem.Grinding.cs:155`

Removed `if (transporting) return false;` from `ServerDoGrind`. The preceding `transporting = IsTransportRunning(playTime);` call still runs and triggers `ServerEmptyTransportInventory` so any in-flight items move into the welder before the new grind. The new grind then sets a fresh transport timer (overriding the old cosmetic one) and adds items to `_TransportInventory`. No item loss because `ServerEmptyTransportInventory` already drained `_TransportInventory` on the same tick.

## Expected impact

The work-cycle gate (`Operations.cs:122-126`, `cycleDivisor = 100/workSpeed`) still caps each BaR to one work attempt per ~1.6 s (`workSpeed=1` default). Removing the transport gate means the BaR uses every work cycle instead of skipping 3-4 of them.

- Welding: ~1 weld per 5-6 s → **~1 weld per 1.6 s** = ≈ 3-4× throughput.
- Grinding: same magnitude.
- Existing `WorkSpeed` (1-10) and `WeldingMultiplier` / `GrindingMultiplier` knobs are unchanged; users who want to go faster can adjust them as before.

Distance-based pacing is no longer enforced via the gate. If a server admin wants to slow far-target work, they can lower `WeldTransportSpeed` / `GrindTransportSpeed` (which now only affects the visual duration, not work rate) — note that this change is now decorative, so the slower setting is also visible visually.

## Verification

1. **Build clean** — `dotnet build ... -c Release -v minimal` → 0 warnings, 0 errors. ✓
2. **Re-profile** — repeat the `thenebula4` scenario (31 BaRs ON, 120 s). Compare:
   - `ServerDoWeld` call count: was 185, expect ~600-800 (3-4× higher).
   - `ServerDoGrind` call count: was 5, expect 100+ (was almost entirely blocked by the gate).
   - `transportTimeMs` distribution: still 5-10 s (timer unchanged), but `transporting=True` entries should now show parallel `welding=True` more often.
   - Per-BaR integrity progress should visibly increase per minute.
   - `simSpeed` should be similar or marginally improved (more work per tick is small absolute cost vs. the orchestration overhead, which is unchanged).
3. **Regression — visuals** — observe the welding/grinding particle effect in-game. Should still travel from welder toward target, same duration as before.
4. **Regression — state machine** — terminal `State:` should still show `Welding (Transporting)` while the timer runs (visual pacing) but with visible integrity progress on the target.
5. **Edge case — full inventory** — if welder inventory is full, `ServerEmptyTransportInventory` returns false, `_TransportInventory` accumulates ground items, transport stays `running`. Existing push-targets logic eventually drains the welder via `ServerTryPushInventory`. No item loss because the timer-gate path was identical to the no-gate path on the full-inventory branch.

## See also

- `Effects.cs:393` — visual particle position uses elapsed/total ratio of the transport timer; unchanged.
- `Welding.cs:552` — `ServerFindMissingComponents` short-circuits when a transport is already in flight; unchanged. Welding inner loop's `if (!transporting) ServerFindMissingComponents(...)` correctly avoids restarting the timer.
- Profile session: `thenebula4` (2026-04-28, v2.5.4 with BUG-101 + BUG-102 applied, 31 BaRs ON, 120 s).

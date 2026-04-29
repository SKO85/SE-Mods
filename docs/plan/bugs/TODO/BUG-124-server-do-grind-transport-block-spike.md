# BUG-124: ServerDoGrind `transportMs` 19 ms spike is NOT in ServerEmptyTransportInventory — likely in `ComputeWorldCenter` on a just-removed block

## Status: Open
## Severity: High (largest main-thread payload spike in 3-large-ship grinding scenario)
## Version: v2.5.5
## Found In: `NanobotSystem.Grinding.cs:325-338` (the `tsTransport` block)

## Description

Profile session `20260429184659-profiling` (3 large ships, ~11,700 blocks each,
58 BaRs grinding, sim-speed dipped to 0.57 min / 0.93 avg) shows the dominant
main-thread grind spike on `ServerDoGrind`:

```
2026-04-29 18:47:57Z;ms=19.713;block=LargeBlockArmorBlock;dismounted=True;
                     emptyMs=0.001;friendlyMs=0.068;decreaseMs=0.050;razeMs=0.432;
                     transportMs=19.134;...
```

**Crucial observation:** `ServerEmptyTransportInventory` has its own profiler
in this session — `max=0.189 ms` across 2 489 calls. So the 19 ms in
`transportMs` is **not** the `ServerEmptyTransportInventory(true)` call inside
the wrapped block. Something else in the surrounding ~5 lines is the spike.

## Suspect surface

`NanobotSystem.Grinding.cs:325-338`:

```csharp
tsMark = Stopwatch.GetTimestamp();
if ((float)_TransportInventory.CurrentVolume >= _MaxGrindTransportVolume || target.IsFullyDismounted)
{
    State.CurrentTransportIsPick = true;
    State.CurrentTransportIsCollecting = false;
    State.CurrentTransportTarget = ComputePosition(target);    // ← suspect
    State.CurrentTransportStartTime = playTime;
    State.CurrentTransportTime = TimeSpan.FromSeconds(2d * targetData.Distance / Settings.GrindTransportSpeed);

    ServerEmptyTransportInventory(true);                       // own profiler: max 0.189 ms — ruled out
    transporting = true;
}
tsTransport = Stopwatch.GetTimestamp() - tsMark;
```

Most likely culprit: **`ComputePosition(target)` → `target.ComputeWorldCenter(out endPosition)`**
called on a block that was *just removed* from its grid by
`target.CubeGrid.RemoveBlock(target, true)` at line 333 (3 lines earlier).
Asking the SE engine to compute world coordinates for a block whose physical
grid entry has just been torn down may trigger fallback paths or block-cache
invalidation work.

Secondary candidates:
- `_TransportInventory.CurrentVolume` (engine property accessor).
- `target.IsFullyDismounted` (engine property accessor; the previous code path
  set this state via `DecreaseMountLevel` only ~5 ms earlier, so the engine
  may still be propagating).
- The ProtoBuf-decorated setters on `SyncBlockState` (cheap per inspection,
  but listed for completeness).

## Root Cause

Unknown — confirmed only by elimination via the existing
`ServerEmptyTransportInventory` profiler. Same diagnostic-first pattern as
BUG-105 / BUG-121 / BUG-122: split `tsTransport` into sub-segments, identify
the dominant cost, fix it in a follow-up.

## Fix (this ticket — instrumentation only, no behavioral change)

Split `tsTransport` into four sub-timers in `NanobotSystem.Grinding.cs:325-338`:

| Sub-timer        | Wraps |
|---|---|
| `tsTransportGate` | the `if` evaluation (`_TransportInventory.CurrentVolume` + `target.IsFullyDismounted`) |
| `tsTransportPos`  | `ComputePosition(target)` (the prime suspect — engine call on a just-removed block) |
| `tsTransportSet`  | the four `State.CurrentTransport*` setter assignments + `TimeSpan.FromSeconds` |
| `tsTransportEmpty`| `ServerEmptyTransportInventory(true)` (sanity check — should match the existing profiler max ~0.2 ms) |

Append fields to the existing `ServerDoGrind` profiler log line:
`transportGateMs`, `transportPosMs`, `transportSetMs`, `transportEmptyMs`.

All gated by `if (profilerTs != 0L)` per BUG-098 hygiene — zero overhead with
profiling off.

## Verification

1. **Build clean** — `dotnet build ... -c Release -v minimal`.
2. **Re-profile** the same 3-large-ship grinding scenario.
3. Identify which new field carries the 19 ms cost. Likely outcomes:
   - **`transportPosMs` dominates**: confirmed `ComputeWorldCenter` on a
     just-removed block. Fix: capture the world center *before* `RemoveBlock`
     (line 333) into a local, then assign that local to `State.CurrentTransportTarget`
     instead of re-computing post-removal. The block reference still has its
     pre-removal coordinates immediately before raze.
   - **`transportGateMs` dominates**: the gate is the issue. Fix: cache
     `IsFullyDismounted` into a local before the gate (we already read it on
     line 285 to drive the dismount path; reuse that result).
   - **`transportSetMs` dominates**: ProtoBuf setter side-effects. Investigate
     whether `Changed = true` triggers anything synchronous; most likely a
     ConcurrentDictionary write on each setter.
   - **`transportEmptyMs` dominates**: contradicts the existing
     `ServerEmptyTransportInventory` profiler — file follow-up to reconcile.
4. **No behavioral change** — all changes are `Stopwatch.GetTimestamp()` wraps.

## Context — why this matters

In the 3-large-ship scenario the user reports lag when grinding. Per-tick
math: BUG-106 caps full-dismounts to 3 per tick. With `transportMs` at 19 ms
on full-dismount samples, three dismounts in one tick cost 3 × 19 = 57 ms,
well over the 16.7 ms frame budget at 60 Hz — directly explains the dipped
sim-speed (0.57 min observed). Reducing `transportMs` from 19 ms toward the
sub-millisecond range (which the other timers in `ServerDoGrind` already
achieve) would cut per-tick dismount cost from ~60 ms to ~5 ms.

Independent of this ticket, the 3-large-ship scenario also stresses background
scan throughput (`AsyncClusterScan` 757 ms avg / 933 ms max, walks ~35 000
blocks per cycle). Background pressure is real but does not directly cause
main-thread spikes — the same scenario before any BUG-124 fix would still
benefit from per-tick spike reduction.

## See also

- Profile session: `20260429184659-profiling` (2026-04-29, 58 BaRs, 3 large ships, sim 0.93 avg / 0.57 min).
- BUG-105 / BUG-106 — the original raze/decrease/transport instrumentation.
- BUG-121 / BUG-122 — the same diagnostic-first pattern applied to wrapper / loop body.
- Plan file: `C:\Users\ICTlogix\.claude\plans\iterative-questing-oasis.md` (recommendation #2 realized here).

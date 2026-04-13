# BUG-095: Scan start early-exit clears in-flight scan flag — permits overlapping scans

## Status: Fixed
## Severity: Medium
## Version: v2.5.3
## Found In: Code review (v2.5.3) — `NanobotSystem.Scanning.cs` — `StartAsyncClusterScan` + `StartAsyncApplyClusterResults` early-exit paths

## Description

`StartAsyncClusterScan` (line 755) and `StartAsyncApplyClusterResults` (line 1123) both run on the main thread from the update loop. Each begins with an early-exit block that fires when the BaR is disabled, unpowered, or `State.Ready == false`:

```csharp
if (!_Welder.Enabled || !_Welder.IsFunctional || State.Ready == false)
{
    lock (State.PossibleWeldTargets) { State.PossibleWeldTargets.Clear(); State.PossibleWeldTargets.RebuildHash(); }
    lock (State.PossibleGrindTargets) { State.PossibleGrindTargets.Clear(); State.PossibleGrindTargets.RebuildHash(); }
    lock (State.PossibleFloatingTargets) { State.PossibleFloatingTargets.Clear(); State.PossibleFloatingTargets.RebuildHash(); }
    lock (_Welder) { _AsyncUpdateSourcesAndTargetsRunning = false; }   // BUG-095
    _InitialScanCompleted = false;
    _LastTargetsUpdate = MyAPIGateway.Session.ElapsedPlayTime;
    _LastSourceUpdate = _LastTargetsUpdate;
    return;
}
```

The flag write **unconditionally** forces `_AsyncUpdateSourcesAndTargetsRunning = false` without first checking whether a background scan started on a previous tick is still in flight. The flag was made the gate for "one scan in flight at a time" in BUG-002; the enqueue path at line 775 honors it, but this early-exit bypasses it.

## Steps to Reproduce

Hard to hit deterministically — requires the BaR to flip `Enabled` or `IsFunctional` across two consecutive update ticks while a background scan is running.

1. BaR enabled and powered. `StartAsyncClusterScan` enqueues a scan on tick N; flag is set to `true`; background thread begins scanning.
2. Tick N+1: BaR gets toggled off (terminal action, power loss, ownership change, grid split, safe zone event). Main thread re-enters `StartAsyncClusterScan`, takes the early-exit, clears all three target lists, and forces flag to `false` — **even though the background scan is still executing**.
3. Tick N+2: BaR toggled back on within the same scan window (e.g. power restored, or a brief functional blip). Main thread re-enters `StartAsyncClusterScan`. Flag reads `false` (cleared in step 2). The normal path at line 775 enqueues a **second** scan while the first is still running.
4. Both background scans now race to write the result into the cluster's shared result slot via `cluster.SetResult(result)` (line 943), and both invoke `ApplyClusterResultToSelf` on the same BaR concurrently. The list swap at 1715–1757 takes list-level locks individually, so the two scans can interleave halfway — e.g. weld list from scan A, grind list from scan B, floating list from scan A — producing an incoherent snapshot for one or more ticks.

The most observable symptom is sporadic `InvalidOperationException`s or wrong-cluster target data after a disable/re-enable bounce, not a continuous failure.

## Root Cause

The early-exit was written as "terminal cleanup — mark everything not-running and go home" under the assumption that a scan cannot be in flight in this branch. That assumption is wrong: the enqueue flag is set on the previous tick's pass through this same function, and the update-loop cadence (10–16 ticks per scan duration) allows the BaR's enabled/functional state to flip between enqueue and completion.

The enqueue path below (line 775) correctly gates on `_AsyncUpdateSourcesAndTargetsRunning`; the early-exit path is the only write site that ignores it.

Note that the finally-block iteration crash BUG-094's sibling fix addressed (NanobotSystem.Scanning.cs:1800 — see the v2.5.3 commit that added `if (profilerTs != 0L)` + list locks around the grid-count walk) is a separate symptom of the same underlying issue: main-thread clears racing the background scan's tail. Locking the finally-block iteration closed the crash symptom but did not close this scan-overlap window.

## Fix

Two options, both minimal:

**A — Defer cleanup while a scan is in flight (preferred).** Check the flag first; if a scan is running, return and let it complete normally. Its outer finally at line 961/1184 already clears the flag under `lock(_Welder)`, and the next tick's update will re-enter this early-exit and do the cleanup if the BaR is still disabled.

```csharp
if (!_Welder.Enabled || !_Welder.IsFunctional || State.Ready == false)
{
    lock (_Welder)
    {
        if (_AsyncUpdateSourcesAndTargetsRunning) return;
    }
    lock (State.PossibleWeldTargets) { State.PossibleWeldTargets.Clear(); State.PossibleWeldTargets.RebuildHash(); }
    lock (State.PossibleGrindTargets) { State.PossibleGrindTargets.Clear(); State.PossibleGrindTargets.RebuildHash(); }
    lock (State.PossibleFloatingTargets) { State.PossibleFloatingTargets.Clear(); State.PossibleFloatingTargets.RebuildHash(); }
    _InitialScanCompleted = false;
    _LastTargetsUpdate = MyAPIGateway.Session.ElapsedPlayTime;
    _LastSourceUpdate = _LastTargetsUpdate;
    return;
}
```

With this change the flag write at the bottom of the early-exit becomes unnecessary — control only reaches the clear code when the flag is already `false`, so writing `false` is a no-op and can be dropped.

**B — Spin-wait (rejected).** Block the main thread until the background scan finishes before clearing. Simpler but stalls the update loop for up to a scan duration (~80 ms). Not acceptable on the update path.

## Trade-offs and notes

- With Option A, on a disable/re-enable bounce the pre-disable scan's results will be swapped into the state lists for one tick before the next early-exit clears them. That's 1 tick of stale data on the terminal display, which is harmless — the BaR isn't acting on it because it's disabled.
- Close() at `NanobotSystem.Init.cs:180` already uses the same flag-under-lock pattern to wait for in-flight scans. This fix aligns the start-path early-exit with that convention.
- Related history: BUG-002 (v2.5.0) fixed the equivalent issue in the happy-path finally. BUG-073 is about Close()'s spin-wait. This bug is the missing third case: the disabled-path early-exit.

## See also

- `NanobotSystem.Scanning.cs:755` `StartAsyncClusterScan`
- `NanobotSystem.Scanning.cs:1123` `StartAsyncApplyClusterResults`
- `NanobotSystem.Scanning.cs:961`, `:1184` background finally that clears the flag under lock
- `NanobotSystem.Init.cs:180` Close() waiter — same lock, same field
- BUG-002 (Done, v2.5.0) — earlier async flag race on the happy path

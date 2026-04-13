# BUG-101: Weld-loop exhaustion fast-path can misclassify grid-limited systems as idle and collapse active BaRs

## Status: TODO
## Severity: High
## Version: v2.5.3
## Found In: `NanobotSystem.Welding.cs:51-59, 287-293`, `NanobotSystem.Operations.cs:124-128, 362-373`

## Description

Issue #133 reports that when `BaR count > MaxSystemsPerTargetGrid`, the expected behavior is "up to the cap remain active" (e.g., 10), but in practice only one BaR may keep working while the rest stay idle until limits are disabled.

Main-branch review shows a remaining path that can produce this under-utilization pattern even though the hard limiter itself exists and is generally correct.

## Root Cause

The weld-loop fast-path (`_weldLoopExhausted`) is currently set when:

```csharp
if (!welding && !needWelding && totalComponentChecks == 0 && !hadLockOn)
{
    _weldLoopExhausted = true;
    _weldExhaustedAtHash = State.PossibleWeldTargets.CurrentHash;
}
```

But `needWelding` is only set **after** a target passes limiter/assignment gates.  
When all candidate blocks are skipped by `IsGridOverSystemLimit(...)`, `needWelding` stays `false`, so the system is marked "exhausted" even though weld work still exists and only temporary slot availability is the blocker.

On subsequent ticks, if target hash is unchanged, the BaR exits early before any re-attempt:

```csharp
if (_weldLoopExhausted && State.PossibleWeldTargets.CurrentHash == _weldExhaustedAtHash)
    return;
```

This creates a starvation mode where non-lock-on BaRs can remain idle for long periods waiting for hash churn, instead of re-attempting to claim newly free limiter slots.

## Why this matches #133 symptoms

- Repro happens specifically when many BaRs target one grid and limiter is enabled.
- Disabling limiter removes the skip condition and BaRs immediately become active again.
- The "only one active" appearance can emerge when one BaR retains lock-on/progress while others sit in exhausted fast-path.

## Proposal

1. **Do not set `_weldLoopExhausted` when limiter/assignment gating occurred.**
   - Track per-loop reasons (`skippedByGridLimit`, `skippedByAssign` already exist).
   - Only mark exhausted when there were truly no actionable targets for non-limiter reasons.

2. **Add periodic retry while exhausted (safety net).**
   - Even with unchanged target hash, force a real weld-loop pass every N ticks.
   - Prevents long starvation windows caused by hash-stable lists.

3. **Add explicit profiler/debug reason fields for fast-path entry.**
   - Distinguish `exhausted=noTargets` vs `exhausted=gridLimited` vs `exhausted=assigned`.

## Verification

1. Set up 20-30 BaRs on one large grid with `MaxSystemsPerTargetGrid=10`.
2. Ensure abundant weld targets and components.
3. Observe active welders over time with `/nanobars debug show` and profiler logs.
4. Expected after fix:
   - Active welders stabilize around cap (<=10), not near 1.
   - Idle BaRs re-attempt promptly when slots free up, even if scan hash is unchanged.
5. Regression checks:
   - FEAT-026 perf gain preserved when no work exists.
   - BUG-052 limiter overshoot remains fixed (never exceed cap).

## Related

- Issue #133 (reported behavior)
- BUG-018 (secondary-mode fallback when primary is grid-limited)
- BUG-052 (live grid counter + lock-on limiter enforcement)
- BUG-097 (same-grid Dec/Inc dip race in grid counter)

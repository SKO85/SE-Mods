# BUG-122: ServerTryWelding loop body has ~15 ms unaccounted cost; ServerFindMissingComponents projected-block 9-10 ms spike has no internal sub-timing

## Status: Fixed
## Severity: High (now the largest visible main-thread spike, post-BUG-121)
## Version: v2.5.5
## Found In: `NanobotSystem.Welding.cs` `ServerTryWelding`, `NanobotSystem.Inventory.cs` `ServerFindMissingComponents`

## Description

BUG-121 closed the wrapper-level diagnosis: the 35 ms wrapper spike in profile
session `20260429181044-profiling` is fully **inside**
`ServerTryWeldingGrindingCollecting` → `ServerTryWelding`. Top spike trace:

```
Wrapper                                 35.104 ms  throttle=fired
└─ ServerTryWeldingGrindingCollecting   34.753 ms
   └─ ServerTryWelding                  27.035 ms
      ├─ ServerFindMissingComponents     9.075 ms  ← (1) projected SmallHydrogenThrust
      ├─ ServerDoWeld                    3.105 ms
      └─ ~14.85 ms unaccounted          ← (2) inside the loop body
```

Two unprofiled costs to surface:

### (1) ServerFindMissingComponents projected-block spike (9-10 ms)

`ServerFindMissingComponents` log line today only reports outer fields
(`block`, `projected`, `transportStarted`, `distance`). The call internally
walks the block definition's component list, queries `_TransportInventory` and
`_PossibleSources`, and (for projected blocks) inspects the projector's build
state. We don't know which sub-step costs the 9-10 ms.

Both top spikes hit **projected** blocks (`SmallHydrogenThrust` 9.075 ms,
`LargeBlockLargeContainer` 10.335 ms) — the projected branch likely costs
significantly more than the regular branch.

### (2) ~15 ms unaccounted inside ServerTryWelding loop

ServerTryWelding logs `weldChecked`, `skipLock`, `skipIgnore`, `skipGrid`,
`skipAssign`, `componentFails`, `starvedSkip`, `compChecks`, `nextCap` — counters,
not timings. With `weldChecked=2 compChecks=1` and the two profiled callees
accounting for only 12 ms of a 27 ms total, ~15 ms is happening between the
counters somewhere — candidates:

- The two `Weldable()` engine calls (BUG-116 cheap pre-filter applies, but
  Weldable is still called on the 2 candidates that passed it).
- `BlockSystemAssigningHandler.AssignToSystem` / `ReleaseFromSystem` swap.
- `_PossibleWelder` / `_PossibleSources` lock acquisition contention with the
  background scan publish.
- Lock-on resolution (`IsSameBlock` walk) when `hadLockOn=False` doesn't apply,
  but other counters might still drive cost.
- `ComputePosition`, `targetData.Distance` recompute during selection.
- A GC pause on the same frame (untrackable inside the SE sandbox per BUG-092).

## Root Cause

Unknown — same diagnostic pattern as BUG-105/121. Counters tell us what was
iterated; timings tell us where time went. We have the first half but not the
second.

## Fix (this ticket — instrumentation only)

### `NanobotSystem.Inventory.cs` `ServerFindMissingComponents`

Wrap the major internal segments with `Stopwatch` sub-timers and append fields
to the existing log line. Specifically:

| Sub-timer        | Wraps |
|---|---|
| `tsCompList`     | the loop that enumerates the block definition's component list |
| `tsProjQuery`    | (projected branch only) the projector inspect / `BuildableBlocksCount` / projected-component lookups |
| `tsInvLookup`    | the `_TransportInventory` / `_PossibleSources` consult to determine availability |

Existing fields preserved; new fields appended (`compListMs`, `projQueryMs`,
`invLookupMs`).

### `NanobotSystem.Welding.cs` `ServerTryWelding`

The original plan called for `tsLoopFilter` / `tsLoopAssign` / `tsLoopSelect`
sub-timers around the per-iteration filter chain. While reading the code, I
found `Weldable` already has its own `MethodProfiler.StopAndLog("Weldable", ...)`
instrumentation — and the session-`20260429181044` data shows it's clean
(max 0.344 ms across 8 485 calls; only 0.039 ms total for the spike's two
calls). `ServerEmptyTransportInventory` is also already profiled (max 0.136 ms).
That rules out the per-iteration filter / engine-call hypothesis entirely.

The next-most-likely suspect is **lock contention on `State.PossibleWeldTargets`**.
The background scan publish path locks the same collection at
`NanobotSystem.Scanning.cs:1472` (`State.PossibleWeldTargets.Clear()` /
`RebuildHash()` under `lock`). If `ApplyClusterResultToSelf` holds the lock
while ServerTryWelding tries to acquire, the wait time is real but invisible
to all per-iteration sub-timers — they only run after lock acquisition.

Single accumulator pair (cleanest possible measurement):

| Sub-timer        | Wraps |
|---|---|
| `tsLockAcquire`  | wall time from "before `lock(...)`" to "first statement inside lock" — pure acquire-wait |
| `tsInLock`       | wall time from acquire to just before the lock's closing brace — total in-lock work |

If `lockAcquireMs` carries multi-millisecond values on spike samples, lock
contention is confirmed and the follow-up fix is one of: (a) shorten the
publish-path lock hold by doing the rebuild outside the lock and only swapping
in under it, (b) use a finer-grained lock or a reference swap as
`PossibleWeldTargets` already supports a hash-stamped pattern, or (c)
restructure `ServerTryWelding` to copy the target list inside a brief lock and
iterate outside.

If `lockAcquireMs` is consistently zero on spike samples, the residual is
inside the loop in code that's not Weldable / ServerEmptyTransport / the
profiled callees — most likely a GC pause (BUG-092 territory) or
`AssignToSystem`/`ReleaseFromSystem` engine internals which would warrant
their own ticket.

All sub-timers gated by `if (profilerTs != 0L)` per BUG-098 closure-allocation
hygiene — production overhead zero when profiling off.

## Verification

1. **Build clean** — `dotnet build ... -c Release -v minimal`.
2. **Re-profile** the same scenario (welding-active 58-BaR cluster).
3. Identify which new field carries the 9-10 ms `ServerFindMissingComponents`
   cost and which carries the ~15 ms unaccounted ServerTryWelding cost.
4. Open follow-up fix ticket per the data — most likely:
   - If `tsProjQuery` dominates: cache projector buildable-component data per
     projector EntityId until projection version changes (similar shape to
     BUG-115's broken-block cache).
   - If `tsInvLookup` dominates: HashSet dedup or pre-aggregated component
     map (similar to BUG-119's `_ScanSourceDedupSet`).
   - If `tsLoopAssign` dominates: investigate `BlockKey` struct cost (already
     optimized by BUG-098) — most likely lock contention.
   - If none of the new fields dominate: GC pause; revisit in light of
     BUG-092's "won't fix" trade-off.

## See also

- Profile session: `20260429181044-profiling` (2026-04-29, 58 BaRs, welding-active scenario, sim 1.01 avg / 0.95 min).
- Diagnostic-first chain: BUG-105 → BUG-106/107/108 (grind/weld engine costs);
  BUG-121 → BUG-122 (wrapper → loop body).
- Plan file: `C:\Users\ICTlogix\.claude\plans\iterative-questing-oasis.md`.

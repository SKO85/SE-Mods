# BUG-113: Add finer sub-timers to ServerDoWeld

## Status: Fixed
## Severity: Low (diagnostic)
## Version: v2.5.4
## Found In: `NanobotSystem.Welding.cs`

## Description

The previous `ServerDoWeld` instrumentation (BUG-105/108) covers the heavy operations: `proj.Build` (`buildMs`), the projected→physical resolve (`resolveMs`/`resolveCoordMs`/`resolveLookupMs`), `IncreaseMountLevel` for incremental welding (`mountMs`), deformation IncreaseMountLevel (`deformMs`), and `MoveItemsToConstructionStockpile` (`stockpileMs`).

The latest welding profile (`welding` session, 23:00–23:02 UTC) confirmed `buildMs` dominates projected-block welds (e.g. 6.66 ms of a 6.68 ms `SmallBlockCockpit` build). But several **engine and handler calls inside `ServerDoWeld` are still uninstrumented** — any of them could be a hidden contributor to outlier spikes:

| Call site | What it does | Possible cost |
|---|---|---|
| `_TransportInventory.FindItem(componentId)` | Search transport inventory for the creation component | O(items in inventory) |
| `_Welder.IsWithinWorldLimits(...)` | Engine check — PCU / world-limit / per-faction quota | Engine call, can be costly under heavy worlds |
| `target.CanContinueBuild(_TransportInventory)` | Engine call — checks whether components for next integrity step exist | Engine call, scales with components |
| `IsWeldIntegrityReached(target)` (×2 sites) | `target.IsFullIntegrity` / `Integrity >= GetRequiredIntegrity` | Property reads (cheap, but worth measuring) |
| `targetData.Block.ReleaseFromSystem()` + `target.AssignToSystem(...)` | `BlockSystemAssigningHandler` dictionary ops on assignment swap | Dict ops; under contention, lock-free CAS retries |

## Fix (instrumentation only — no behavior change)

`NanobotSystem.Welding.cs` `ServerDoWeld` — added five new sub-timers:

```csharp
long tsFindItem = 0, tsLimitsCheck = 0, tsCanContinue = 0,
     tsIntegrityCheck = 0, tsAssign = 0;
```

Wrapped each call:

- **`tsFindItem`** — `_TransportInventory.FindItem(blockDefinition.Components[0].Definition.Id)`
- **`tsLimitsCheck`** — `_Welder.IsWithinWorldLimits(...)` (extracted to local before the if to time it)
- **`tsCanContinue`** — `target.CanContinueBuild(_TransportInventory) || CreativeModeActive`
- **`tsIntegrityCheck`** — both `IsWeldIntegrityReached(target)` call sites (entry check + completion check, summed)
- **`tsAssign`** — both `targetData.Block.ReleaseFromSystem()` and `target.AssignToSystem(_Welder.EntityId)` (paired swap, summed)

The existing `ServerDoWeld` profile log line is extended with five new fields:
`findItemMs`, `limitsMs`, `canContinueMs`, `integrityCheckMs`, `assignMs`.

The early-exit log path (when `TryClaimProjBuildSlot` denies the slot) also includes the new fields, with only `findItemMs` and `limitsMs` populated since the early return happens after those calls but before the others.

## What this gives us

After re-profiling, the `ServerDoWeld` log line will fully account for the call's time:

```
ServerDoWeld total =
   buildMs + resolveMs + stockpileMs + mountMs + deformMs +
   findItemMs + limitsMs + canContinueMs + integrityCheckMs + assignMs +
   <unprofiled overhead>
```

If a future spike shows large unaccounted-for time with all sub-timers small, that's evidence of GC pause or lock contention rather than a specific call. If one sub-timer dominates, we've identified the culprit.

## No behavior change

Pure `Stopwatch.GetTimestamp()` wraps. Same API calls in the same order. Profiler-disabled overhead is zero (sub-timers initialized to 0 and emitted only inside the `if (profilerTs != 0L)` log block).

## Verification

1. **Build clean** — `dotnet build ... -c Release -v minimal` → 0 warnings, 0 errors. ✓
2. **Re-profile a welding scenario** that previously produced a spike. The new fields will reveal the dominant sub-cost.
3. **No regression**: same set of welds, same outcomes.

## Result (profile session `20260428232214`)

The new sub-timers fully account for every `ServerDoWeld` call. Top spike breakdown:

```
ServerDoWeld=2.491ms  block=SmallBlockCockpit  created=True
  buildMs=2.474     ← 99.3% of total
  resolveMs=0.009
  stockpileMs=0.001  mountMs=0.000  deformMs=0.000
  findItemMs=0.001  limitsMs=0.000  canContinueMs=0.000
  integrityCheckMs=0.001  assignMs=0.007
  ────
  Σ sub-timers ≈ 2.493 ms ≈ total. Unprofiled overhead: ~0 ms.
```

Same pattern across all top spikes. **`proj.Build()` is the entire cost**. All other sub-timers — including the new BUG-113 ones (`findItemMs`, `limitsMs`, `canContinueMs`, `integrityCheckMs`, `assignMs`) — are sub-10 µs each. The diagnostic conclusively rules out hidden BaR-mod-side cost in `ServerDoWeld`.

The instrumentation stays in place permanently. If a future profile shows a spike with non-buildMs dominance, the fields will identify the culprit immediately.

## See also

- BUG-105 / BUG-108 — earlier instrumentation passes that established the pattern.
- BUG-107 — `proj.Build` budget; the early-exit log path now includes the new sub-timer fields too.
- Profile session `welding` (`20260428230208-profiling`-equivalent) showed the previous instrumentation.

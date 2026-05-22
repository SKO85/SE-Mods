# BUG-097: Work mode GrindIfWeldGetStuck was identical to WeldBeforeGrind; priority hash and GridSystemCount races

## Status: Fixed
## Severity: High (work mode) / Medium (priority race) / Low (grid count race)
## Version: v2.5.3
## Found In: `NanobotSystem.Operations.cs`, `Handlers/PriorityHandling.cs`, `Models/SyncBlockState.cs`

## Description

Three independent issues found during a deep audit of the scan/sort/priority/work-mode pipeline. Grouped into one ticket because they all landed in the same review pass and the fixes are small.

### A. [High] `WeldBeforeGrind` and `GrindIfWeldGetStuck` behave identically

> **Update (v2.5.4 — see BUG-101):** the v2.5.3 narrowing described below introduced a deadlock — when there were no weld targets, `needWelding` stayed `false`, the `weldStuck` gate never opened, and the BaR sat permanently in `State: Idle`. After a player report, this section's resolution path was changed: `GrindIfWeldGetStuck` was **removed entirely** in v2.5.4. The mode was redundant with `WeldBeforeGrind` and the label was ambiguous. Sections B and C below remain as fixed in v2.5.3.

`NanobotSystem.Operations.cs:130-156` dispatches work modes. The English UI label for `GrindIfWeldGetStuck` is *"Grind if weld get stuck"* — users reasonably expect grinding to kick in **only when welding is actively blocked** (targets exist but cannot proceed: missing components, safe zone, priority-starved). When there's simply nothing to weld, the BaR should stay idle instead of falling through to grind.

The existing implementation used `!(welding || transporting)` as the fall-through condition for **both** `WeldBeforeGrind` and `GrindIfWeldGetStuck` — that fires whenever the welding pass didn't do anything, regardless of *why*. Selecting `GrindIfWeldGetStuck` therefore gave `WeldBeforeGrind` behavior. The `primaryStuck` variable that the `WeldBeforeGrind` branch sets is only used in the profiler log line, not for flow control — confirming nobody differentiates.

### B. [Medium] `_PrioHash` thread-safety race in `PriorityHandling<C,I>`

`Handlers/PriorityHandling.cs:69-90` — `GetPriority` and `GetEnabled` do `_PrioHash.TryGetValue(...)` **lock-free**. Both are called from the sort comparators on the background scan thread (via `BlockGrindPriority.GetPriority` / `BlockWeldPriority.GetPriority`) and from the main-thread weld/grind loops.

`UpdateHash` rebuilt `_PrioHash` in place with `Clear()` + repeated `Add(...)`. When the user reordered priorities in the terminal (main thread setting `_HashDirty = true`), the next `GetPriority` call from *any* thread entered `UpdateHash` and mutated `_PrioHash` while another thread's `TryGetValue` could be actively reading it. `Dictionary<int,int>` is **not** safe for concurrent read vs. write — the race can return a wrong priority, torn state, or throw `InvalidOperationException` under write/read contention.

Low-frequency in practice (needs user terminal interaction exactly while a background scan sort is running), but real.

### C. [Low] `GridSystemCount` dip on same-grid lock-on reference refresh

`NanobotSystem.Welding.cs:98` refreshes the lock-on reference every scan cycle because the background scan produces fresh `IMySlimBlock` objects for the same physical block each time. The `SyncBlockState.CurrentWeldingBlock` setter then decrements `GridSystemCount[oldGridId]` and increments `GridSystemCount[newGridId]` — but `oldGridId == newGridId` for same-grid swaps, so both operations hit the same counter bucket.

Between the `Dec` and the `Inc` (two separate `ConcurrentDictionary.AddOrUpdate` calls), the counter is briefly `N-1` instead of `N`. Another BaR on another thread running `IsGridOverSystemLimit` via `GetCachedSystemCountOnGrid` during that dip could see `N-1 < limit` and erroneously pass the `MaxSystemsPerTargetGrid` check, letting an extra BaR start welding on a grid that was already at cap. Race window is sub-microsecond; over millions of ticks across many BaRs, feasible.

`CurrentGrindingBlock` has the symmetric pattern and the same bug.

## Fix

### A. `GrindIfWeldGetStuck` — require actual stuck condition

`NanobotSystem.Operations.cs:150-158`:

```csharp
case WorkModes.GrindIfWeldGetStuck:
    ServerTryWelding(out welding, out needWelding, out transporting, out currentWeldingBlock);
    var weldStuck = needWelding && !welding && !transporting;
    if (weldStuck || (scriptControlled && Settings.CurrentPickedGrindingBlock != null))
    {
        primaryStuck = true;
        ServerTryGrinding(out grinding, out needGrinding, out transporting, out currentGrindingBlock);
    }
    break;
```

`needWelding=true && !welding && !transporting` means: the welding pass found eligible targets, flagged them as needing work, but couldn't proceed (no components, safe zone, priority starvation). That's "stuck". When weld targets don't exist at all, `needWelding=false` and the fall-through is suppressed — the BaR stays idle, which is the whole point of this mode.

`WeldBeforeGrind` and `GrindBeforeWeld` are unchanged; their "nothing active → try the other" fallback is the correct semantics for those modes.

### B. Atomic reference swap for `_PrioHash`

`Handlers/PriorityHandling.cs`:

- `_HashDirty` becomes `volatile bool` so the main-thread write propagates promptly to the background reader.
- New `_hashLock` object replaces the prior `lock(_ClassList)` idiom — locking the list itself was iffy because the list reference is reassigned by the new swap pattern.
- `UpdateHash` now builds **fresh** `MemorySafeList<string>` and `Dictionary<int,int>` inside the lock, then publishes both via field reassignment:
  ```csharp
  _ClassList = newClassList;
  _PrioHash = newPrioHash;
  ```
- `GetPriority` / `GetEnabled` snapshot `_PrioHash` into a local before `TryGetValue`:
  ```csharp
  var hash = _PrioHash;
  if (hash.TryGetValue(itemKey, out prio)) return prio;
  ```
  so a concurrent `UpdateHash` swap can't race with the lookup. Readers always see a fully-built dictionary.

Field assignment of a reference is atomic on x64 (and on all JITs the mod runs on). The `volatile` on `_HashDirty` is the release barrier for the new dictionary's contents.

### C. Skip Dec/Inc on same-grid swaps

`Models/SyncBlockState.cs` — both `CurrentWeldingBlock` and `CurrentGrindingBlock` setters now compute old and new `CubeGrid.EntityId` and only call `DecrementGridCount`/`IncrementGridCount` when `oldGridId != newGridId`:

```csharp
var oldGridId = (_CurrentWeldingBlock != null && _CurrentWeldingBlock.CubeGrid != null)
    ? _CurrentWeldingBlock.CubeGrid.EntityId : 0L;
var newGridId = (value != null && value.CubeGrid != null)
    ? value.CubeGrid.EntityId : 0L;
if (oldGridId != newGridId)
{
    if (oldGridId != 0L) Mod.DecrementGridCount(oldGridId);
    if (newGridId != 0L) Mod.IncrementGridCount(newGridId);
}
```

Same-grid swap (the common lock-on reference refresh path) now has zero counter impact — no dip, no race window. Cross-grid swaps (BaR moving from one grid's block to another's) still produce the correct net-zero total via one Dec and one Inc on different buckets.

## Performance impact

- **(A) Work mode**: one extra `&&` evaluation per tick in `GrindIfWeldGetStuck`. Zero measurable cost.
- **(B) Priority hash**: `UpdateHash` now allocates a new `Dictionary<int, int>` and `MemorySafeList<string>` per rebuild. Rebuilds happen only on user priority reorder — very infrequent (seconds apart at most). The common lookup path gains one extra field load (the snapshot into `hash`) and loses zero comparisons elsewhere. Net cost is negligible.
- **(C) Same-grid swap**: two integer comparisons replace two `ConcurrentDictionary.AddOrUpdate` calls (~60ns → ~2ns) on the hot path where lock-on references are refreshed every tick. Net **win**.

## Verification

1. **Build**: `dotnet build SKO-Nanobot-BuildAndRepair-System/Data/Scripts/SKO-Nanobot-BuildAndRepair-System/SKO-Nanobot-BuildAndRepair-System.csproj -c Release -v minimal` → 0 warnings, 0 errors. ✓
2. **A — GrindIfWeldGetStuck idle behavior**: configure BaR for `GrindIfWeldGetStuck`, place broken blocks + janitor-marked blocks in range. Grind janitor-marked blocks initially (weld pass will proceed on broken blocks and grind pass skipped because weld is NOT stuck). Fix all broken blocks, verify grinding does NOT start (no `needWelding` → no fall-through). Contrast: `WeldBeforeGrind` in the same scenario falls through to grind.
3. **A — GrindIfWeldGetStuck stuck behavior**: remove all components from the welder's conveyor source. Broken blocks become weld-targets that can't be welded (`needWelding=true`, `welding=false`). Verify the grind pass runs on janitor-marked blocks because the weld is actively stuck.
4. **B — Priority race**: stress test by rapidly moving priority items up/down in the terminal while a heavy multi-BaR scan is running. Before: occasional `InvalidOperationException` in `Dictionary` or wrong priority ordering in profile logs. After: no exceptions, priority changes visible on next scan cycle.
5. **C — GridSystemCount dip**: place 21 BaRs on a grid with `MaxSystemsPerTargetGrid=20`. Under heavy target churn (background scan refreshing lock-on references every ~2s), monitor `/nanobars debug show` live counter — before: occasional 21 BaRs active on the same grid. After: stays at ≤20.
6. **Regression checks**:
   - `WeldBeforeGrind` / `GrindBeforeWeld` / `WeldOnly` / `GrindOnly`: unchanged, should behave identically to v2.5.3 pre-fix.
   - Priority sorts: confirm the sort output is identical when priorities are stable (no user reorder during scan).
   - Same-grid lock-on cycle: confirm `GridSystemCount[gridId]` stays stable across scan cycles for a BaR that holds a steady lock-on.

## See also

- BUG-094 / BUG-096 (v2.5.3) — recent fixes to the scan sort pipeline; none of those masked these three issues.
- FEAT-070 (v2.5.3) — sort comparator consolidation; the new shared helpers inherit the benefit of fix (B) automatically (all sites go through `GetPriority`, now race-free).
- `Welding.cs:98` — the lock-on refresh site that triggers fix (C).
- **BUG-101** — section A's narrowing introduced a deadlock: when there were no weld targets (or the weld loop was exhausted), `needWelding` stayed `false`, the `weldStuck` gate never opened, and the BaR sat permanently in `State: Idle`. A player report confirmed the regression. Resolved in a follow-up release by **removing `GrindIfWeldGetStuck` entirely** — it was functionally redundant with `WeldBeforeGrind` and the label was ambiguous. Saved values migrate to `WeldBeforeGrind` on load. Sections B and C of this ticket remain as fixed in v2.5.3.

# BUG-112: AsyncAddBlockIfWeldTarget filter — move NeedRepair before relation lookup

## Status: Fixed
## Severity: Medium (perf — large-grid scan cost)
## Version: v2.5.4
## Found In: `NanobotSystem.Scanning.cs`

## Description

Profile session `20260428222525-profiling` showed `AsyncAddBlocksOfGrid` taking 219-237 ms per scan on an 11,112-block grid (`gridId=116207788909290906`). Background-thread, so doesn't directly hurt sim-speed, but it's a substantial CPU cost on the scan thread that scales with grid size.

The per-block filter chain in `AsyncAddBlockIfWeldTarget` (non-projected branch, `Scanning.cs:466-469` pre-fix) was:

```csharp
if ((!useIgnoreColor || !IsColorNearlyEquals(ignoreColor, colorMask)) &&
    (!useGrindColor || !IsColorNearlyEquals(grindColor, colorMask)) &&
    BlockWeldPriority.GetEnabled(block) &&        // dict lookup     ~100 ns
    IsRelationAllowed4Welding(block) &&            // ENGINE CALL     ~1-10 µs
    block.NeedRepair(Settings.WeldOptions))        // integrity check ~50-200 ns
```

`block.NeedRepair` was the **last** condition in the `&&` chain. C# short-circuit evaluation runs left-to-right, so for every block on the grid we ran:

1. Color comparisons (cheap, ~50 ns each)
2. Priority dict lookup (~100 ns)
3. **Engine relation lookup** (`block.GetUserRelationToOwner(_Welder.OwnerId)` — 1-10 µs)
4. Then finally checked if the block actually needed repair

For a stable base where 99 % of blocks are full integrity, **the relation lookup ran on blocks that were going to bail anyway**. The existing comment at `NanobotSystem.State.cs:107` explicitly flags this:

> Phase 3: Optimize — relation lookup is called per-block per-scan, consider caching per-grid.

## Fix

Reorder so `NeedRepair` runs first:

```csharp
if (block.NeedRepair(Settings.WeldOptions) &&                      // Cheapest first — bails on full integrity
    (!useIgnoreColor || !IsColorNearlyEquals(ignoreColor, colorMask)) &&
    (!useGrindColor || !IsColorNearlyEquals(grindColor, colorMask)) &&
    BlockWeldPriority.GetEnabled(block) &&
    IsRelationAllowed4Welding(block))                              // Expensive last
```

Single-line semantic change. `NeedRepair` is a `Utils.cs:18` extension method that:

1. Null check (instant)
2. `IsDestroyed` (property)
3. `FatBlock.Closed/MarkedForClose` (2 properties)
4. `WeldOptions == WeldSkeleton` (enum compare)
5. `target.Integrity >= GetRequiredIntegrity(...)` — typically fast
6. Deformation checks (only for full-integrity blocks)

For a fully built undeformed block (the dominant case on stable grids), it returns false in ~50-200 ns.

## Estimated impact on the 11k-block grid

Assuming 99 % of blocks are full-integrity stable armor:

- **Pre-fix per-block cost**: color (~100 ns) + priority (~100 ns) + relation (~3 µs avg) + needRepair (~100 ns) ≈ **3.3 µs**
- **Post-fix per-block cost**: needRepair (~100 ns), bail ≈ **100 ns** for the 99 % case

For 11,000 × 99 % = 10,890 full-integrity blocks:
- Before: 10,890 × 3.3 µs ≈ **36 ms saved per scan**
- After: 10,890 × 0.1 µs ≈ **1 ms**

Expected `AsyncAddBlocksOfGrid` reduction: **220 ms → ~185 ms** on the 11k-block grid (about a 16 % reduction). For grids with more damage the win is smaller, but never worse than the original.

## Why this is safe

- The **set of blocks that pass** the filter is unchanged: `&&` is commutative for the predicate's truth value, only the evaluation order changes.
- `NeedRepair` has a side effect (`ResetSkeleton` on deformed blocks) but that side effect is **gated on full-integrity-with-deformation**, which is exactly the same set of blocks as before — it was reachable via the original chain too, just after extra wasted work.
- Build clean, no compile errors.

## Grinding path: already cheap-first

Audited `AsyncAddBlockIfGrindTarget` for the same pattern — the existing chain already runs:

1. `block.IsProjected()` (cheap, instant bail on projected blocks)
2. `BlockGrindPriority.GetEnabled(block)` (dict lookup) — short-circuit gates the relation lookup
3. Only if priority enabled: `block.GetUserRelationToOwner(_Welder.OwnerId)` (engine call)

No reorder needed. `autoGrindRelation != 0 && BlockGrindPriority.GetEnabled(block)` already prevents the relation lookup on disabled-priority blocks.

## Verification

1. **Build clean** — `dotnet build ... -c Release -v minimal` → 0 warnings, 0 errors. ✓
2. **Re-profile the same scenario.** Expected outcomes:
   - `AsyncAddBlocksOfGrid` avg drops on stable-base scans (most blocks full integrity).
   - `AsyncClusterScan` avg drops proportionally (it sums per-grid costs).
   - Sim-speed unchanged (background-thread savings, doesn't directly affect main thread).
3. **Behavior unchanged**: same set of weld candidates, same priorities, same relations enforced. Only filter order is different.

## Future work (deferred)

The `NanobotSystem.State.cs:107` comment hints at a per-grid relation cache. With one grid having ~11k blocks and one BaR per scan, that's 11k engine calls per scan. Caching per-(grid, owner) pair would drop the remaining 1 % of repair-needed blocks from 3 µs to ~10 ns. Bounded incremental win — file as future ticket if needed. Not done in BUG-112 to keep this change minimal.

## See also

- BUG-110/111 — the previous allocation-pressure pass on the scan thread.
- `NanobotSystem.State.cs:107` — TODO comment about caching the relation lookup.
- Profile session: `20260428222525-profiling` (11k-block grid, 219-237 ms per AsyncAddBlocksOfGrid).

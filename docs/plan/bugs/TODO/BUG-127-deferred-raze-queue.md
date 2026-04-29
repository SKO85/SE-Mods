# BUG-127: Defer block raze (`CubeGrid.RemoveBlock`) into a per-tick processed queue to keep raze SE-engine cost off the grind tick

## Status: Open
## Severity: High (SE engine raze spike compounds with grind tick when 3 BaRs dismount simultaneously)
## Version: v2.5.5
## Found In: `NanobotSystem.Grinding.cs:329` (`target.CubeGrid.RemoveBlock(target, false)`), `Mod.cs` (queue host)

## Description

Player observation: `target.CubeGrid.RemoveBlock(...)` causes spikes when it
actually removes the block. Profile evidence: razeMs spikes 5-8 ms on complex
blocks (`LargeBlockBatteryBlock` 8.520 ms, `LargeBlockArmorBlock` outliers
6-7 ms). Most armor blocks are sub-millisecond, but conveyor-connected blocks
trigger SE-side integrity / conveyor topology / mass / event-fanout work.

BUG-106 caps full-dismount grinds to 3 per tick globally — but the
**raze itself happens on the same tick as the dismount grind**. Worst case:
3 BaRs dismount complex blocks on one tick = 3 × ~5 ms decrease + 3 × ~8 ms
raze ≈ 39 ms on the main thread, well over the 16.7 ms 60 Hz frame budget.

## Root Cause

SE engine raze cost is intrinsic — the engine must update grid integrity tree,
refresh conveyor topology adjacent to the removed block, recalc grid
mass/inertia, and fire block-removed events. We can't reduce per-call cost.
What we *can* do is decouple "block hit 0 integrity" from "block is razed."

## Fix — Deferred raze queue

Add a Mod-level FIFO queue. Grind sites enqueue instead of calling
`RemoveBlock` directly. `Mod.UpdateBeforeSimulation` drains a configurable
maximum number of entries per tick, calling `RemoveBlock` on each.

### `Mod.cs` (additions)

```csharp
// BUG-127: Deferred raze queue. Decouples "block hit 0 integrity" (cheap, ~µs to mark)
// from "engine fully removes block" (5-8 ms on conveyor-connected blocks). Without the
// queue, BUG-106's 3-per-tick dismount cap still let 3 razes pile on the same tick =
// 24 ms of engine cleanup over the 16.7 ms frame budget. With the queue, razes are
// processed at most MaxRazesPerTickDefault per tick — independent of which tick the
// underlying grind happened on, so the engine work is spread across multiple ticks.
public const int MaxRazesPerTickDefault = 1;
private static readonly Queue<IMySlimBlock> _razeQueue = new Queue<IMySlimBlock>();

public static void EnqueueRaze(IMySlimBlock block)
{
    if (block == null) return;
    lock (_razeQueue) { _razeQueue.Enqueue(block); }
}

public static int GetRazeQueueDepth()
{
    lock (_razeQueue) { return _razeQueue.Count; }
}

private void ProcessRazeQueue()
{
    var processed = 0;
    while (processed < MaxRazesPerTickDefault)
    {
        IMySlimBlock target;
        lock (_razeQueue)
        {
            if (_razeQueue.Count == 0) break;
            target = _razeQueue.Dequeue();
        }
        if (target == null) continue;
        try
        {
            var grid = target.CubeGrid;
            if (grid == null) continue;
            // Block could have been welded back up before we drained it; skip.
            // Engine returns IsDestroyed=true once integrity hits 0 and stays so until
            // RemoveBlock; if some other path razed it first, FatBlock may be Closed.
            if (target.FatBlock != null && target.FatBlock.Closed) continue;
            grid.RemoveBlock(target, false);
            processed++;
        }
        catch (Exception ex)
        {
            Logging.Instance.Error(ex);
        }
    }
}
```

`ProcessRazeQueue();` invocation added to `Mod.UpdateBeforeSimulation` once
per frame on the server path (alongside the existing periodic ladder).

### `NanobotSystem.Grinding.cs:326-330` (replacement)

Replace the immediate `RemoveBlock` call with an enqueue:

```csharp
tsMark = Stopwatch.GetTimestamp();
// BUG-127: defer raze to Mod's per-tick queue. The 5-8 ms SE-engine cleanup
// spike no longer co-occurs with the grind tick; it's processed at
// MaxRazesPerTickDefault rate from Mod.UpdateBeforeSimulation.
Mod.EnqueueRaze(target);
tsRaze = Stopwatch.GetTimestamp() - tsMark;  // now sub-microsecond
```

## Why this is safe

- **No double-grinding**: `ServerTryGrinding` already skips destroyed blocks
  (`if (targetData.Block.FatBlock != null && targetData.Block.FatBlock.Closed) continue;`
  + the `IsDestroyed` short-circuit at `Grinding.cs:73`). A block with
  `integrity=0` and queued for raze is `IsDestroyed=true` at the engine level;
  the existing checks already filter it out.
- **No item loss**: items were already drained into `_TransportInventory` by
  `DecreaseMountLevel` before this point. The queue only handles the
  removal-from-grid part.
- **Visual**: block at integrity=0 lingers an extra 1-N ticks (~16-50 ms at
  60 Hz with `MaxRazesPerTickDefault=1`). Typically invisible; matches what
  players already see on slow grinders.
- **Block reference validity**: we re-check `CubeGrid != null` and
  `FatBlock.Closed` inside `ProcessRazeQueue`; stale entries are skipped via
  try/catch.
- **Persistence on save**: queue is in-memory — pending entries lost on
  session save. Acceptable: a `IsDestroyed=true` block sitting at integrity=0
  in the grid is harmless; player can grind again or it'll be cleaned up next
  session.

## Throughput math

| Scenario | Before BUG-127 | With BUG-127 (cap=1) |
|---|---|---|
| 3 BaRs dismount complex blocks on tick T | 3 × 5 ms decrease + 3 × 8 ms raze = **39 ms** | 3 × 5 ms decrease = **15 ms**; razes spread across T+1, T+2, T+3 at 8 ms each = 8 ms / tick |
| Sustained grind throughput | 3 razes/tick @ 60 Hz = 180 razes/sec | 1 raze/tick @ 60 Hz = 60 razes/sec |
| Peak frame cost | 39 ms (sim-speed dip) | 15 ms (within budget) |

For 11k-block grids: 60 razes/sec × 60 sec = 3 600 blocks per minute. 3 large
ships = ~50 minutes total grind time. If players want it faster, raise
`MaxRazesPerTickDefault` to 2 (still 16 ms peak, on budget) or 3 (matches old
behaviour without queueing).

Future enhancement (separate ticket if needed): use
`IMyCubeGrid.RazeBlocks(List<Vector3I>)` plural-batch API to coalesce multiple
razes on the same grid into one engine integrity-recalc. Not in scope here —
shipping the simpler queue first to confirm the spike-smoothing gain.

## Verification

1. **Build clean** — `dotnet build ... -c Release -v minimal`.
2. **In-game smoke** in the 3-large-ship grinding scenario:
   - Grind into a high-component-density area (containers / batteries).
   - Compare sim-speed pattern against pre-BUG-127 baseline.
   - Expected: peak frame cost drops, sim-speed dips during dismount-storm
     should disappear or substantially reduce.
3. **Profile session** to quantify:
   - `ServerDoGrind` `razeMs` field should now report **sub-microsecond**
     (just the enqueue cost), not 5-8 ms.
   - Total `ServerDoGrind` `ms` on dismount samples should drop by the
     pre-existing razeMs amount.
   - New profile entry could be added for `ProcessRazeQueue` cost (one-line
     instrumentation, optional follow-up).
4. **Block-removal correctness**:
   - Confirm grinded blocks still actually disappear (queue does drain).
   - Confirm `Mod.GetRazeQueueDepth()` returns to 0 when grinding pauses.

## Settings tunable

`MaxRazesPerTickDefault = 1` is conservative. If players want to expose this
via `ModSettings.xml`, add a `MaxRazesPerTick` setting in `SyncModSettings`
(mirroring `MaxGrindsPerTick`). Out of scope for this ticket; player can edit
the `const` if they want different behaviour locally.

## See also

- BUG-106 — full-dismount budget (3/tick) for `DecreaseMountLevel` cascade. Stays.
- BUG-107 — `proj.Build` budget (3/tick). Stays.
- Future: `RazeBlocks(List<Vector3I>)` batch-API exploration as a follow-up
  if per-grid coalescing offers measurable additional win.

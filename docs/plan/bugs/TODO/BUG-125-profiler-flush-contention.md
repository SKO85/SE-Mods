# BUG-125: MethodProfiler `Write` does per-line `writer.Flush()` under a global lock — 58-BaR contention adds 8 ms wall-time per profiler exit during heavy ticks

## Status: Open
## Severity: Critical (profiler itself dominates measured spikes; contaminates all profile data on contended scenarios)
## Version: v2.5.5
## Found In: `Profiling/MethodProfiler.cs:682-723` `Write`

## Description

BUG-124 added wall-time sub-timers around the `tsTransport` block in
`ServerDoGrind`. Profile session `20260429185841-profiling` (3 large ships,
58 BaRs grinding, sim-speed dipped to 0.38 min) shows the smoking gun on the
top `ServerDoGrind` sample:

```
ServerDoGrind:           ms=10.172  transportEmptyMs=8.418  (BUG-124 outer measurement)
ServerEmptyTransportInventory:  ms=0.474  transportVol=0.000  (own profiler, same entity, same timestamp)
```

The outer measurement says **8.418 ms** wall time around the
`ServerEmptyTransportInventory(true)` call. The function's own profiler says
**0.474 ms**. The ~8 ms gap is happening inside `MethodProfiler.StopAndLog`
**after** the inner `elapsedTicks = Stopwatch.GetTimestamp() - startTimestamp`
capture but **before** the function actually returns to my outer
timestamp. Code at `MethodProfiler.cs:474-501`:

```csharp
public static void StopAndLog(...)
{
    if (startTimestamp == 0L || !IsEnabled) return;

    var elapsedTicks = Stopwatch.GetTimestamp() - startTimestamp;  // ← logged "ms" captured here
    var elapsedMs = elapsedTicks * 1000.0 / Stopwatch.Frequency;

    UpdateAggregate(methodName, elapsedMs);  // takes lock(_syncRoot)

    if (elapsedMs < _minDurationMs) return;

    string details = null;
    if (detailsFactory != null) { ... details = detailsFactory(); ... }

    Write(methodName, elapsedMs, details);  // ← the real cost
}
```

`Write` at line 682:

```csharp
private static void Write(string methodName, double elapsedMs, string details)
{
    var writer = GetOrCreateWriter(methodName);
    if (writer == null) return;

    var simSpeed = MyAPIGateway.Physics?.ServerSimulationRatio ?? 1.0f;

    lock (_syncRoot)                     // global lock — every BaR's every profile call contends here
    {
        if (simSpeed < _minSimSpeed) _minSimSpeed = simSpeed;
        if (simSpeed > _maxSimSpeed) _maxSimSpeed = simSpeed;
        _sumSimSpeed += simSpeed;
        _simSpeedSamples++;
    }

    var line = new StringBuilder(512);
    // ... format the line ...

    lock (_syncRoot)                     // global lock again
    {
        writer.WriteLine(line.ToString());
        writer.Flush();                  // ← sync file flush per line — kills throughput
    }
}
```

Two problems:

1. **`writer.Flush()` per line** — every profile log entry triggers an OS-level
   file flush. Modern SSDs handle ~1 000-10 000 sync ops/sec; with 58 BaRs and
   ~30-50 profiler exits per tick at 60 Hz, the queue is ~2 000 ops/sec. Each
   call waits for the OS to confirm the flush, and the OS serialises them
   through the file handle.

2. **Single global `_syncRoot` lock** — all profile writes from all BaRs and
   all methods contend on one lock. Even without `Flush`, lock acquisition under
   contention is expensive when 50+ threads queue up.

## Impact

The user's reported "lag during grinding" is largely a profiler-self-overhead
effect under heavy load:

- Top `ServerDoGrind` spike `ms=10.172` decomposes as ~1.7 ms real work
  (`decreaseMs=0.851 + razeMs=0.766` + `emptyMs/friendlyMs` etc.) plus
  ~8.4 ms profiler exit overhead inside `transportEmptyMs`.
- Each `ServerDoGrind` call exits two profilers (its own + the inner
  `ServerEmptyTransportInventory`). At 8 ms each on contended ticks =
  ~16 ms of profiler overhead per grind.
- 3 dismounts per tick × 16 ms = ~48 ms of profiler overhead per spike tick.
  At 16.7 ms frame budget (60 Hz), that's a sim-speed of ~0.35 — matching the
  observed 0.38 min sim-speed in the latest session.

**Without profiling enabled, this overhead is zero.** The mod's actual
performance under the same workload should be substantially better.

## Root Cause

Per-entry `writer.Flush()` (line 716) inside the global `_syncRoot` lock
forces every profile log line through a synchronous file flush. Combined with
58 BaRs writing concurrently, this serializes the main thread on disk I/O.

## Fix

Two-part minimal change in `MethodProfiler.Write`:

1. **Remove the explicit `writer.Flush()` call**. `TextWriter` (the underlying
   `StreamWriter` in SE's storage path) buffers internally and flushes on
   buffer-fill, on `Close()`, and on `Flush()` from the existing session-end
   `WriteSummary` / `StopSession` paths. Worst case on a hard crash: last
   ~1 KB of profile data per method log file (a few entries) is lost — acceptable
   because (a) profile data is per-session and reproducible, (b) a hard crash
   loses the in-memory aggregate stats anyway, (c) the existing summary line is
   what's used for analysis, not the last few raw entries.

2. **Add a defensive periodic flush** (e.g., every 5 s in the existing
   `Mod.UpdateBeforeSimulation` periodic-check ladder) so a long-running profile
   session that does not call `StopSession` (e.g., user closes SE) still has
   most of its data on disk. One flush call every 5 s × ~30 method writers =
   negligible cost.

The lock-contention angle (single `_syncRoot` for all writes) is a second-order
issue and intentionally not addressed in this ticket — we'll measure first
whether removing `Flush()` is sufficient. If a future profile still shows
multi-millisecond outer-vs-inner gaps, file a follow-up ticket to switch to
per-writer locks.

## Verification

1. **Build clean** — `dotnet build ... -c Release -v minimal`.
2. **Re-profile** the same 3-large-ship grinding scenario.
3. **Expected**: `ServerDoGrind` `transportEmptyMs` should now match
   `ServerEmptyTransportInventory`'s own profiler (~0.5 ms instead of 8 ms).
   Top `ServerDoGrind` should drop from 10 ms to ~2 ms.
4. **Sim-speed expected** to recover from 0.38 min toward 0.85+ on the same
   workload.
5. **Smoke**: confirm log files still get the expected entries, and the session
   summary line still reports correct `totalMs` / `maxMs` / `avgMs` per method.
6. **Independent validation**: also run a non-profiled session in the same
   scenario to establish a true performance baseline. If non-profiled
   sim-speed is high (~1.0) and profiled-with-fix is also high, the lag the
   user reported was indeed profiler self-overhead. If non-profiled is low,
   we have a separate underlying problem.

## See also

- Profile session: `20260429185841-profiling` (2026-04-29, 58 BaRs, 3 large ships, sim 0.93 avg / 0.38 min).
- BUG-124 — added the outer `transportEmpty` sub-timer that surfaced this gap.
- BUG-098 — earlier profiler hygiene work (closure-allocation guards). This is
  a related "profiler is heavier than expected" finding.

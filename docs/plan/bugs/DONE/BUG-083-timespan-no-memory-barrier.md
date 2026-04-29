# BUG-083: TimeSpan fields read/written without memory barrier
## Status: Won't Fix (accepted as known limitation)
## Severity: Low (theoretical only — no observable impact on target platform)
## Version: v2.5.0
## Found In: Code review round 6 — `NanobotSystem.Scanning.cs:36-37`

## Description
`_LastTargetsUpdate` and `_LastSourceUpdate` are `TimeSpan` fields (8 bytes) read from the game-loop thread (`UpdateSourcesAndTargetsTimer`) and written from background scan threads (`AsyncClusterScan`, `AsyncApplyClusterResults`) without synchronization. C# does not guarantee atomic reads/writes of 8-byte structs across all platforms; without a memory barrier, the game loop is not formally guaranteed to promptly see an updated value.

## Resolution: Won't Fix

After review, the trade-off favors leaving the current code as-is.

### Why no fix is warranted

- **Target platform is x64 only**: the mod runs on Space Engineers (.NET Framework 4.8, x64). On x64, aligned 8-byte reads/writes are atomic at the CPU level (Intel/AMD memory model guarantee). Torn reads of `TimeSpan` cannot be reproduced on the actual deployment platform.
- **Worst-case outcome is harmless**: the ticket itself describes the practical impact as "scan interval might be off by one cycle." That is smaller than natural jitter from background-thread scheduling and invisible to players.
- **No accumulating drift**: a stale read for one tick is corrected on the next read — the field is overwritten frequently by every scan completion.

### Why the proposed fixes would regress

- **Option 1 (long ticks + Volatile/Interlocked)**: more code, no observable improvement on x64. C# 6's `volatile` cannot apply to `long` directly; would require `Volatile.Read`/`Volatile.Write` calls on every access throughout the codebase. Maintenance burden for zero practical gain.
- **Option 2 (`lock(_Welder)`)**: adds lock overhead to a hot timer check that fires every frame. Measurable cost, no measurable benefit.

### Why profiling can't help

This is a memory-model concern, not a performance one:
- The "race" is at most a brief window of stale-read possibility (microseconds), invisible in profile logs.
- Even if reproduced, the visible effect is a single scan-cycle timing nudge — same magnitude as natural scheduling jitter.

## See also

- BUG-082 — adjacent low-impact race accepted on the same reasoning. Both are theoretical x64 memory-model concerns where the fix would cost more than the gain.

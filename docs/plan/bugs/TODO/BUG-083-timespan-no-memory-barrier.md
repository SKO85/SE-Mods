# BUG-083: TimeSpan fields read/written without memory barrier
## Status: Open
## Severity: Low
## Version: v2.5.0
## Found In: Code review round 6 — NanobotSystem.Scanning.cs:36-37
## Description
`_LastTargetsUpdate` and `_LastSourceUpdate` are `TimeSpan` fields (8 bytes) read from the game loop thread (`UpdateSourcesAndTargetsTimer`) and written from background scan threads (`AsyncClusterScan`, `AsyncApplyClusterResults`) without synchronization. C# does not guarantee atomic reads/writes of 8-byte structs on all platforms. On x64, `TimeSpan` writes are effectively atomic, but there is no memory barrier to ensure the game loop promptly sees the updated value.
## Steps to Reproduce
Theoretical — on x64 .NET 4.8, torn reads of 8-byte aligned fields are extremely unlikely. The practical impact is that a scan interval might be off by one cycle (scanning slightly too early or too late).
## Root Cause
`volatile` keyword cannot be applied to `TimeSpan` in C# 6. The fields would need `lock` protection or conversion to `long` ticks with volatile/Interlocked.
## Fix
Low priority. Options:
1. Convert to `long` tick fields and use volatile reads/writes
2. Wrap reads/writes in `lock(_Welder)` — adds lock overhead to the timer check
3. Accept as known limitation — impact is negligible on x64

# FEAT-005: Reduce Source/Push Target Scan Interval
## Status: Done
## Priority: Low
## Version: v2.5.0
## Summary
Reduced `SourcesUpdateInterval` from 60 seconds to 30 seconds.
## Motivation
Sources and push targets (cargo containers, assemblers, etc.) could be up to 60 seconds stale. When players add or remove cargo containers, the BaR took too long to recognize the changes.
## Design
Changed the default `SourcesUpdateInterval` in `SyncModSettings` from `TimeSpan.FromSeconds(60)` to `TimeSpan.FromSeconds(30)`. The source scan piggybacks on the existing target scan (every 10s), so the only cost is one extra inventory enumeration per 30s cycle.
## Files Affected
- `Models/SyncModSettings.cs` — changed default from 60 to 30
## Testing
- Add/remove cargo containers while BaR is running
- Verify source/push target counts update in info panel within ~30 seconds

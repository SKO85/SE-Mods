# FEAT-060: Sim-speed min/avg in profile summary HUD
## Status: Done
## Priority: Low
## Version: v2.5.0
## Summary
Show sim-speed min and average values in the profile summary HUD panel.
## Motivation
Server admins using the profiler need sim-speed context to interpret profiling results. Low sim-speed during profiling means measurements reflect degraded conditions.
## Design
- Added `SimSpeedMin` (ProtoMember 4) and `SimSpeedAvg` (ProtoMember 5) to `MsgProfileSummary`.
- `MethodProfiler.BuildSummaryMessage` populates them from existing `_minSimSpeed` / `_sumSimSpeed` / `_simSpeedSamples` tracking.
- `HudHandler.RenderProfileSummaryFromModel` renders a "Sim-Speed" row below the header with color coding: orange if min < 0.80 or avg < 0.90.
## Files Affected
- `Models/MsgProfileSummary.cs`
- `Profiling/MethodProfiler.cs`
- `Handlers/HudHandler.cs`
## Testing
Build succeeds. Profile summary shows sim-speed when profiling data exists.

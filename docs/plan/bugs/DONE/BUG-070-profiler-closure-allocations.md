# BUG-070: Profiler lambda closures allocated every tick even when profiling is off
## Status: Fixed
## Severity: Medium
## Version: v2.5.0
## Found In: Code review round 5 — Welding.cs, Grinding.cs, Collecting.cs, Inventory.cs, Update.cs, Operations.cs, Scanning.cs
## Description
Every `MethodProfiler.StopAndLog(name, ts, () => string.Format(...))` call allocates a closure object and delegate on the heap, even when profiling is disabled. The `Start()` method returns 0L when disabled, and `StopAndLog` returns early on 0L, but the lambda is already allocated at the call site because it captures local variables. With 100+ BaRs running every 10 ticks, this creates thousands of unnecessary heap objects per second, increasing GC pressure.
## Root Cause
C# compiler generates a display class for lambdas that capture locals. The display class is allocated at the lambda expression, not when the lambda is invoked.
## Fix
Wrapped all `StopAndLog` calls (and their associated local-variable captures) with `if (profilerTs != 0L)` so the lambda expression is never reached when profiling is off. Applied to 15+ call sites across all hot-path files:
- `NanobotSystem.Welding.cs` — ServerTryWelding, Weldable, ServerDoWeld, ServerFindMissingComponents, ServerPickFromWelder (2x)
- `NanobotSystem.Grinding.cs` — ServerTryGrinding, ServerDoGrind
- `NanobotSystem.Collecting.cs` — ServerTryCollectingFloatingTargets
- `NanobotSystem.Inventory.cs` — ServerTryPushInventory, ServerEmptyTransportInventory, PullComponents
- `NanobotSystem.Update.cs` — UpdateBeforeSimulation10_100
- `NanobotSystem.Operations.cs` — ServerTryWeldingGrindingCollecting
- `NanobotSystem.Scanning.cs` — AsyncScanForSources, AsyncAddBlocksOfGrid (2x), AsyncAddBlocksOfBox, AsyncClusterScan, PreSortClusterCandidates, AsyncApplyClusterResults, ApplyClusterResultToSelf

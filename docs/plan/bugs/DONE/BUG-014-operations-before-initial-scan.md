# BUG-014: BaR attempts weld/grind/collect before initial scan completes

## Status: Fixed
## Severity: High
## Version: v2.5.0
## Found In: NanobotSystem.Operations.cs, NanobotSystem.Scanning.cs

## Description

When a BaR block is enabled/placed, it immediately starts `ServerTryWeldingGrindingCollecting()` on the first update tick. However, sources (`_PossibleSources`) and push targets (`_PossiblePushTargets`) are not populated until the first async scan completes (~2-30s after enable). This causes:

1. BaR reports "Missing Components" even when source containers exist, because `_PossibleSources` is empty and `PullComponents()` finds nothing.
2. The custom info panel shows Sources=0 and PushTargets=0 initially, confusing the player.
3. Weld attempts fail unnecessarily, wasting a cycle.

## Root Cause

- `_PossibleSources` and `_PossiblePushTargets` start as empty lists (NanobotSystem.cs:90-91).
- `Init()` does not trigger a scan (NanobotSystem.Init.cs:87-147).
- `ServerTryWeldingGrindingCollecting()` has no gate to wait for the first scan to finish.
- The first scan is triggered by `Mod.RebuildSourcesAndTargetsTimer()` (~2s), which runs `AsyncUpdateSourcesAndTargets()` on a background thread. Sources update on the first scan (because `_LastSourceUpdate` is initialized to `-SourcesUpdateInterval`), but results only appear after the background task completes.

## Fix

Add `_InitialScanCompleted` flag to NanobotSystem. Set it after the first `AsyncUpdateSourcesAndTargets` completes. In `ServerTryWeldingGrindingCollecting()`, when ready but `_InitialScanCompleted=false`:
1. Reset `_LastSourceUpdate` and `_LastTargetsUpdate` to force sources inclusion
2. Trigger immediate `StartAsyncUpdateSourcesAndTargets(true)` — don't wait for the 2s timer
3. Skip all operations this tick (no weld/grind/collect/push)

This makes the BaR self-initiating: it triggers a scan immediately on enable, operations start ~1-2s later once the background scan completes with sources.

Files: NanobotSystem.cs, NanobotSystem.Scanning.cs, NanobotSystem.Operations.cs, NanobotSystem.Init.cs

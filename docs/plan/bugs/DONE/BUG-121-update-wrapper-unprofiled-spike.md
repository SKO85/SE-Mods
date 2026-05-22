# BUG-121: UpdateBeforeSimulation10_100 wrapper has 59 ms / 49 ms spikes in unprofiled code paths

## Status: Fixed
## Severity: High (largest single main-thread spike at filing)
## Version: v2.5.5
## Found In: `NanobotSystem.Update.cs` `UpdateBeforeSimulation10_100`

## Resolution

Profile session `20260429181044-profiling` (with the new sub-timers) **ruled
out all four instrumented wrapper segments**. Across all 41 772 wrapper
samples, the new fields stay at:

- `periodicMs` — typically 0.000, observed max 2.154 ms (one outlier on a
  legitimate 2 s-tick `SetSafeZoneAndShieldStates` + `UpdateCustomInfo`
  refresh; not spike-relevant).
- `resourceSinkMs` — observed max 0.006 ms.
- `settingsSaveMs` — observed max 0.011 ms.
- `msgSendMs` — observed 0.000 throughout.

The wrapper's true spike trail this session (top sample, entity
`136636626974493795` at `2026-04-29 18:10:54Z`):

```
Wrapper                                 35.104 ms  (throttle=fired)
  periodicMs=0.000  resourceSinkMs=0.000  settingsSaveMs=0.001  msgSendMs=0.000
└─ ServerTryWeldingGrindingCollecting   34.753 ms
   └─ ServerTryWelding                  27.035 ms  (weldChecked=2, compChecks=1)
      ├─ ServerFindMissingComponents     9.075 ms  (projected SmallHydrogenThrust)
      ├─ ServerDoWeld                    3.105 ms  (resolveMs=1.54, assignMs=1.54)
      └─ ~14.85 ms unaccounted in the loop body
```

The 35 ms wrapper spike is **inside** `ServerTryWelding`, not in the wrapper
segments. ~15 ms of that 27 ms is currently unprofiled inside the welding loop
itself. Welding-domain spikes did not exist in the previous (`20260429145732`)
session because that scenario was grind-only — only this session captured the
welding spike shape.

A secondary mystery surfaced: **14.181 ms wrapper sample at
`throttle=stagger`**, where the work payload was skipped and all four BUG-121
sub-timers are zero. Suspects narrow to the unprofiled cycle/stagger
calculation block (`Update.cs:90-118`, including `Mod.GetEffectiveStaggerGroupCount`,
`AssignedCluster.Members.Count`, `Mod.GetEffectiveSimSpeed`) or a GC pause hitting
that frame.

## Follow-up tickets opened

- **BUG-122** — instrument the ServerTryWelding loop body and `ServerFindMissingComponents`
  internals to identify the ~15 ms unaccounted cost and the 9-10 ms
  ServerFindMissingComponents projected-block spike.
- **BUG-123** — friendly-damage relation cache. `friendlyMs` outliers (max
  4.813 ms in this session) re-confirm the recommendation from
  `iterative-questing-oasis.md`. Distinct from this ticket, kept as its own
  fix because the cache touches `Mod.cs` rather than the wrapper.

## Description (original)

## Description

Profile session `20260429145732-profiling` (120 s, 58-BaR cluster) reports the
two largest main-thread spikes against `UpdateBeforeSimulation10_100`:

```
ms=59.941 entityId=85454431274025687 delay=13 clusterSize=58 effectiveGroups=3 throttle=workCycle
ms=49.337 entityId=138445524651275346 delay=13 clusterSize=58 effectiveGroups=3 throttle=fired
```

Both spikes are in the wrapper, not in the work payload:

- `throttle=workCycle` on the 59 ms sample means `ServerTryWeldingGrindingCollecting`
  was **skipped** (the BaR already executed in the current work cycle). 59 ms
  was therefore spent entirely outside the weld/grind path.
- `throttle=fired` on the 49 ms sample means the work payload ran. Even so,
  `ServerTryWeldingGrindingCollecting` max in this session is only 10.158 ms,
  leaving ~39 ms unaccounted for in the wrapper.
- Both lines have `delay=13` — the BaR was scheduled 13 ticks late, indicating
  back-pressure in the scheduler.

Initial hypothesis (cluster-apply pile-up via `ApplyClusterResultToSelf` /
`AsyncApplyClusterResults`) was ruled out: both apply methods are dispatched
through `Mod.AddAsyncAction` (`NanobotSystem.Scanning.cs:1485`) and run on the
background pool, so they do not contribute to main-thread spikes directly.

## Suspect surface (all unprofiled inside the wrapper)

`NanobotSystem.Update.cs:52-219` runs the following on the main thread between
the two outer profiler timestamps. Profiled cells are already covered; the
unprofiled cells are the suspect surface for this bug.

| Section | Lines | Profiled today |
|---|---|---|
| `CleanupFriendlyDamage` | 78-84 | yes (`CleanupFriendlyDamage`) |
| Cycle / stagger calculation | 90-143 | no |
| `ServerTryWeldingGrindingCollecting` | 144-148 | yes |
| `_PeriodicExtraChecksLast` 2 s branch (`SetSafeZoneAndShieldStates`, `UpdateCustomInfo`) | 150-159 | partial — `SetSafeZoneAndShieldStates` profiled, `UpdateCustomInfo` profiled (but max 4.258 ms in this session) |
| `_UpdatePowerSinkLast` 2 s branch (`resourceSink.Update()`) | 161-169 | **no** |
| `Settings.TrySave` | 171 | **no** |
| `TryTransmitState` | 173 | yes |
| `NetworkMessagingHandler.MsgBlockSettingsSend` (settings transmit) | 184-188 | **no** |
| Trailing `UpdateCustomInfo(false)` | 190 | profiled |

The only segments that could plausibly hide a 50+ ms spike under `throttle=workCycle`
(where `ServerTryWeldingGrindingCollecting` is skipped) are:

- `resourceSink.Update()` — engine call, no instrumentation.
- `Settings.TrySave` — does an `IsModified` check and may write to mod storage.
- `MsgBlockSettingsSend` — protobuf serialize + transmit.
- `init` / re-init paths — `Init()` at line 61, but `ready=True` rules this out.

## Root Cause

Unknown. Same investigation pattern as BUG-105 (`ServerDoGrind` / `ServerDoWeld`
unprofiled costs): hypotheses are useless without instrumented data, so this
ticket only adds sub-timers — no behavioral change. Follow-up tickets handle
the actual fix once data identifies the dominant segment.

## Fix (this ticket — instrumentation only, no behavioral change)

Add sub-timers in `UpdateBeforeSimulation10_100` around each unprofiled
segment, and append the resulting fields to the existing `UpdateBeforeSimulation10_100`
profile log line:

| Sub-timer       | Wraps |
|---|---|
| `tsPeriodic`    | the 2 s `_PeriodicExtraChecksLast` branch (lines 150-159) |
| `tsResourceSink`| the 2 s `_UpdatePowerSinkLast` branch (lines 161-169) |
| `tsSettingsSave`| `Settings.TrySave(...)` (line 171) |
| `tsMsgSend`     | the `Settings.IsTransmitNeeded` block (lines 184-188) |

`CleanupFriendlyDamage`, `ServerTryWeldingGrindingCollecting`, `TryTransmitState`,
and `UpdateCustomInfo` already log under their own profiler keys — re-wrapping
them would double-count and is not needed.

All sub-timers are gated by `if (profilerTs != 0L)` per project convention
(BUG-098 closure-allocation hygiene) so production overhead is zero when
profiling is off.

## Verification

1. **Build clean** — `dotnet build ... -c Release -v minimal`.
2. **Re-profile** the same 58-BaR scenario at the same scale. Expected outcome:
   - Sample showing `throttle=workCycle ms≥40` will have non-zero values for
     one of the new sub-timers. Whichever dominates is the next ticket's fix
     target.
   - If all four sub-timers are sub-millisecond and the spike is still
     reported by the wrapper, the cost is in the cycle-calculation block
     (lines 90-143) or in untracked exception-handling overhead — re-iterate
     instrumentation around that surface.
3. **No behavioral change** — all changes are `Stopwatch.GetTimestamp()` wraps
   around existing operations.

## Follow-up

Open a fix ticket (BUG-122 or higher) once the dominant segment is known. The
fix shape will depend on what wins: e.g. backoff for `Settings.TrySave` if its
write path spikes, batching for `MsgBlockSettingsSend`, or staggered scheduling
for `resourceSink.Update`.

## See also

- Profile session: `20260429145732-profiling` (2026-04-29, 58 BaRs, 120 s, sim 1.00 avg / 0.85 min).
- Same diagnostic-first pattern as BUG-105 (`ServerDoGrind`/`ServerDoWeld`
  unprofiled costs → BUG-106/107/108 follow-up fixes).
- Plan file: `C:\Users\ICTlogix\.claude\plans\iterative-questing-oasis.md`
  (records the corrected analysis and the ROI-ordered list of follow-ups).

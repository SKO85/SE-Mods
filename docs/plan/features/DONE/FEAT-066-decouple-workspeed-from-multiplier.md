# FEAT-066: Decouple update frequency from weld/grind multipliers (WorkSpeed setting)

## Status: Done
## Priority: High
## Version: v2.5.0

## Summary

Add a new `WorkSpeed` setting (1-10) that independently controls how often BaR operations run, decoupled from the weld/grind multiplier. Fixes BUG-085 (fast-mode housekeeping bugs).

## Motivation

A server admin reported that `WeldingMultiplier=10` felt slow but going to 10.1 caused a massive 10x speed jump. The root cause: the multiplier controlled both per-tick weld amount AND update frequency via a binary cliff at >10.

| Multiplier | Tick interval | Per-tick amount | Effective speed |
|---|---|---|---|
| 10 | every 100 frames (~1.67s) | 10x | ~10x |
| 10.1 | every 10 frames (~0.17s) | 10.1x | ~101x |

There was no middle ground, and the fast path (multiplier > 10) permanently skipped housekeeping (BUG-085).

## Design

- **New setting**: `WorkSpeed` (int, range 1-10, default 1)
  - 1 = operations fire every 100 frames (~1.67s) — same as old Update100 (default)
  - 5 = operations fire every 20 frames — 5x faster than default
  - 10 = operations fire every 10 frames (~0.17s) — same as old Update10 (fastest)
- **Always register `EACH_10TH_FRAME`** — cycle math controls actual work frequency: `cycleDivisor = 100 / WorkSpeed`
- **Multiplier now only controls per-tick amounts** — no longer triggers frequency changes
- **Removed `fast` parameter** — all housekeeping runs unconditionally (self-throttled internally)
- **Backwards compatible**: migration auto-sets `WorkSpeed = 10` when multiplier > 10 and WorkSpeed is at default
- **Chat command**: `/nanobars config set WorkSpeed 5`

### Speed examples for server admins

| WeldingMultiplier | WorkSpeed | Per-tick amount | Updates/sec | Relative speed |
|---|---|---|---|---|
| 10 | 1 (default) | 10x | ~0.6 | 1x (baseline) |
| 10 | 2 | 10x | ~1.2 | 2x |
| 10 | 5 | 10x | ~3 | 5x |
| 10 | 10 | 10x | ~6 | 10x |

## Files Affected

- `Models/SyncModSettingsWelder.cs` — added `WorkSpeed` property (ProtoMember 12, default 1)
- `Models/SyncModSettings.cs` — validation (clamp 1-10), migration (multiplier > 10 → WorkSpeed = 10)
- `Chat/Commands/ConfigCommand.cs` — added `WorkSpeed` IntSetting (1-10)
- `NanobotSystem.Init.cs` — always `EACH_10TH_FRAME`, removed multiplier > 10 conditional
- `NanobotSystem.Update.cs` — removed `UpdateBeforeSimulation100`, removed `fast` param, removed `!fast` guards, cycle uses `100 / WorkSpeed`

## Testing

1. `WorkSpeed=1`: verify identical behavior to old Update100 (default multiplier <=10)
2. `WorkSpeed=10`: verify identical behavior to old Update10 (multiplier > 10) but without BUG-085
3. `WorkSpeed=5`: verify intermediate speed (~5x faster than WorkSpeed=1)
4. Change a block setting with `WorkSpeed=10`, reload world — verify settings persist (BUG-085 fix)
5. `/nanobars config set WorkSpeed 5` and `/nanobars config get WorkSpeed` — verify chat commands work
6. Server with existing `WeldingMultiplier=100` — verify migration auto-sets `WorkSpeed=10`

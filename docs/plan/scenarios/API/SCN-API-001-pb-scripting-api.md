# SCN-API-001: PB scripting API incl. ScriptControlled and ScriptControllFixed lockdown
## Area: API
## Tier: 3
## Preconditions
- World with in-game scripts enabled. Rig: one BaR, a PB, an assembler, 5 damaged armor blocks,
  stocked cargo. Two runs: (a) `Welder.ScriptControllFixed = false`; (b) `= true`.
- PB script exercising what the companion script (`SKO-Nanobot-BuildAndRepair-System-Script/Script.cs`)
  relies on: `BuildAndRepair.Mode/WorkMode/WeldMode`, `PossibleTargets`, `PossibleGrindTargets`,
  `MissingComponents`, `CurrentTarget`, `CurrentPickedTarget` (set), `ScriptControlled` (set),
  `WeldPriorityList` / `GetWeldPriority` / `SetWeldPriority` / `GetWeldEnabled` / `SetWeldEnabled`,
  `ProductionBlock.EnsureQueued`, `Inventory.NeededComponents4Blueprint`, plus `ApplyAction("WeldOnly_On")`.
## Steps
1. Run (a): execute the script; it writes `key=value;` results to its CustomData.
2. Set `ScriptControlled = true` from the PB and pick a target via `CurrentPickedTarget`; observe the
   BaR welds only that block; clear it; observe automatic selection resumes.
3. Run (b): execute the same script.
4. Run the real companion script with an assembler group against SCN-WELD-005's empty-cargo setup.
## Expected outcome
- (a): all reads return data (`PossibleTargets.Count == 5`, `MissingComponents` empty with stock),
  setters work, `EnsureQueued` returns the queued count, `NeededComponents4Blueprint` fills the dict.
- (b): read-only properties still present (BUG-260612.38); `ScriptControlled`, `Set*`, picked-target
  setters and `EnsureQueued` are **absent** — `GetValue` throws / companion helpers return `-3`; the
  per-block `ScriptControlled` flag is forced off (`SyncBlockSettings:806`). `EnsureQueued` never
  mutates queues under lockdown (BUG-260824.10).
- Detection probe on a plain vanilla welder returns defaults, never throws (`Terminal.cs:180` note).
- Companion script queues the aggregate shortfall once, `info-only` mode queues nothing.
## Perf envelope
`GetPossibleWeldTargetsList` etc. copy under lock ≤ 0.5 ms for 1024 targets (PB call cadence Update100).
## Coverage
manual
planned: ingame:S09
## Last verified
never
## Related
BUG-049..051, BUG-260612.36..39, BUG-260824.10, BUG-260824.2, FEAT-069, docs Scripting page (stale note on lockdown — fix)

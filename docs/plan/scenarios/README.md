# Scenarios

Catalogue of every feature and behaviour the BaR has to offer, written so each one can be
verified — by hand, by a unit test, or by the in-game harness (`/nanobars test`, FEAT-260910.2).
This is the spec that tests point back at; "covered" means a scenario here names a real test.

## ID format

- `SCN-<AREA>-NNN`, e.g. `SCN-WELD-003`. **Deliberate exception** to the `<TYPE>-YYMMDD.N` scheme used
  by bugs/features/reviews: those identify dated events, scenarios are a permanent catalogue referenced
  for years. The area prefix makes coverage gaps visible per area in `state.md`.
- `NNN` is monotonic per area, never reused.
- Filename: `<AREA>/<id>-<short-name>.md`, e.g. `WELD/SCN-WELD-003-weld-options-stop-points.md`.
- Areas: `CORE WELD GRIND COLLECT INV PRIO CLUSTER BUDGET ZONE FX TERM API CFG CHAT NET LOC`.

No `TODO/DONE` split — a scenario is permanent; its state is the `Coverage` and `Last verified`
fields. Behaviour removed from the mod → delete the scenario file (and its row in `state.md`).

## Tiers

| Tier | Meaning |
|---|---|
| 1 | Single BaR, single grid, one setting under test |
| 2 | Multiple settings/flags interacting, still one BaR |
| 3 | Multiple BaRs and/or multiple grids (clusters, limits, assignment) |
| 4 | Dedicated server / multiplayer, or performance envelope |

## Template

```
# SCN-<AREA>-NNN: [Title]
## Area: <AREA>
## Tier: 1 | 2 | 3 | 4
## Preconditions
[world type, ModSettings.xml keys, grids, BaR settings, ownership/factions]
## Steps
1. ...
## Expected outcome
[functional: states, targets, inventory, chat/log lines]
## Perf envelope
[profiler methods + thresholds (steadyAvgMs / maxMs), tick cost, sim speed min — or "n/a"]
## Coverage
manual | unit:<TestClass.TestName> | ingame:<ScenarioId>   (one per line; "none" is allowed and honest)
## Last verified
v<version> build <BuildId> <YYYY-MM-DD> — manual | unit | ingame   (or "never")
## Related
BUG-..., FEAT-..., REVIEW-..., GitHub #...
```

## Rules

- A `Coverage` entry must name a test or harness scenario that exists in the repo. Planned automation
  is written as `planned: ingame:S03` until the code lands.
- `Last verified` changes only when the scenario was actually run, with the BuildId it ran on.
- Perf thresholds are starting points; tune them from real `agg=` lines and record the change in the
  scenario's `Related` ticket.
- Every bug ticket's `## Scenario` section names the scenario it adds or updates (or
  `no scenario: <reason>`); every feature ticket's `## Testing` lists the scenario ids it touches.
- Once per release: a `REVIEW-YYMMDD.N-coverage-pass` walks `state.md` — stale `Last verified`
  (older than one minor version), coverage entries naming tests that no longer exist, scenarios with no
  related ticket in a year (delete candidates).
- Keep scenarios minimal and concrete. One behaviour per scenario; matrices go in the Steps table.

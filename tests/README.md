# BaR test framework

Two complementary layers, both pointing back at the scenario catalogue in
`docs/plan/scenarios/` (coverage field `unit:<Test>` / `ingame:<Sxx>`).

| Layer | What it tests | Where it runs | How |
|---|---|---|---|
| **Out-of-game unit tests** (this folder, FEAT-260910.3) | Pure logic: TTL cache, per-tick budgets, settings clamp/migration/XML, config command, auto stagger/budgets, QuickSelect, localization key parity, ProtoBuf tag uniqueness, catalogue honesty | Your dev machine (needs Space Engineers installed) | `dotnet test` |
| **In-game scenario harness** (FEAT-260910.2) | Real behaviour + performance: spawns rigs, welds/grinds, asserts results and profiler/tick-cost numbers | A world loaded with the `-Testing` mod variant | `/nanobars test run <Sxx|all> [xN]` |

## Out-of-game unit tests

```bash
# from the repo root
dotnet test tests/SKO-Nanobot-BuildAndRepair-System.Tests/SKO-Nanobot-BuildAndRepair-System.Tests.csproj -c Debug
```

Visual Studio: open the test `.csproj` (or add it to a solution of your own) — Test Explorer discovers the xUnit tests.

### How it works without the game

- References the compiled mod assembly (ProjectReference) and the Space Engineers assemblies from
  `$(SpaceEngineersBin)` (default `E:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64`;
  override with the `SPACEENGINEERS_BIN` environment variable or `-p:SpaceEngineersBin=...`).
- Every engine static the mod touches is publicly settable, so `ModTestBase` injects fakes:
  `MyAPIGateway.Session` (NSubstitute, controllable play time and frame counter),
  `MyAPIGateway.Utilities/Multiplayer/Parallel = null`, `MyLog.Default`.
- Private members are reached through reflection. That is fine in **test** code; only mod code is
  sandboxed (no reflection / threading).
- Mod statics (`Mod.Settings`, `NanobotSystems`, `GridSystemCount`, handler caches, cached auto-stagger)
  are reset before and after every test; tests run serially (`DisableTestParallelization`).

### Guarantees — it does not change what you have

- **No mod code changes.** No seams, no `InternalsVisibleTo`, nothing in `Data/Scripts` — the files SE
  compiles in-game are untouched.
- **Mod build/deploy unchanged.** `Directory.Build.props` and `nuget.config` live under `tests/` and
  only apply to the test project; the mod csproj and `SKO-SE-Mods.sln` are untouched.
- Building the tests builds the mod through the ProjectReference, which runs the mod's normal
  post-build deploy to `%AppData%\SpaceEngineers\Mods` — identical to any other mod build
  (verified: same deployed file set, no test files deployed).
- `tests/nuget.config` restores from nuget.org only, so machine-wide private feeds don't break restore.

### Limits (by design)

- **No CI.** The Space Engineers assemblies are licensed and not redistributable, so a hosted runner can't
  build or test. A self-hosted runner on a machine with SE installed would work — not set up.
- Anything that needs real entities (grids, blocks, inventories, welding, physics, safe zones,
  network) is out of reach here — that's what the in-game harness is for. Unit tests cover the logic
  around those calls, not the engine calls themselves.
- `NanobotSystem` instances can be created (`new NanobotSystem()`) with a faked welder for logic that
  only reads simple state; deeper per-BaR flows (scan, weld loop) are in-game only.

### Adding a test

1. Derive from `ModTestBase` when the code touches mod statics or `MyAPIGateway`.
2. Name it after the behaviour, and add `unit:<Class>.<Method>` to the scenario's `## Coverage`
   (replacing `planned:`). `CatalogueConsistencyTests` fails if a coverage entry names a test that
   doesn't exist.

## In-game scenario harness

See `docs/plan/features/TODO/FEAT-260910.2-ingame-scenario-harness.md`. In short:

1. Build the mod (`dotnet build ... -c Debug`); the harness deploys only to `SKO-Nanobot-BuildAndRepair-System-Testing`.
2. Load an offline creative world with only the `-Testing` mod.
3. `/nanobars test list`, `/nanobars test run S01`, then `run all`; `run S08 x5` for perf stability
   (S01/02/04-08/10 exist; S03 projector and S09 PB contract are planned).
4. Results: chat + `%AppData%\SpaceEngineers\Storage\SKO-Nanobot-BuildAndRepair-System-Testing_SKO-Nanobot-BuildAndRepair-System\NanobotTest.<session>.log`.

The release variant answers `/nanobars test` with "Test harness not installed".

## What's covered today

| Test class | Scenarios |
|---|---|
| `TtlCacheTests` | SCN-BUDGET-001 |
| `PerTickBudgetTests` | SCN-BUDGET-002, SCN-BUDGET-003, SCN-GRIND-004 |
| `ModResolverTests` | SCN-BUDGET-002, SCN-BUDGET-004 |
| `SyncModSettingsTests` | SCN-CFG-001, SCN-CFG-002 |
| `ConfigCommandTests` | SCN-CHAT-001 |
| `GridSaturationTrackerTests` | SCN-CLUSTER-002 |
| `QuickSelectTests` | SCN-GRIND-003 |
| `LocalizationTests` | SCN-LOC-001 |
| `ProtoContractTests` | SCN-NET-001 (network-sync tag uniqueness) |
| `SkeletonResetHandlerTests` | SCN-WELD-006 (BUG-261023.3 drain re-checks HasDeformation) |
| `TransportInventoryMoveTests` | SCN-COLLECT-001 (BUG-261023.5 verified moves into the transport) |
| `MissingComponentsAggregateTests` | SCN-WELD-005, SCN-BUDGET-006 (BUG-261023.1 time slicing; drives the real `NanobotSystem` method with faked blocks and inventories) |
| `ScenarioRegistryTests`, `CatalogueConsistencyTests` | harness + catalogue integrity |

Next candidates: `TruncateGridAware` fair share (SCN-CLUSTER-003), `SyncBlockSettings.CheckLimits`
policy enforcement (incl. REVIEW-261023.1 priority-lock finding).

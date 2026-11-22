# SCN-CFG-003: Admin restrictions and switches in ModSettings win over block settings

## Area: CFG
## Tier: 2
## Preconditions
- Harness rigs; ModSettings changed for the run only (restored after each scenario). Block settings are set the
  way a player would try; `SettingsChanged()` runs `CheckLimits` like the server does for client messages.
## Steps
1. S46 `Welder.AllowBuildFixed` + `AllowBuildDefault=false`, block tries AllowBuild on.
2. S47 `AllowedGrindJanitorRelations = Enemies`, block tries janitor NoOwnership near a derelict.
3. S48 `AllowedWorkModes = WeldOnly`, block tries GrindOnly. S49 `AllowedSearchModes = Grids`, block tries BoundingBox.
4. S50 `DisableLimitSystemsPerTargetGrid` with `MaxSystemsPerTargetGrid=2`, 4 BaRs. S51 `AssignToSystemEnabled=false`.
   S52 `MaxWeldsPerTick=1`, 5 BaRs. S53 `SafeZoneCheckEnabled=false` under a no-welding zone.
## Expected outcome
- S46: AllowBuild forced off, nothing built. S47: janitor NoOwnership cleared, derelict kept. S48: work mode
  forced to WeldOnly, nothing ground. S49: search mode forced to Grids, separate grid untouched.
- S50: more than 2 BaRs on the target grid at once. S51: no claims, still finished. S52: never more than one weld
  per tick, still finished. S53: the BaR ignores the zone and welds.
## Perf envelope
n/a
## Coverage
ingame:S46
ingame:S47
ingame:S48
ingame:S49
ingame:S50
ingame:S51
ingame:S52
ingame:S53
unit:BlockSettingsLimitsTests
## Last verified
never
## Related
FEAT-260910.2, SCN-CFG-001

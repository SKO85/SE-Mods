# SCN-GRIND-006: Protected grids are not ground (indestructible, immune, damage-handler protection)

## Area: GRIND
## Tier: 2
## Preconditions
- A grid the janitor would grind (derelict), protected by: `DestructibleBlocks = false` (admin toggle / plugin), grid
  `Immune`, or a BeforeDamage handler that zeroes grind damage (protection plugins, mods).
## Steps
1. `test run S73,S74`.
## Expected outcome
- Indestructible / immune: dropped at scan time, never reaches ServerDoGrind.
- Damage-handler protection: the BaR raises BeforeDamage (BUG-261122.15), sees the veto, grinds nothing and skips that
  grid for 30 s (no retry storm: <= 3 grind attempts in 20 s).
## Coverage
unit:GrindDamageVetoTests
ingame:S73
ingame:S74
## Last verified
v2.5.6 build 261122.25 2026-11-22 — in game: S73 (0 grind calls), S74 (1 refused attempt, nothing ground); unit green.
## Related
BUG-261122.15

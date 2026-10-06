# SCN-ZONE-001: Safe zones and DefenseShields block weld / grind / build; characters protected
## Area: ZONE
## Tier: 3
## Preconditions
- Creative world with a Safe Zone block; `SafeZoneCheckEnabled = true`. One BaR (BoundingBox) whose
  area straddles the zone edge; a damaged grid and a projector inside the zone, another damaged grid outside.
- Second run with the DefenseShields mod loaded, `ShieldCheckEnabled = true`, a shielded enemy grid in range.
- A player character standing in the weld beam path.
## Steps
| Zone setting | Expected panel line | Expected behaviour |
|---|---|---|
| Welding disallowed | `SafeZone: Welding disabled!` | inside-zone damaged blocks not welded; outside welded |
| Building disallowed | `SafeZone: Building projections disabled!` | projected blocks inside not built |
| Grinding disallowed | `SafeZone: Grinding disabled!` | inside-zone grind targets skipped |
| Zone removed | lines disappear within the cache TTL | work resumes |
1. Toggle each zone permission; observe panels and the HUD `SafeZones` cache counts.
2. Shields run: observe `Shields Active: Grinding limited to own grid!` and that the shielded grid is untouched.
3. Character in beam: no damage taken from the BaR (DamageHandler).
## Expected outcome
- Permissions come from `SafeZoneHandler.GetActionsAllowedForSystem` (fails **closed** on exception —
  BUG-260502.1); grind protection fails closed and skips the cache write on error (BUG-260502.2).
- Shape-aware geometry (sphere/box, BUG-260612.3), any-zone-prohibits aggregation, execution-time
  protection gates (BUG-260612.13); a grid split by the zone edge splits the cluster (BUG-053).
- `ShieldApi` loads tolerantly and honours `Compromised` (BUG-260612.31/.32); `Mod.Shield` created on
  runtime enable (BUG-260612.5).
- Friendly grinder damage on characters is tracked with timeout, not blocked outright (DamageHandler).
## Perf envelope
`SafeZoneHandler.GetSafeZones` / `IsProtectedFromGrinding` steadyAvgMs ≤ 0.2 (TTL caches, no LINQ — BUG-026).
## Coverage
manual
## Last verified
never
## Related
BUG-006, BUG-010, BUG-020a, BUG-026, BUG-047, BUG-053, BUG-055, BUG-074, BUG-260502.1/.2, BUG-260612.3/.5/.6/.13/.31/.32

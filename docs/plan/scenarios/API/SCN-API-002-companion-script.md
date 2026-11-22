# SCN-API-002: Companion script: groups, assembler queuing, displays, tags, Custom Data, arguments

## Area: API
## Tier: 3
## Preconditions
- The companion PB script (SKO-Nanobot-BuildAndRepair-System-Script/Script.cs) with its shipped group config.
## Steps
1. Unit: `CompanionScriptBehaviourTests` compiles the script and runs it against fake blocks.
2. In game: `test run S71` (real PB, assembler, LCDs, cockpit; needs in-game scripts enabled).
## Expected outcome
- BaRs found by group, by name, or auto-detected by subtype (vanilla welders ignored); assemblers by group or name.
- Missing components queued (largest need across BaRs, plus what already sits in assembler outputs).
- `info-only` stops queuing and is kept in Storage (recompile / reload); `queue` resumes; other arguments change nothing.
- Displays by name, `Name[n]` cockpit surfaces, `[BaR:group@surfaces]` tags (index or name, case-insensitive; invalid
  tags ignored), Custom Data `@BaR` / `@BaR@n` overrides (Kinds + aliases, MaxLines, SwitchTime, FontSize / auto),
  template seeded into empty Custom Data, a tagged surface wins over the same surface by name.
## Coverage
unit:CompanionScriptBehaviourTests
ingame:S71
## Last verified
v2.5.6 build 261122.18 2026-11-22 — unit (34 tests green); in game (S71): queued in 3.7 s, LCD / tag / Cockpit[0] written, info-only kept over recompile, queue resumed at once.
## Related
SCN-API-001, BUG-261122.13

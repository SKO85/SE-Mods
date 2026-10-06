# SCN-CHAT-001: /nanobars config set/get round trip and bounds rejection
## Area: CHAT
## Tier: 1
## Preconditions
- Admin player (PromoteLevel ≥ Admin). Local game or listen server.
## Steps
| # | Command | Expect |
|---|---|---|
| 1 | `/nanobars config set MaxGrindsPerTick 20` | ok; `config get MaxGrindsPerTick` → 20 |
| 2 | `/nanobars config set MaxGrindsPerTick 101` | error, value still 20 |
| 3 | `/nanobars config set WeldingMultiplier 2.5` | ok (invariant culture — also on a de-DE OS) |
| 4 | `/nanobars config set DebugMode yes` | error (bool parse) |
| 5 | `/nanobars config set NoSuchSetting 1` | "Unknown setting" error |
| 6 | `/nanobars config set StaggerGroupCount 0` | ok, `config list` shows `0 (auto)` |
| 7 | `/nanobars config save` then `config reload` | values persist; reload reports the source (world) |
| 8 | `/nanobars config reset` | all defaults; `config list` matches CLAUDE.md defaults |
| 9 | As a non-admin: `/nanobars config list` | "Command requires admin permissions"; `/nanobars version` still works |
## Expected outcome
- Every `set` takes effect immediately (`Mod.SettingsChanged()` — assignments cleared, loop-exhausted
  flags reset) and is broadcast to clients.
- Range rejections leave the previous value untouched and return `ChatCommandResult.Error`.
## Perf envelope
n/a
## Coverage
planned: unit:ConfigCommandTests.SetGetRoundTrip_AndBoundsRejection
## Last verified
never
## Related
FEAT-260910.3, BUG-032 (no reflection), BUG-076, BUG-260610.20 (invariant parse), BUG-260502.3 (admin checks)

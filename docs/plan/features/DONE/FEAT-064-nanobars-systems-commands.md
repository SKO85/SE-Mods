# FEAT-064: Admin commands for remote BaR system management
## Status: Done
## Priority: Medium
## Version: v2.5.0
## Description
Add a set of admin-only chat commands under `/nanobars systems` for remote management of BaR blocks on the server.

### Commands
1. **`/nanobars systems list`** — List all BaR blocks on the server (owner, grid name, enabled/disabled status, position).
2. **`/nanobars systems list --owner <playerName>`** — List BaR blocks filtered by owner (case-insensitive, partial match).
3. **`/nanobars systems count`** — Show overview of BaR system count per player and per faction.
3. **`/nanobars systems enable all`** — Enable all BaR blocks on the server.
4. **`/nanobars systems disable all`** — Disable all BaR blocks on the server.
5. **`/nanobars systems enable --grid <grid-name>`** — Enable all BaR blocks on a specific grid.
6. **`/nanobars systems disable --grid <grid-name>`** — Disable all BaR blocks on a specific grid.
7. **`/nanobars systems enable --owner <playerName>`** — Enable all BaR blocks owned by a specific player.
8. **`/nanobars systems disable --owner <playerName>`** — Disable all BaR blocks owned by a specific player.

### Requirements
- All commands are admin-only.
- On dedicated servers, commands are forwarded to server and responses sent back to client (same as existing chat command pattern).
- Grid name matching should be case-insensitive and support partial matches.
- Player name matching should be case-insensitive.
- List output should be paginated or capped to avoid chat overflow.

### Implementation Notes
- Add a `SystemsCommand` class following the existing `ConfigCommand` / `ProfileCommand` pattern.
- Register under the existing `/nanobars` handler in `ChatHandler.cs`.
- Use `Mod.NanobotSystems` to iterate all BaR blocks.
- Enable/disable via `block.Enabled = true/false` on the welder.
## Files
- New: `Chat/Commands/SystemsCommand.cs`
- Modified: `Chat/ChatHandler.cs` (register new command)

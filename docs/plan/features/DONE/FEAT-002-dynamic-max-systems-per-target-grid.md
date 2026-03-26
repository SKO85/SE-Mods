# FEAT-002: Dynamic MaxSystemsPerTargetGrid Default Based on Game Type
## Status: Done
## Priority: Medium
## Version: 2.5.0
## Summary
Change the default value of `MaxSystemsPerTargetGrid` from a fixed value to a game-type-aware default: **20** for local/single-player games, **10** for multiplayer games.
## Motivation
Local games have no network overhead and typically fewer performance constraints, so players can benefit from more BaR systems working on the same grid simultaneously. Multiplayer games need a lower default to balance server load across connected players. The current fixed default of 5 is too conservative for both scenarios.
## Design
- On settings load (when no user-provided config override exists for this field), detect game type:
  - **Local game** (`!MyAPIGateway.Multiplayer.MultiplayerActive`): default to **20**
  - **Multiplayer game** (`MyAPIGateway.Multiplayer.MultiplayerActive`): default to **10**
- If the user has explicitly set `MaxSystemsPerTargetGrid` in `ModSettings.xml`, that value takes precedence over the dynamic default.
- The constructor default in `SyncModSettings` should remain a safe fallback (e.g., 10).
- The dynamic default logic should run in `Mod.Init()` or `SyncModSettings.Load()` after the API is available.
## Files Affected
- `Models/SyncModSettings.cs` (constructor default, possibly Load() logic)
- `Mod.cs` (apply dynamic default after settings load)
## Testing
1. Start a local/single-player world without `ModSettings.xml` — verify `MaxSystemsPerTargetGrid` is 20.
2. Start a multiplayer/dedicated server world without `ModSettings.xml` — verify `MaxSystemsPerTargetGrid` is 10.
3. Set `MaxSystemsPerTargetGrid=10` in `ModSettings.xml` — verify the explicit value (10) is used regardless of game type.
4. Verify BaR systems respect the new limits (place >15 BaRs targeting the same grid in local, confirm all work).

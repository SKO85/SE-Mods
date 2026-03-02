# Release Notes – v2.0.0 – v2.1.1 (Major Update)

- Release date: September 2025
- Notes: Major rewrite based on the original mod's codebase.

---

## New Features

### Safe-Zone Support
The Build and Repair system now respects Safe-Zone rules for welding, grinding, and projection building. Checks are enabled by default and can be turned off in `ModSettings.xml`.

### Shield Mod Support
Checks for the Shields mod are now enabled by default. Can be disabled in `ModSettings.xml`.

### Reputation Loss on Grinding
Grinding grids belonging to other factions or NPCs now correctly causes a reputation penalty, matching the behaviour of manual grinding. This can be disabled in `ModSettings.xml`.

### `DeleteBotsWhenDead` Config Option (server setting)
Controls whether bots (Wolves, Spiders) are deleted after their inventory is emptied. Default: `true`.

### Control Panel Warnings
- A notification is shown in the block's info panel when inside a Safe-Zone or when a Safe-Zone option is disabled.
- A warning is shown when the mod has not been fully downloaded on a server, helping diagnose issues caused by partial Steam mod downloads on Dedicated Servers.

---

## Bug Fixes

- Fixed Safe-Zone grinding checks: enemies can no longer be ground inside Safe-Zones the player or their faction does not own. Grinding within your own Safe-Zone is allowed when the relevant Safe-Zone option is enabled.

---

## Performance

- Added caching for multiple heavy API calls to improve overall performance and reduce simulation speed impact in multiplayer. Safe-Zone detection for grids may be delayed by up to 10–15 seconds.

---

## Known Limitations / Temporary Removals

- **Auto-Power-Off** has been temporarily removed. A rework is planned that will let players opt out of this behaviour.
- **Moving grid tracking** has been temporarily removed.

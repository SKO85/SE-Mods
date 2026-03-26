# FEAT-049: TextHudAPI debug/profiling HUD overlay
## Status: Done
## Priority: Medium
## Version: v2.5.0

## Description

Added a client-side HUD overlay using TextHudAPI (BuildInfo mod) that shows comprehensive BaR system stats. Visible only to admins when DebugMode is enabled or profiling is active. Soft dependency — if BuildInfo is not installed, nothing shows.

### HUD sections:
- **BaR Systems:** Active/total with % working, activity breakdown (weld/grind/collect/idle/off), transporting, inventory full, component starved, safe zone blocked
- **Work Modes:** Per-mode count with percentages (Weld>Grind, Grind>Weld, etc.), search mode (Grids/BBox)
- **Targets:** Unique weld/grind/float targets (per-cluster, not double-counted)
- **Performance:** Clusters, stagger groups, grind budget usage with CAPPED indicator, sim speed, background tasks, scan age, empty grid skip
- **Assignments & Limits:** Block assignment count, max sys/grid
- **Caches:** SafeZone, ownership, block priority entry counts
- **Profiling:** Recording status, elapsed/total time, min duration (only when profiling active)

### Implementation:
- `Mods/HudAPIv2.cs` — Patched TextHudAPI v2 library (standalone, no BuildInfo dependency at compile time)
- `Handlers/HudHandler.cs` — HUD manager with two-column layout (labels + values)
- Admin-only via PromoteLevel check
- Updates every 2 seconds, auto-hides when no BaR systems exist
- Players online shown in header

# BUG-093: Empty `AllowedGrindJanitorRelations` silently disables grinding on every BaR

## Status: Fixed
## Severity: High
## Version: v2.5.3
## Found In: `SyncModSettings.cs` — `Load()` validation + `AdjustSettings()` version migration, paired with `SyncBlockSettings.cs:772` janitor mask

## Description

A mod-wide `ModSettings.xml` with `<AllowedGrindJanitorRelations></AllowedGrindJanitorRelations>` (empty flag enum = `0`) silently breaks grinding for every BaR in the world. The BaR appears to scan normally but never finds any grind targets unless the user has also painted blocks with the configured grind color.

Discovered after a player shared their world's `ModSettings.xml` reporting "BaR not grinding at all" and the same file reproduced the issue on a second machine.

## Steps to Reproduce

1. Edit `ModSettings.xml` in a world's Storage folder:
   ```xml
   <AllowedGrindJanitorRelations></AllowedGrindJanitorRelations>
   <Version>7</Version>
   ```
2. Load the world. Place a BaR.
3. Enable the janitor feature in the BaR terminal (e.g. "NoOwnership").
4. Observe: the terminal shows the janitor relation as selected, but the BaR never grinds any blocks even with the janitor toggles on.

## Root Cause

Two interacting problems:

### 1. Every block's janitor setting is AND-masked against the mod-wide value

`Models/SyncBlockSettings.cs:772`:

```csharp
UseGrindJanitorOn &= Mod.Settings.Welder.AllowedGrindJanitorRelations;
```

This line runs when per-block settings are assigned or reloaded. With `AllowedGrindJanitorRelations == 0`, the mask unconditionally forces every BaR's `UseGrindJanitorOn` to `0`, regardless of what the player chose in the terminal. The terminal UI still shows the janitor flag as "on" because the toggle reads the pre-mask field for display, but the scan code reads the post-mask value and finds `0` → no janitor-based grind candidates.

Combined with the `(useGrindColor || autoGrindRelation != 0)` scan-enable check in `NanobotSystem.Scanning.cs`, grinding only continues if `useGrindColor` is true AND blocks are actually painted the configured grind color. For anyone relying on janitor-only grinding, nothing gets ground.

### 2. The migration that would repopulate the default is version-gated

`Models/SyncModSettings.cs:352`:

```csharp
if (settings.Version <= 5 && settings.Welder.AllowedGrindJanitorRelations == 0)
    settings.Welder.AllowedGrindJanitorRelations = AutoGrindRelation.NoOwnership | AutoGrindRelation.Enemies | AutoGrindRelation.Neutral;
```

The guard `Version <= 5` means that once a config file has been saved with `Version = 6` or `Version = 7` (current), this migration never runs again. If the file is hand-edited to clear the field, or if `Version` is bumped by some other save pathway while `AllowedGrindJanitorRelations` ends up empty, the migration will not recover.

The `SyncModSettingsWelder` constructor does set the default:

```csharp
AllowedGrindJanitorRelations = AutoGrindRelation.NoOwnership | AutoGrindRelation.Enemies | AutoGrindRelation.Neutral;
```

But the XML deserializer overwrites the constructor value with whatever the XML file says — including an explicit empty element — so the constructor default is bypassed on load.

## Fix

Apply the default unconditionally in `SyncModSettings.Load()` validation whenever `AllowedGrindJanitorRelations == 0`, decoupled from the version migration. There's no legitimate "no janitor relations allowed at all" configuration — if the admin wants to disable the feature, they set `UseGrindJanitorFixed=true` with `UseGrindJanitorDefault=None`, not an empty allowed-relations list.

### Code change (`Models/SyncModSettings.cs`, validation block ~line 290)

Added after the `WorkSpeed` clamp:

```csharp
// BUG-093: An empty AllowedGrindJanitorRelations silently breaks grinding
// on every BaR in the world — SyncBlockSettings masks each block's
// UseGrindJanitorOn against this value, so 0 here clobbers the per-block
// janitor setting. The version-gated migration (Version <= 5) doesn't
// run on files already at a newer version, so a manually-edited or
// cleared XML would stay broken forever. Apply the default unconditionally
// whenever the value is empty — there's no legitimate "all janitor
// relations disabled" state; users should disable the feature via
// UseGrindJanitorFixed instead.
if (settings.Welder.AllowedGrindJanitorRelations == 0)
{
    settings.Welder.AllowedGrindJanitorRelations = AutoGrindRelation.NoOwnership | AutoGrindRelation.Enemies | AutoGrindRelation.Neutral;
    adjusted = true;
}
```

On load, any file that has (or gains) an empty `AllowedGrindJanitorRelations` is quietly fixed up to the same default as the constructor, `adjusted=true` triggers a re-save, and the mask on `SyncBlockSettings.cs:772` now passes the block's janitor flags through unchanged.

## Risks / Notes

- **Behavior change is scoped** to admins who had somehow persisted an empty `AllowedGrindJanitorRelations`. The broken state was already a footgun with no upside, so there's no legitimate config being overwritten.
- **Version-gated migration (line 352) is now redundant** for this field. Leaving it in place for clarity — the unconditional check supersedes it for any future corruption, while the gated one documents the original v5 → v6 transition.
- **No player workaround needed after the fix** — existing worlds with the broken config will auto-heal on first load after update, because `adjusted=true` triggers a re-save with the corrected value.

## Verification

1. Build: `dotnet build SKO-Nanobot-BuildAndRepair-System/Data/Scripts/SKO-Nanobot-BuildAndRepair-System/SKO-Nanobot-BuildAndRepair-System.csproj -c Release -v minimal` — clean under C# 6. ✓
2. Reproduce the original break: copy a `ModSettings.xml` with `<AllowedGrindJanitorRelations></AllowedGrindJanitorRelations>` and `<Version>7</Version>` into a test world's Storage folder. Load the world with the pre-fix build. Confirm BaR doesn't grind.
3. Replace the mod DLL with the post-fix build. Reload the world. Confirm:
   - Log line "NanobotBuildAndRepairSystemSettings: Settings …" shows `AllowedGrindJanitorRelations` populated with `NoOwnership, Enemies, Neutral`.
   - `ModSettings.xml` on disk has been rewritten with the populated field (re-save triggered by `adjusted=true`).
   - BaR finds grind candidates and actively grinds when janitor NoOwnership is enabled in terminal.
4. Regression check: a config with a **legitimate subset** of relations (e.g. `<AllowedGrindJanitorRelations>Enemies</AllowedGrindJanitorRelations>`) should pass through unchanged — the fix only triggers when the value is `0`.

## See also

- The `AllowedGrindJanitorRelations` mask line: `SyncBlockSettings.cs:772`.
- The version-gated migration this fix decouples from: `SyncModSettings.cs:352`.
- Originally reported from player config at `C:\Users\ICTlogix\AppData\Roaming\SpaceEngineers\Saves\76561198001777579\Empty World 2026-03-13 20-18\Storage\SKO-Nanobot-BuildAndRepair-System-Testing_SKO-Nanobot-BuildAndRepair-System\ModSettings.xml`.

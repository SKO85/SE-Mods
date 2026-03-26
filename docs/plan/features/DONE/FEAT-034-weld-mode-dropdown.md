# FEAT-034: Weld Mode Dropdown (Full / Functional / Skeleton)

## Status: Done
## Priority: Medium
## Version: v2.5.0

## Summary

Replace the "Weld to functional only" on/off checkbox with a three-option dropdown: **Weld to Full** (default), **Weld to Functional**, and **Weld to Skeleton**.

## Motivation

The current binary checkbox (weld to functional or not) is too limited. Players building large bases from projections want to quickly place all blocks without fully welding them — this is the "skeleton" use case. Having three distinct modes gives players precise control over how much the BaR system welds:

- **Weld Full** (default): Weld blocks to 100% integrity. Current default behavior.
- **Weld Functional**: Weld only to the functional threshold (`CriticalIntegrityRatio`). Blocks work but aren't fully armored.
- **Weld Skeleton**: Only place projected blocks (build the first component). Never repair or continue welding existing blocks. Fastest way to lay out a structure from a projection.

## Design

### 1. Enum Change

Replace the `[Flags]` enum with a mutually-exclusive enum:

```csharp
// OLD (Terminal.cs):
[Flags]
public enum AutoWeldOptions
{
    FunctionalOnly = 0x0001
}

// NEW (NanobotTerminal.cs — file was renamed on v2.5.1):
public enum AutoWeldOptions
{
    WeldFull = 0,        // 100% integrity (default)
    WeldFunctional = 1,  // CriticalIntegrityRatio threshold
    WeldSkeleton = 2     // Place projected blocks only
}
```

**Note:** `WeldFull = 0` keeps the default behavior for existing saves (protobuf default for int is 0). The `[Flags]` attribute is removed since modes are mutually exclusive.

### 2. Terminal UI

- **Remove**: `CreateWeldOptionFunctionalOnly()` from `OnOffSwitches.cs` (the old checkbox).
- **Add**: `CreateWeldMode()` in `ComboBoxes.cs` — a combobox with three items.
- **Register**: In `NanobotTerminal.cs`, replace the checkbox registration with the combobox, placed in the weld options section.

### 3. Welding Logic

All changes are in existing methods — no new methods needed.

#### `IsWeldIntegrityReached()` (NanobotSystem.Operations.cs)

```
- WeldSkeleton → always return true (treat as "already sufficient")
- WeldFull → return target.IsFullIntegrity (fast path, no math)
- WeldFunctional → compute required integrity and compare
```

#### `ServerDoWeld()` — Projector Build (NanobotSystem.Operations.cs)

The `proj.Build()` boolean parameter (`requestInstant`) should be `true` only for `WeldFull`:
```csharp
proj.Build(target, ..., Settings.WeldOptions == AutoWeldOptions.WeldFull, ...);
```

#### `ServerDoWeld()` — IncreaseMountLevel capping (NanobotSystem.Operations.cs)

For non-WeldFull modes, cap the weld amount to not exceed the required integrity:
```
if (WeldSkeleton && just created) → skip welding entirely
if (not WeldFull) → cap weldAmount to (requiredIntegrity - current integrity)
if (remaining <= 0) → skip welding
```

#### `NeedRepair()` extension (Utils/Utils.cs)

Change signature from `bool functionalOnly` to `AutoWeldOptions weldMode`:
- `WeldSkeleton` → return `false` (existing blocks never need repair)
- Otherwise → use `GetRequiredIntegrity(weldMode)` for integrity check

#### `GetRequiredIntegrity()` extension (Utils/Utils.cs)

Change signature from `bool isFunctionalOnly` to `AutoWeldOptions weldMode`. Use switch:
- `WeldFunctional` → `MaxIntegrity * CriticalIntegrityRatio + 1` (capped at MaxIntegrity)
- Default (WeldFull) → `MaxIntegrity`
- `WeldSkeleton` never reaches this path (early-exited in NeedRepair/IsWeldIntegrityReached)

### 4. Inventory / Component Sourcing (NanobotSystem.Inventory.cs)

When in skeleton mode and a projected block has been placed:
- Skip fetching remaining components (only the first creation component was needed)
- Clear `_TempMissingComponents` so the generic pick loop doesn't double-fetch

For functional mode, use `IntegrityLevel.Functional` (only critical components). For full mode, use `IntegrityLevel.Complete`.

### 5. Localization

Replace old keys:
```
WeldToFuncOnly        → (remove)
WeldToFuncOnly_Tooltip → (remove)
```

Add new keys:
```
WeldMode              → "Weld mode"
WeldMode_Tooltip      → "Select how far the nanobots weld blocks."
WeldMode_Full         → "Weld to full"
WeldMode_Functional   → "Weld to functional only"
WeldMode_Skeleton     → "Weld to skeleton"
```

Update all language files: English, German, Polish, Russian.

### 6. Script Companion (Script.cs)

Add `WeldMode` property to `RepairSystemHandler` for programmatic access via `GetValue<long>("BuildAndRepair.WeldMode")` / `SetValue<long>()`. Display in status panel.

### 7. Network Compatibility

`SyncBlockSettings.WeldOptions` already uses `[ProtoMember(49)]` with type `AutoWeldOptions`. Since `WeldFull = 0` is the default, existing saves where this field was never set will default correctly. Saves that had `FunctionalOnly = 1` will deserialize as `WeldFunctional = 1` — same numeric value, correct behavior.

## Source Reference

The `fix/v2.5.1` branch contains a complete implementation of this feature (commit `da50e41`, 2026-03-04). Code quality assessment:

**Good to pull directly:**
- Enum definition (`NanobotTerminal.cs`) — clean, well-documented
- `CreateWeldMode()` in `ComboBoxes.cs` — follows existing pattern for `CreateWorkMode()`
- Localization strings (all 4 languages)
- `NeedRepair()` / `GetRequiredIntegrity()` changes in `Utils.cs` — clean switch-based logic
- `IsWeldIntegrityReached()` — clear mode-based branching, safe catch block (returns `true`)
- Inventory skeleton-mode skip logic

**Needs adaptation:**
- `NanobotSystem.Inventory.cs` — entire file is new on `fix/v2.5.1` (extracted from Operations.cs). The weld mode logic is good, but the file structure differs from current branch. Cherry-pick the weld-mode-specific changes, not the whole file.
- `OnOffSwitches.cs` — the v2.5.1 diff also includes unrelated changes (sort toggle deselect fix, priority label changes, `DisableParticleEffects` toggle). Only pull the `CreateWeldOptionFunctionalOnly` removal.
- `NanobotSystem.Operations.cs` — v2.5.1 has many structural changes. Only pull the `IsWeldIntegrityReached()`, `ServerDoWeld()` weld-mode logic, and `ShouldWeldTarget()` signature changes.

**Not needed:**
- `Script.cs` — new companion file, can be added separately
- Version bump, `BlockPriorityHandling` changes, and other unrelated diffs

## Files Affected

| File | Change |
|------|--------|
| `NanobotTerminal.cs` (was `Terminal.cs`) | Replace `AutoWeldOptions` flags enum with sequential enum |
| `Terminal/ComboBoxes.cs` | Add `CreateWeldMode()` method |
| `Terminal/OnOffSwitches.cs` | Remove `CreateWeldOptionFunctionalOnly()` |
| `NanobotTerminal.cs` | Replace checkbox registration with combobox |
| `NanobotSystem.Operations.cs` | Update `IsWeldIntegrityReached()`, `ShouldWeldTarget()`, `ServerDoWeld()` |
| `NanobotSystem.Inventory.cs` | Skeleton-mode component skip; functional-mode integrity level |
| `Utils/Utils.cs` | Update `NeedRepair()`, `GetRequiredIntegrity()` signatures and logic |
| `Models/SyncBlockSettings.cs` | No structural change needed (type already `AutoWeldOptions`) |
| `Localization/Texts.cs` | Replace old string IDs with new ones |
| `Localization/TextsEnglish.cs` | New English strings |
| `Localization/TextsGerman.cs` | New German strings |
| `Localization/TextsPolish.cs` | New Polish strings |
| `Localization/TextsRussian.cs` | New Russian strings |

## Testing

1. **Default behavior**: Place BaR with default settings. Verify dropdown shows "Weld to full" and blocks are welded to 100%.
2. **Functional mode**: Set dropdown to "Weld to functional only". Verify blocks stop welding at functional integrity (lights turn on, doors work, etc. but armor isn't full).
3. **Skeleton mode**: Set dropdown to "Weld to skeleton". Enable a projector with a blueprint. Verify BaR places blocks (first component) but does not continue welding them. Verify existing damaged blocks are ignored.
4. **Mode switching**: Switch between modes mid-operation. Verify BaR adjusts behavior immediately.
5. **Save/load**: Save world with each mode set. Reload. Verify mode persists.
6. **Multiplayer**: On DS, verify mode syncs correctly between server and client terminal UI.
7. **Script companion**: If Script.cs is included, verify `WeldMode` property reads/writes correctly from PB.

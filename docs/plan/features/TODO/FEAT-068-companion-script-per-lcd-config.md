# FEAT-068: Companion script — per-LCD config via name tag and Custom Data

## Status: Done
## Priority: Medium
## Version: v2.5.2

## Summary

New ways to configure status displays in the companion PB script without editing the script config for every panel:

1. **Name tag auto-discovery** — put `[BaR:<group>]` anywhere in an LCD's CustomName and it auto-attaches to that BaR group.
2. **Multi-surface name tag** — `[BaR:<group>@0,1,2]` attaches several surfaces of the same block (e.g. multiple cockpit screens) at once, each as an independent entry.
3. **Per-LCD Custom Data overrides** — an `@BaR ... @/BaR` block in an LCD's Custom Data overrides the script-level `DisplayDefinition` for that panel.
4. **Scoped per-surface Custom Data** — `@BaR@<surfaceIndex> ... @/BaR` blocks let a single cockpit's Custom Data drive different pages on different surfaces of that cockpit.
5. **Font control per panel** — new `FontSize` key in the `@BaR` block accepts an explicit size or `auto`/`fit` to measure the rendered text and pick the largest size that fits the surface.
6. **Forced Monospace font** — panels the script writes to are switched to `Monospace` so the existing column-aligned status lines render correctly (they drift in proportional fonts).
7. **Auto-seeded default Custom Data** — when a tagged block's Custom Data is empty, the script writes a commented default `@BaR` template into it so users can discover and edit the available knobs from the terminal UI. Non-empty Custom Data is never touched.
8. **Auto-queuing line on the full Status page** — shows whether the script is actively queuing missing components into assemblers (`Enabled (N assemblers)`, `Disabled (info-only)`, or `Disabled (no assemblers)`).
9. **Faster reinit (120 s → 30 s)** — the periodic full rescan that picks up renames, new LCDs, Custom Data edits and group changes now runs every 30 seconds instead of every 2 minutes, so terminal-only edits take effect quickly without a PB recompile.
10. **Script size reduction** — dead / redundant comments (Complex Example block, commented-out grind example, verbose XML doc comments on self-describing methods) were stripped so the single-file PB script fits well under the Workshop size limit. The file shrank from 2634 to 2075 lines (~21%) with no behavioural changes.

All features are fully backward compatible: panels that are not tagged and have no Custom Data block continue to behave as before (except for the forced Monospace font, which is a deliberate fix for the long-standing column-drift in the existing status layout).

## Motivation

Today all displays have to be hard-coded in the script's `BuildAndRepairSystemQueuingGroups` array. This has two ergonomic problems:

- **Listing every LCD by name** gets tedious on ships with many status panels, and every new LCD requires a PB recompile + paste of the updated script.
- **Fine-tuning what each panel shows** (e.g. a dedicated "missing items" LCD next to a dedicated "grind targets" LCD) forces the user to duplicate the enclosing `DisplayDefinition` for each variation.

The goal is to let players configure the most common cases purely from the terminal UI (rename the LCD, edit its Custom Data) without touching the script.

## Design

### Precedence

For any given panel, the effective configuration is layered:

```
script config (group's first DisplayDefinition, or built-in default)
        │
        ▼
Custom Data overrides (any subset of the keys)
        │
        ▼
effective DisplayDefinition used at render time
```

Group assignment is resolved once, via this precedence:

1. If the block's CustomName contains a `[BaR:<group>]` tag, it is attached to that group (the tag wins).
2. Otherwise, if the block's name is listed in a `DisplayNames` array, it is attached to that group (legacy path).
3. If both are present, the tag wins and the explicit `DisplayNames` entry is silently skipped so panels are not duplicated.

### Name tag format

- Tag: `[BaR:<group>]`, `[BaR:<group>@<surfaceIndex>]`, or `[BaR:<group>@<a>,<b>,<c>]`.
- Tag is found anywhere in the block's CustomName via a case-insensitive `IndexOf("[BaR:", ...)` search.
- `<group>` is either the group's `Name` (case-insensitive) or a 1-based index into `BuildAndRepairSystemQueuingGroups`.
- `@<surfaceIndex>` (optional) selects a surface on `IMyTextSurfaceProvider` blocks (cockpits, PBs, fighter cockpits, etc.). Omitted = surface 0.
- Multiple surface indices can be comma- or semicolon-separated. Each becomes its own `TaggedPanel` entry (own rotation state, own Custom Data scope). Duplicates within the list are ignored.
- Malformed tags (missing `]`, empty group id, non-numeric surface index, any negative index, group id that doesn't match any group, out-of-range surface index) are ignored and logged via `InitializationResultMessage`.

### Custom Data format

Two block flavours:

1. **Unscoped** — `@BaR ... @/BaR`. Applies to every attached surface on the block. On a plain LCD this is the per-LCD override. On a cockpit it is a cockpit-wide base.
2. **Scoped** — `@BaR@<surfaceIndex> ... @/BaR`. Applies only to the specific surface index on the block.

Example with both:

```
@BaR
MaxLines=15
@/BaR

@BaR@0
Kinds=Status
SwitchTime=0
@/BaR

@BaR@1
Kinds=WeldTargets,MissingItems
@/BaR
```

**Layering for each attached surface:**

```
script DisplayDefinition
    → @BaR (unscoped)                — cockpit-wide base
    → @BaR@<matching surfaceIdx>     — per-surface override
    = effective DisplayDefinition
```

Each layer is a full merge: keys present overwrite the layer above, keys omitted fall through.

Keys accepted in any block:

- `Kinds` (alias `DisplayKinds`) — comma- or semicolon-separated list of `DisplayKind` values, case-insensitive. Short aliases accepted: `short`, `weld`, `grind`, `collect`, `missing`, `weldpriority`, `grindpriority`.
- `MaxLines` (alias `DisplayMaxLines`) — positive integer.
- `SwitchTime` (alias `DisplaySwitchTime`) — seconds, double. `0` = no rotation.
- `FontSize` — positive float (explicit size) or `auto` / `fit` (measure once and pick the largest scale that fits both dimensions of the surface). Omit to leave the font size alone.
- Blank lines and lines starting with `#` or `//` inside a block are ignored.
- Any content outside the `@BaR` … `@/BaR` blocks is untouched so users can keep other Custom Data alongside them.

Note: `EntityHandler.GetCustomData` (a generic `IndexOf`/`LastIndexOf` helper used by earlier iterations) cannot correctly parse multiple blocks in one Custom Data string. A dedicated scanner `ParseAllBaRBlocks` walks the Custom Data sequentially, reading one `@BaR...@/BaR` pair at a time and returning `(surfaceIdx, body)` pairs where `surfaceIdx == -1` represents the unscoped block.

### Rendering changes

Each panel now becomes its own `DisplayEntry` with independent rotation state. A `DisplayEntry` holds:

- `EffectiveSettings` — the merged `DisplayDefinition` (base + Custom Data overrides).
- `Display` — a `StatusAndLogDisplay` bound to exactly one surface.
- `DisplayKindIdx` / `NextSwitchTime` — rotation state.

`BuildAndRepairSystemQueuingGroupData.RefreshDisplay` iterates the `DisplayEntries` list instead of the old shared `StatusDisplays` + per-definition rotation arrays.

### Font and alignment

`StatusAndLogDisplay.SetPanelText` now forces `panel.Font = "Monospace"` before writing so the column-aligned status lines actually line up (the default `Debug` font is proportional). A per-panel `EnsureFontApplied` helper runs once per panel and applies:

- `OverrideFontSize > 0` — sets `panel.FontSize` and marks the panel done.
- `AutoFitFontSize == true` — waits for the first update with real content, then measures the text via `panel.MeasureStringInPixels` (at scale 1), compares against `panel.SurfaceSize` minus `TextPadding`, applies `min(widthScale, heightScale) * 0.98` clamped to `[0.1, 10]`. Marked done on success.
- Neither — marked done with no change.

Tracking is kept in a `HashSet<IMyTextSurface>` on the `StatusAndLogDisplay`, so the work happens exactly once per init cycle and every subsequent update is a zero-overhead hash lookup.

### Auto-queuing state on the Status page

`BuildAndRepairSystemQueuingGroupData` gained a per-tick `InfoOnly` flag (set from `BuildAndRepairAutoQueuing._InfoOnly` in `RefreshDisplays`). `DisplayStatus` renders a new line:

```
Auto-queuing      : Enabled (3 assemblers)
Auto-queuing      : Disabled (info-only)
Auto-queuing      : Disabled (no assemblers)
```

`Enabled` requires `!InfoOnly && Assemblers != null && Assemblers.Count > 0` — exactly the condition `BuildAndRepairAutoQueuing.Handle` checks before calling `CheckAssemblerQueues`.

### Default Custom Data template

`ScanTaggedPanels` checks each tagged block's `CustomData` after a successful tag match. If it is null or whitespace, the script writes a commented `@BaR` template (`BuildAndRepairAutoQueuing.DefaultCustomDataTemplate`) into it. The template exposes every key with its default value and hints at `FontSize=auto` and `@BaR@<surfaceIdx>` scoped blocks for cockpits. Non-empty Custom Data is never touched, and the write happens only when CustomData is empty, so it is naturally idempotent across reinit cycles.

### Refresh timing

- Name tag and Custom Data are both read at init and on the periodic ~30 second reinit (`_ReInit` in `BuildAndRepairAutoQueuing.Handle`). The interval was reduced from 120 s to 30 s as part of FEAT-068 so renames and Custom Data edits take effect quickly.
- To apply changes immediately, the user recompiles the PB (already the existing convention).

## Files Affected

- `SKO-Nanobot-BuildAndRepair-System-Script/Script.cs`
  - New header documentation for the name tag and Custom Data blocks, including multi-surface tags and scoped blocks.
  - New `DisplayEntry` class.
  - `BuildAndRepairSystemQueuingGroupData` switched from `StatusDisplays`/`DisplayKindIdx`/`NextSwitchTime` arrays to a `List<DisplayEntry> DisplayEntries`; added `InfoOnly` property populated each tick by `RefreshDisplays`.
  - `DisplayDefinition` gained `FontSize` (float) and `AutoFitFontSize` (bool).
  - `BuildAndRepairAutoQueuing.Initialize` expanded to scan for tagged panels first, dedupe explicit `DisplayNames` against tagged surfaces, and then add tagged entries per group. Each created `StatusAndLogDisplay` is wired to its effective `FontSize` / `AutoFitFontSize` via `ApplyDisplayDefinitionToStatusDisplay`.
  - `ScanTaggedPanels` seeds empty Custom Data with `DefaultCustomDataTemplate` (commented defaults) on first match; never touches non-empty Custom Data.
  - `DisplayStatus` now emits an `Auto-queuing` line based on `InfoOnly` and the assembler list.
  - New helpers: `BuildEffectiveDisplayDefinition(name)`, `BuildEffectiveDisplayDefinitionFromBlock(block, surfaceIdx)`, `ParseAllBaRBlocks(customData)`, `ApplyCustomDataBody`, `MakeDefaultDisplayDefinition`, `ApplyDisplayDefinitionToStatusDisplay`, `ResolveSurfaceByName`, `ScanTaggedPanels`, `TryParseBaRTag(out string, out int[])`, `MatchGroupId`, nested `TaggedPanel` class, `DefaultCustomDataTemplate` constant.
  - New `StatusAndLogDisplay(MyGridProgram, string, IMyTextSurface)` constructor used by the auto-discovery path (no name-resolution; direct surface binding).
  - `StatusAndLogDisplay` gained `OverrideFontSize` / `AutoFitFontSize` properties, a `_FontApplied` HashSet, and an `EnsureFontApplied` helper called per panel before each `SetPanelText`.
  - `StatusAndLogDisplay.SetPanelText` forces `panel.Font = "Monospace"` so the pre-existing column-aligned status lines render correctly.
  - `_ReInit` interval reduced from `120` to `30` seconds.
  - File shrunk from 2634 → 2075 lines by stripping the Complex Example comment block, commented-out `ScriptControlledGrinding` example, and ~400 lines of self-describing XML doc comments across `RepairSystemHandler`, `EntityHandler`, `StatusAndLogDisplay`, `BuildAndRepairAutoQueuing`, and `BuildAndRepairSystemQueuingGroupData`.
- `docs/pages/Build-and-Repair-System/Companion-Script/index.md`
  - "Per-LCD Configuration" section covering the name tag (including multi-surface `@0,1,2`), the Custom Data format (unscoped `@BaR` and scoped `@BaR@<n>`), the full key table with `FontSize`, font/alignment note, auto-seeded template description, auto-fit example, and the auto-queuing line mention in the `Status` kind row. Refresh timing updated to 30 s throughout.
- `docs/plan/state.md`
  - FEAT-068 row updated to reflect the expanded scope.

## Testing

1. **Backward compatibility:** existing script config with no tags and no Custom Data — confirm all panels still show the configured pages and rotate as before.
2. **Name tag by index:** rename a plain LCD to `"Test LCD [BaR:1]"` and recompile the PB — expect it to start showing the default `Status` page immediately.
3. **Name tag by name:** set a group `Name = "Hangar"` and rename an LCD to `"Test [BaR:Hangar]"` — expect it to attach to that group.
4. **Cockpit single-surface tag:** rename a cockpit to `"Cockpit [BaR:1@0]"` — expect surface 0 to display the status page.
5. **Cockpit multi-surface tag:** rename a multi-surface cockpit to `"Cockpit [BaR:1@0,1,2]"` — expect surfaces 0, 1, 2 to all show the default status page, each as its own entry.
6. **Custom Data override (standalone):** on a tagged LCD, set Custom Data to
   ```
   @BaR
   Kinds=MissingItems
   SwitchTime=0
   @/BaR
   ```
   Expect the LCD to stop rotating and show only the Missing Items page.
7. **Custom Data override (explicit DisplayNames):** list an LCD in `DisplayNames`, add a Custom Data block with `Kinds=WeldTargets` — expect the LCD to show only weld targets, overriding the script's kinds array.
8. **Scoped cockpit Custom Data:** with the multi-surface cockpit from test 5, set Custom Data to
   ```
   @BaR
   MaxLines=15
   @/BaR
   @BaR@0
   Kinds=Status
   SwitchTime=0
   @/BaR
   @BaR@1
   Kinds=WeldTargets,MissingItems
   @/BaR
   @BaR@2
   Kinds=GrindTargets
   SwitchTime=0
   @/BaR
   ```
   Expect surface 0 to show only Status, surface 1 to rotate weld/missing, surface 2 to show only grind targets. All three should inherit `MaxLines=15`.
9. **Dedup:** list an LCD in `DisplayNames` AND rename it with `[BaR:1]` — expect exactly one entry, not two (the tag wins). Verify via PB echo output.
10. **Malformed tag:** rename an LCD to `"Test [BaR:]"` — expect no attachment, no crash.
11. **Out-of-range surface:** rename a two-surface cockpit to `"Cockpit [BaR:1@5]"` — expect an informational line in `InitializationResultMessage` and no attachment for that index. For `"Cockpit [BaR:1@0,5]"`, surface 0 attaches and surface 5 is logged as unresolved.
12. **Default base config:** empty `Displays` array on a group, tag an LCD to it — expect the built-in default (`Status`, 19 lines, 5 s) to be used.
13. **Refresh:** while the PB is running, edit the Custom Data of a tagged LCD and wait up to 30 seconds — expect the override to take effect on next reinit. Recompiling the PB should apply it immediately.
14. **Column alignment:** any existing LCD that uses the `Status` page — expect the label and value columns to line up cleanly (script forces `Monospace` font).
15. **Explicit font size:** set `FontSize=1.2` in an LCD's Custom Data — expect the font size in the terminal to become `1.2` on next recompile.
16. **Auto-fit font:** set `FontSize=auto` on a small cockpit surface and a large wall LCD — expect the small surface to shrink the text and the large surface to grow it so content fills the area in both dimensions.
17. **Default template seeding:** rename an LCD with empty Custom Data to `"Test [BaR:1]"` and recompile — expect the Custom Data to be populated with a commented `@BaR` template listing `Kinds`, `MaxLines`, `SwitchTime`, a commented `FontSize=auto`, and scoped-block examples.
18. **Preserve non-empty Custom Data:** tag an LCD that already has some Custom Data (e.g. a single `Owner=me` line) — expect the script to leave it untouched, and the panel to use the base script config.
19. **Auto-queuing state (enabled):** run the PB without `info-only` argument and with assemblers configured — expect the Status page to show `Auto-queuing      : Enabled (N assemblers)`.
20. **Auto-queuing state (info-only):** run the PB with the `info-only` argument — expect the Status page to show `Auto-queuing      : Disabled (info-only)` and no assembler queue changes.
21. **Auto-queuing state (no assemblers):** remove all assemblers from a group — expect `Auto-queuing      : Disabled (no assemblers)` on the Status page.

# FEAT-053: Rename "Build new" to "Build Projections" and reorder welding section
## Status: Done
## Priority: Low
## Version: v2.5.0
## Summary
Rename the "Build new" terminal option to "Build Projections" for clarity, and move it along with the weld mode dropdown to the top of the welding settings section.
## Motivation
"Build new" was ambiguous — players didn't know it referred to projected blocks from a projector. "Build Projections" is self-explanatory. Moving it and the weld mode to the top makes the most important welding options immediately visible.
## Design
- Renamed `WeldBuildNew` text in all 4 languages (English, Polish, German, Russian) plus tooltips.
- Reordered terminal controls so "Build Projections" and "Weld mode" appear first in the welding section, before the ignore color picker.
## Files Affected
- `Localization/TextsEnglish.cs` — "Build new" → "Build Projections"
- `Localization/TextsPolish.cs` — "Buduj nowe" → "Buduj projekcje"
- `Localization/TextsGerman.cs` — "Neue Blöcke erzeugen" → "Projektionen bauen"
- `Localization/TextsRussian.cs` — "Построить новый" → "Строить проекции"
- `Terminal.cs` — reordered AllowBuild and WeldMode to top of welding section
## Testing
- Terminal shows "Build Projections" instead of "Build new".
- "Build Projections" and "Weld mode" appear at the top of the welding settings section.
- All 4 languages display the updated text.

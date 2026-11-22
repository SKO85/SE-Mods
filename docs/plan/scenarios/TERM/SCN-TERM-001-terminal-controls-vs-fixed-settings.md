# SCN-TERM-001: Terminal controls, actions and sliders reflect fixed vs default mod settings
## Area: TERM
## Tier: 2
## Preconditions
- Local game. Two runs of `ModSettings.xml`: (a) defaults; (b) `Welder` section with
  `AllowedWorkModes = WeldOnly` only, `AllowedSearchModes = Grids` only, `AllowBuildFixed = true`,
  `AreaSizeFixed = true`, `UseIgnoreColorFixed = true`, `ScriptControllFixed = true`, `SoundVolumeFixed = true`.
## Steps
1. Open a BaR terminal in run (a): all controls present and enabled; vanilla "Help Others" hidden.
2. Toolbar: add `WeldOnly_On`, `AllowBuild_OnOff`, `AreaWidth_Increase`, `ShowArea_On`,
   `WeldPriority_OnOff`, `Grids_On` actions; trigger each; verify the terminal reflects the change.
3. Run (b): reopen the terminal.
4. Reload the world twice (controls-init retry path).
## Expected outcome
- (b): single-value combos are removed/disabled with no property setter; fixed switches/sliders are
  disabled and their actions are no-ops; `ScriptControlled` switch and property are absent entirely;
  colour setters silently ignore writes.
- Actions' on/off text is localised via `MyTexts` (BUG-260610.37); actions and controls are registered
  once even after an init retry (BUG-260824.11); init state resets on a mid-init exception (BUG-260610.38).
- `Reset All Settings` restores defaults within the allowed masks; a settings change made in the
  terminal round-trips to the server and back without being dropped (500 ms per-sender window).
- BUG-261023.9: a change that can't alter the scan (sound volume, a script's `CurrentPicked*Block`)
  doesn't force a debounce-bypassing rescan (`ImmediateRescanTrigger` reason=settingsChanged absent);
  area/mode/flag/priority changes still do, and `config set` always does.
## Perf envelope
n/a
## Coverage
manual
unit:SettingsScanKeyTests.PickedTargetChange_DoesNotChangeTheKey
unit:SettingsScanKeyTests.ScanRelevantChanges_ChangeTheKey
## Last verified
v2.5.6 build 261023.6 2026-10-23 — unit (logic only; in-game steps not yet run)
## Related
BUG-008, BUG-013, BUG-056, BUG-260610.37/.38, BUG-260612.38, BUG-260824.8, BUG-260824.11, BUG-261023.9

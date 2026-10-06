# SCN-LOC-001: Terminal labels render in EN / DE / PL / RU and DisableLocalization
## Area: LOC
## Tier: 1
## Preconditions
- Local game; one BaR. Game language switched between English, Deutsch, Polski, Русский.
- Second run with `DisableLocalization = true` in `ModSettings.xml`.
## Steps
1. For each language open the BaR terminal, the info panel, a toolbar action tooltip, and trigger
   `/nanobars -help`.
2. `DisableLocalization = true`: repeat in Deutsch.
## Expected outcome
- Every control label, tooltip, combo entry, on/off action name and panel line
  (`Info_InventoryFull`, `Info_LimitReached`, `Info_BlockSwitchedOff`, …) shows the translated text
  from `Localization/Texts*.cs`; no raw key names, no `???`.
- Unlocalised languages fall back to English; `DisableLocalization` forces English everywhere.
- Action on/off texts resolve through `MyTexts` (BUG-260610.37); the grind priority tooltip matches the
  code behaviour (BUG-260610.43).
## Perf envelope
n/a
## Coverage
manual
## Last verified
never
## Related
BUG-260610.37, BUG-260610.43, `Localization/LocalizationHelper.cs`

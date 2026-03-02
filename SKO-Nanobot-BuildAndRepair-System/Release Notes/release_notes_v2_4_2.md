# Release Notes – v2.4.2 — Hotfix #2

- Release date: 19 February 2026
- Notes: N/A

---

## Bug Fixes

- **Grind priority list not respected in mixed work modes**
  The grind priority list (enabled/disabled block classes) was only correctly applied when the work mode was set to _Grinding only_. In _Weld before grind_, _Grind before weld_, and _Grind if welding gets stuck_ modes, the priority list was ignored and all block classes were treated as enabled. This has been fixed.

- **Grind order toggle leaves no option selected when switched off**
  Switching off _Smallest grid first_ or _Farthest first_ left none of the three grind order options in a selected state. Both now correctly fall back to _Nearest first_ when turned off.

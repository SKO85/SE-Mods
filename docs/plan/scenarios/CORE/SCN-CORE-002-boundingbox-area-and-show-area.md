# SCN-CORE-002: BoundingBox search mode: area size/offset clamps and Show Area
## Area: CORE
## Tier: 2
## Preconditions
- Creative world, admin. Default `ModSettings.xml` (`Range` 100 m, `MaximumOffset` default).
- One BaR on a small rig; a second, separate damaged grid placed 60 m away.
- `Welder.AllowedSearchModes` includes `BoundingBox`; `AreaSizeFixed` / `AreaOffsetFixed` off.
## Steps
1. Set `SearchMode = BoundingBox`, `Show area` on; observe the wireframe box.
2. Drag `AreaWidth/Height/Depth` sliders to their max; drag `AreaOffsetLeftRight/UpDown/FrontBack` to max.
3. Set `Range` to 50 via `/nanobars config set Range 50` — sliders and box shrink.
4. Place the damaged grid just inside, then just outside, the box edge.
5. Switch back to `SearchMode = Grids`.
## Expected outcome
- Sliders never exceed `Settings.MaximumRange` / `MaximumOffset` (`SyncBlockSettings.CheckLimits`);
  lowering `Range` clamps stored values on the next `CheckLimits` and the box redraws.
- Inside: the separate grid's damaged blocks appear in `Blocks to build` after the next scan;
  outside: they do not (`AsyncAddBlocksOfBox` OBB containment).
- `Grids` mode drops the separate grid immediately after the settings-change rescan (FEAT-080).
- With `AreaSizeFixed` on, the size sliders are disabled and the `AreaWidth_Increase` actions are no-ops.
## Perf envelope
n/a (visual). `AsyncAddBlocksOfBox` maxMs ≤ 20 with one 100-block grid in range.
## Coverage
manual
## Last verified
never
## Related
BUG-020b (floating objects outside area), BUG-260610.42 (MaximumOffset bound)

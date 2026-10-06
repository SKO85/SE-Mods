# SCN-WELD-004: Ignore-colour blocks are skipped; creative mode needs no components
## Area: WELD
## Tier: 2
## Preconditions
- Survival world: one BaR, stocked cargo, 10 damaged armor blocks, 5 of them painted a distinct colour.
- `UseIgnoreColor` on, `IgnoreColor` picked from one of the painted blocks (HSV sliders or
  `BuildAndRepair.IgnoreColor` property); `Welder.UseIgnoreColorFixed` off.
- Second run in a creative world with empty cargo.
## Steps
1. Enable the BaR; observe `Blocks to build`.
2. Repaint one ignored block to a different colour.
3. Creative world: enable the BaR with empty cargo.
## Expected outcome
- Only the 5 non-ignored blocks are listed and welded; `skipIgnore` in the `ServerTryWelding`
  profiler details counts the others. Repainting a block makes it a target after the next scan.
- Colour match uses the packed HSV compare (`IgnoreColorPacked`), tolerant to the terminal's slider
  rounding; `UseIgnoreColorFixed` disables the switch and the property setter becomes a no-op.
- Creative: `CreativeModeActive` — blocks are welded with no components pulled, `Missing` stays
  empty, the panel shows `Creative Mode Active`.
## Perf envelope
n/a
## Coverage
manual
## Last verified
never
## Related
BUG-022 (priority cache ignores real param), BUG-260610.40 (sort caches cleared per scan)

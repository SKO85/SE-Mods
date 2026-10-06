# SCN-BUDGET-004: Auto stagger groups 1/2/3 by enabled BaR count
## Area: BUDGET
## Tier: 3
## Preconditions
- `StaggerGroupCount = 0` (auto). 12 BaRs placed, some disabled.
## Steps
| Enabled BaRs | Expected `Stagger groups` on HUD |
|---|---|
| 0–5 | 1 |
| 6–10 | 2 |
| 11+ | 3 (`StaggerGroupCountDefault`) |
1. Enable 5 → read HUD; enable 3 more → read (allow the 60-frame cache to expire); enable all 12.
2. Set `StaggerGroupCount = 4` explicitly → HUD shows 4 regardless of count.
## Expected outcome
- Only **enabled** BaRs count (`IsEnabled`), so disabled blocks don't inflate the group count (BUG-031).
- Each BaR's effective groups may be larger than the mod-wide value: cluster size and a sim-speed
  penalty (`simSpeed < 0.9` adds `ceil((1-sim) × groups)`) are added in `NanobotSystem.Update.cs`.
- Work still fires: every BaR gets its slot (`ClaimStaggerSlot` round-robin) within `groups` cycles.
## Perf envelope
n/a
## Coverage
planned: unit:ModResolverTests.AutoStaggerGroupsFor_Thresholds
## Last verified
never
## Related
FEAT-260910.3 (seam S3 `AutoStaggerGroupsFor`), BUG-031, BUG-102 (isolated BaR stagger collapse)

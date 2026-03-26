# FEAT-021: AsyncScanForSources cost reduction
## Status: Done (Won't Fix — Negligible)
## Priority: Low
## Resolution: Profiling (120s, 2026-03-17) shows cost dropped to 25.7ms total (3 calls, 8.6ms avg) — 0.2ms/sec on background thread. Cluster sharing reduced call count. Not worth optimizing.
## Version: v2.5.0

## Summary
Reduce the cost of `AsyncScanForSources` (83ms total, 10.4ms avg, 8 calls) — the most expensive per-call scan method.

## Motivation
Profiling (120s, 60 BaRs) shows source scanning at ~10ms per call. It runs every 30s per coordinator (8 calls over 120s). With 67 source inventories found per scan, each checked via `AddIfConnectedToInventory` (66ms total, 752 calls). Low total impact due to infrequent calls, but each call is heavy and blocks the background thread.

## Design
Options to investigate:
1. **Incremental source scanning** — only re-check sources when grid blocks change (block added/removed events) instead of full re-scan.
2. **Cache connected-to-inventory results** — `AddIfConnectedToInventory` (0.087ms avg, 752 calls) checks conveyor connectivity. Results could be cached with a TTL since conveyor topology rarely changes.
3. **Share source lists across cluster** — coordinator already scans sources for the cluster. Members could share the source list without each running their own connectivity checks.

## Profiling baseline (120s, 60 BaRs)
| Metric | Value |
|--------|-------|
| AsyncScanForSources calls | 8 |
| AsyncScanForSources total | 83ms |
| AsyncScanForSources avg | 10.4ms |
| AddIfConnectedToInventory calls | 752 |
| AddIfConnectedToInventory total | 66ms |

## Files Affected
- `NanobotSystem.Scanning.cs` — `AsyncScanForSources`
- `NanobotSystem.Inventory.cs` — `AddIfConnectedToInventory`

## Testing
1. Deploy 40+ BaRs with conveyor-connected inventories
2. Run `/nanobars profile start 120`
3. Compare `AsyncScanForSources` avg with baseline (10.4ms)
4. Verify all source inventories are still discovered correctly
5. Test adding/removing containers — verify sources update within 30s

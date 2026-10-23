# SCN-BUDGET-001: Block assignment TTL: a claimed block is released to another BaR
## Area: BUDGET
## Tier: 1
## Preconditions
- `AssignToSystemEnabled = true`, `AssignmentTtlSeconds = 8` (default).
- Two BaRs (A, B) on one rig with 30 damaged armor blocks and stocked cargo.
## Steps
1. Enable both BaRs; observe `Blocks to build` on each — no block name appears on both at the same time.
2. Disable BaR A while it holds a lock-on (`CurrentWeldingBlock != null`).
3. Watch BaR B for up to `AssignmentTtlSeconds`.
4. Re-enable A.
## Expected outcome
- While both run, `BlockSystemAssigningHandler.TryGetAssignedSystem(block)` for any
  `CurrentWeldingBlock` returns that BaR's own `EntityId`; the debug HUD `Block Assigns` count never
  exceeds the number of active BaRs × per-cycle actions.
- Disabling A releases its claims immediately (`ReleaseAllForSystem`); if A had crashed instead, B
  picks the block up after the TTL expires (`TtlCache.IsExpired` uses `>=`).
- Lock-on survives a scan rebuild (identity by `CubeGrid.EntityId + Position`, not reference).
- BUG-261023.11: the periodic cleanup (background pool) never deletes a claim refreshed while it runs.
## Perf envelope
`ServerTryWelding` `assignOpsMs` ≤ 0.5 per call.
## Coverage
unit:TtlCacheTests.SetThenTryGet_ReturnsBeforeExpiry_AndMissesAfter
unit:TtlCacheTests.CleanupExpired_RemovesOnlyExpired_TwoPass
ingame:S05
unit:TtlCacheTests.CleanupExpired_KeepsEntriesRefreshedWhileItRuns
## Last verified
v2.5.6 build 261006.3 2026-10-23 — unit (logic only; in-game steps not yet run)
## Related
BUG-005, BUG-028, BUG-029, BUG-048, BUG-054, BUG-003 / BUG-260522.3 (TtlCache cleanup), FEAT-260910.2/.3, BUG-261023.11

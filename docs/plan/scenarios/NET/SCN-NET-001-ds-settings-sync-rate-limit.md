# SCN-NET-001: Dedicated server: client settings sync and per-sender rate limit
## Area: NET
## Tier: 4
## Preconditions
- Torch / vanilla DS; two clients (A owner, B faction-mate with terminal access, C unrelated player).
  One BaR owned by A. `ModSettings.xml` present on the server with non-default values.
## Steps
1. A and B join: both receive the server `SyncModSettings` (`Mod.SettingsValid` true within 10 s of
   the `MsgDataRequest` cadence); `/nanobars config list` on the server and a client agree.
2. A changes `WorkMode`; within ~0.5 s B changes `AreaWidth`.
3. C tries to change a setting through the terminal (should not have access) and via a crafted
   `MsgBlockSettings` (cannot forge — server validates sender access).
4. Watch the BaR panel on B while A's BaR welds (state sync 1–2 s, delta-synced lists ≤ 24 items).
5. Unload and reload the world on the server.
## Expected outcome
- Both client changes are applied and persisted; the second is **not** dropped by the 500 ms limit
  because the window is per (block, sender) (BUG-260824.8); the rate limit still blocks a single
  client spamming (BUG-260610.31).
- C's change is rejected server-side (`BUG-260610.2` sender access validation) and logged.
- `SyncBlockState` deltas: full sync every `FullSyncInterval = 5`, `ExcludedLists` bits used, hash
  rebuilt under the list lock (BUG-260610.23); transmit backoff when nothing changed
  (`TryTransmitState` `reason=fpUnchanged`); `SyncSent/SyncSkipped` visible on the HUD.
- After reload the network handlers are registered again (BUG-260610.27 / BUG-260824.3 latch fix).
- BUG-261023.10: repeated block data requests from one client for the same BaR within 2 s are dropped
  (stream-in sends one request per BaR, so legit clients are unaffected).
## Perf envelope
`MsgBlockStateSend` steadyAvgMs ≤ 0.3; `TryTransmitState` skip path ≤ 0.05 ms.
## Coverage
unit:SettingsReceiveTests (access check: owner / admin / no access / unknown sender; forger rejected before the
rate limit; 500 ms per (block, sender); two senders both applied; client ignores non-server settings)
manual
unit:ProtoContractTests.ProtoMemberTags_AreUniquePerType
## Last verified
v2.5.6 build 261006.3 2026-10-23 — unit (logic only; in-game steps not yet run)
## Related
BUG-034, BUG-043, BUG-260610.2/.23/.27/.31/.41, BUG-260824.3/.8, BUG-260612.8..10, BUG-261023.10

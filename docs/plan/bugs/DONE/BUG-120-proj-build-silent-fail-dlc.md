# BUG-120: `proj.Build` silently fails for DLC-missing online owners — BaR retries forever

## Status: Fixed
## Severity: Medium (player-visible: BaR appears stuck, never welds the projection, wastes weld slots)
## Version: v2.5.4
## Found In: `NanobotSystem.Welding.cs` (`ServerDoWeld` projected-block resolve branch)

## Symptom

Player report: a BaR keeps trying to weld a projected grid that contains DLC blocks the BaR's owner doesn't have. Every tick the BaR attempts the same projected block and fails. There is no error in the mod log, no profiler spike — the BaR simply never makes progress on that projection. Other valid weld targets on the same scan list may starve while the broken block is locked-on and retried.

## Root cause

`proj.Build()` has two failure modes when the build owner lacks the required DLC:

1. **Offline owner** — SE's `MyProjectorBase.BuildInternal` → `MySessionComponentGameInventory.HasArmor` null-derefs because the entitlement table isn't populated for offline identities. Throws `NullReferenceException`. **Already handled by BUG-115** via `try { proj.Build(...) } catch (NullReferenceException)` and persistent `_BrokenProjBuildKeys` HashSet at `NanobotSystem.cs:174`.
2. **Online owner without DLC** — `proj.Build()` returns cleanly, `HasArmor` returns false, `ValidateArmor` rejects the build, no physical block materializes. **Not handled.** The post-build resolve at `Welding.cs:600-628` detects the failure (`projectorGrid.GetCubeBlock(blockPos)` returns null), sets `targetData.Ignore = true` at line 628, but that flag lives on the per-`TargetBlockData` instance which gets rebuilt every ~2 s by the background scan refresh. So the very next scan re-feeds the same projected block to the weld loop, and the cycle repeats.

The DLC pre-check (BUG-104, removed) is not a viable alternative: `MyAPIGateway.DLC.HasDLC(dlcId, identityId)` queries SE's local entitlement cache, which is unpopulated for offline players (returns false even when the player owns the DLC) and on dedicated servers without an active Steam session for the BaR's owner. BUG-104 documented that the check rejected far more valid welds than DLC-missing ones.

## Fix

Mirror BUG-115's persistent-skip pattern for the silent-fail branch, with two additions: a small retry tolerance to ignore transient races, and **owner-scope invalidation** so the cache resets when the BaR's owner changes (DLC entitlements may differ).

### 1. New fields (`NanobotSystem.cs`, near the existing `_BrokenProjBuildKeys`)

```csharp
private readonly Dictionary<string, int> _ProjBuildSilentFailCount = new Dictionary<string, int>();
private const int PROJ_BUILD_MAX_SILENT_FAILS = 3;
private long _BrokenCacheOwnerId = long.MinValue;
```

### 2. Owner-scope helper (`NanobotSystem.Welding.cs`, near `GetBrokenBlockKey`)

```csharp
private void EnsureBrokenCacheOwnerScope()
{
    var currentOwner = _Welder != null ? _Welder.OwnerId : 0L;
    if (currentOwner != _BrokenCacheOwnerId)
    {
        _BrokenProjBuildKeys.Clear();
        _ProjBuildSilentFailCount.Clear();
        _BrokenCacheOwnerId = currentOwner;
    }
}
```

Called once at the top of `ServerTryWelding` — one `long` comparison per tick. Polling-based; the codebase has no event-driven ownership-change detection elsewhere (`GridOwnershipCacheHandler` uses TTL, every other path reads `_Welder.OwnerId` fresh).

### 3. Silent-fail branch (`NanobotSystem.Welding.cs:626-628` in `ServerDoWeld`)

The existing `else { targetData.Ignore = true; }` is extended to track the failure. After `PROJ_BUILD_MAX_SILENT_FAILS` consecutive failures the block is promoted to `_BrokenProjBuildKeys`, which the existing `Weldable()` short-circuit at `Welding.cs:364` already honors. A single `Logging.Level.Event` line is emitted on first promotion (gated by `HashSet.Add` return value).

### 4. Power-cycle reset (`NanobotSystem.Init.cs`, `_onEnabledChanged` lambda)

Both caches are cleared on `EnabledChanged` so a player toggling the BaR off→on (or on→off) gets a fresh attempt — the natural UX trigger for "I just acquired the DLC, retry now."

## Why this scope

- **Reuses BUG-115's infrastructure**: `_BrokenProjBuildKeys`, `GetBrokenBlockKey`, the `Weldable()` short-circuit, and the `_onEnabledChanged` subscription are all already in place. Net ~30 added lines.
- **Failure-mode agnostic**: the same machinery catches any future cause of silent `proj.Build` failure (world settings disabling specific blocks, weight limits, projector mode flags).
- **Owner-scoped without per-tick cost**: the polling guard fires once per `ServerTryWelding` invocation. Only on owner-change does it do non-trivial work (clear two small dicts).
- **Retry tolerance protects against false positives**: 3 consecutive failures rules out one-tick races (another BaR built it the same tick, momentary projector disable, brief component shortage mid-build).
- **Three player-visible recovery paths**: (a) acquire DLC → toggle BaR off/on; (b) admin/terminal reassigns BaR to faction-mate who owns the DLC → automatic on owner change; (c) world reload → automatic.

## Verification

1. **Build clean** — `dotnet build ... -c Release -v minimal` → 0 warnings, 0 errors. ✓
2. **In-game test (DLC-missing block)**: place projector with a blueprint containing DLC blocks the BaR owner doesn't have. Let BaR cycle 3+ ticks against the offending block. Expected: after 3 failures, single `proj.Build silently failed 3 times … ignored permanently for owner X` event log line; subsequent ticks skip the block; BaR moves on to other targets if any exist.
3. **In-game test (power-cycle reset)**: with a block in the broken set, toggle BaR off→on. Expected: BaR re-attempts the block, re-promotes after another 3 failures (confirms `_onEnabledChanged` clears the caches).
4. **In-game test (owner-change reset)**: with a block in the broken set, transfer BaR ownership to a faction-mate who owns the DLC. Expected: next weld tick succeeds (`created=true; result=True`), block is no longer in skip set.
5. **Regression check (NRE path)**: BUG-115 offline-mode repro still produces `earlyExit=projBuildNRE` log on first failure, immediate add to skip set (no retry tolerance for NRE — engine state is definitively broken in that case).

## See also

- BUG-104 — original DLC pre-check; removed because of offline-owner unreliability.
- BUG-115 — persistent skip set + try/catch for the **NRE** failure mode. This ticket extends the same machinery to the **silent-fail** failure mode and adds owner-scope.
- `NanobotSystem.cs:174` — `_BrokenProjBuildKeys` declaration; the new fields sit next to it.
- `NanobotSystem.Welding.cs:364` — existing `Weldable()` short-circuit that consumes `_BrokenProjBuildKeys`; no change needed there.
- `NanobotSystem.Welding.cs:600-628` — the silent-fail detection point in `ServerDoWeld`.

## Out of scope (intentionally not in this change)

- **Per-DLC entitlement caching** by reading `block.BlockDefinition.DLCs` and querying `MyAPIGateway.DLC.HasDLC` upfront — same offline-owner unreliability that ruled out BUG-104. Defer until SE provides a reliable offline-aware API.
- **Time-based retry / cooldown** — DLC entitlement is essentially fixed mid-session; the explicit power-cycle path covers the "I just bought the DLC" case without time-based complexity.
- **Per-owner cache preservation** (`Dictionary<long, HashSet<string>>` keyed by ownerId) so A→B→A ownership flip-flops don't lose state. Owner ping-pong is rare; clear-on-change matches the user's intent ("cache the ignore per player if the owner of the BaR remains the same"). Revisit only if telemetry shows it's needed.

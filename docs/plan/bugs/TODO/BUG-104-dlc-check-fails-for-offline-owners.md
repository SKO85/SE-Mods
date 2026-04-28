# BUG-104: DLC check filters out projected blocks when the BaR owner is offline

## Status: Fixed
## Severity: Medium
## Version: v2.5.4
## Found In: `Helpers/DlcCheckHelper.cs`, `Utils/UtilsPlayer.cs`, `NanobotSystem.Scanning.cs`

## Description

`DlcCheckHelper.IsBlockDlcAvailableForOwner` is supposed to filter out projected blocks the BaR owner doesn't have a DLC for. In practice it also filters out **every DLC-tagged projected block when the BaR owner is offline** — because the underlying `UtilsPlayer.GetPlayer(ownerId)` only resolves currently-connected players.

This makes BaRs effectively unable to build DLC-tagged blocks (Decorative pack, Sparks of the Future, Warfare, etc.) while the owner is logged off — the opposite of what most players expect from an autonomous build-and-repair system.

A stale-cache symptom compounds the issue: once the empty-DLC result is cached for an offline owner, it persists across the owner logging in until `CleanupOwnerCache()` runs.

## Steps to Reproduce

1. On a dedicated server, place a BaR owned by Player A. Project a blueprint containing at least one DLC-tagged block (e.g. a Decorative pack window) within range.
2. While Player A is online: BaR welds the projected block normally.
3. Player A logs off.
4. Damage / replace the block so it becomes a fresh projected target. Or wait for the next scan cycle.
5. **Observed**: BaR no longer welds the DLC-tagged block. The block is silently filtered out at scan time and doesn't appear in `PossibleWeldTargets`. Non-DLC blocks on the same projection continue to weld.
6. Player A logs back in. **Observed**: the BaR still doesn't weld the DLC block until `CleanupOwnerCache()` fires (periodic, several minutes).

## Root Cause

### Path A — offline owner returns no DLCs

`Utils/UtilsPlayer.cs:10-21`:

```csharp
public static IMyPlayer GetPlayer(long identityId)
{
    var players = GetAllPlayers();
    return players.FirstOrDefault(c => c.IdentityId == identityId);
}

public static List<IMyPlayer> GetAllPlayers()
{
    var list = new List<IMyPlayer>();
    MyAPIGateway.Players.GetPlayers(list);  // online players only
    return list;
}
```

`MyAPIGateway.Players.GetPlayers` returns only currently-connected players, so an offline `ownerId` resolves to `null`.

`Helpers/DlcCheckHelper.cs:63-90`:

```csharp
private static HashSet<string> GetOwnedDlcs(long ownerId)
{
    // ... cache check ...
    var player = UtilsPlayer.GetPlayer(ownerId);
    var owned = new HashSet<string>();

    if (player != null && !player.IsBot)
    {
        foreach (var kvp in MyAPIGateway.DLC.GetDLCs())
        {
            if (MyAPIGateway.DLC.HasDLC(kvp.Name, player.SteamUserId))
                owned.Add(kvp.Name);
        }
    }
    // owned stays empty when player == null
    _OwnerDlcCache[ownerId] = owned;
    return owned;
}
```

When `player == null`, no DLCs are added. `IsBlockDlcAvailableForOwner` then loops over `requiredDlcs` and returns `false` on the first one, filtering the block out at `NanobotSystem.Scanning.cs:441` and `:785`.

### Path B — cache poisoning

The empty set computed for the offline owner is cached in `_OwnerDlcCache`. When the owner logs back in, the cache is not invalidated. `CleanupOwnerCache()` (`Mod.cs:432`) runs on a periodic TTL cleanup, so the stale "no DLCs" entry can persist for minutes after login.

## Fix

**Option A applied: drop the DLC check entirely.**

Rationale: the projector itself already enforces DLC ownership when the projection is loaded. Once a DLC-tagged block exists in the world as a projection, that's evidence the projector's owner was sanctioned to project it. Letting any BaR materialize it adds no DLC-policy bypass — only a physical step. Removing the BaR-side check eliminates both the offline-owner failure and the cache-poisoning compounding effect, with no real DLC-enforcement loss.

Changes:

- `NanobotSystem.Scanning.cs:441` — removed `DlcCheckHelper.IsBlockDlcAvailableForOwner(block, _Welder.OwnerId) &&` from the projected-block filter chain.
- `NanobotSystem.Scanning.cs:785` — removed the same call from the cluster-scan projected-block filter.
- `Mod.cs:432` — removed the `DlcCheckHelper.CleanupOwnerCache()` periodic invocation in the TTL cleanup task.
- `Helpers/DlcCheckHelper.cs` — deleted entirely. No remaining consumers; the `using SKONanobotBuildAndRepairSystem.Helpers;` directives in other files stay (they reference other helpers like `PowerHelper`, `InventoryHelper`).

Alternatives considered and rejected:

- **B** (offline-capable SteamID resolution via `MyAPIGateway.Players.TryGetSteamId`) — adds complexity to preserve a check that duplicates upstream enforcement.
- **C** (cache invalidation on player connect) — only addresses path B of the bug, leaves path A broken.
- **D** (treat offline owner as "has all DLCs") — same outcome as A but with dead code; cleaner to remove.

## Verification

1. **Build clean** — `dotnet build ... -c Release -v minimal` → 0 warnings, 0 errors. ✓
2. **Offline-owner build test** — on a dedicated server: place a BaR owned by Player A, project a DLC blueprint, log Player A off. Damage a DLC block. Confirm BaR welds it.
3. **Online-owner regression** — same setup but with Player A online: confirm DLC blocks still build (no behavioral change for the working case).
4. **Non-DLC blocks unchanged** — projected non-DLC blocks build the same as before in both online and offline scenarios.

## See also

- `Helpers/DlcCheckHelper.cs` — both caches (`_BlockDlcCache`, `_OwnerDlcCache`) and the public entry point.
- `Utils/UtilsPlayer.cs:10` — `GetPlayer` is the resolution function that fails offline.
- `NanobotSystem.Scanning.cs:441,785` — the two scan-time call sites that filter.
- `Mod.cs:432` — periodic `CleanupOwnerCache` invocation.
- Discovered while diagnosing the `thenebula` welding issue (player report: BaRs targeting blocks but never welding). Bypassing this check did **not** fix the thenebula scenario, so the DLC issue and the thenebula issue are independent — but this remains a real bug worth fixing on its own.

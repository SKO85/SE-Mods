# FEAT-080: Trigger immediate rescan when terminal settings change

## Status: Done
## Version: v2.5.4
## Found In: `NanobotSystem.Init.cs`, `NanobotSystem.Operations.cs`, `NanobotSystem.Scanning.cs`

## Description

When a player edits a BaR's settings via the terminal — toggling Work Mode, changing the priority list, adjusting Area Size, switching colour filters, etc. — they expect the change to take effect immediately. With default scan intervals (FEAT-071 idle backoff up to 10 s, working scans every 1-2 s), the player can be staring at a BaR doing nothing for several seconds while the next scan tick produces a fresh target list under the new settings. Confusing and feels like a bug.

The mod already had an immediate-rescan mechanism (`NanobotSystem.Operations.cs:217-240`) that fires when work completes — that's what makes a BaR pick up new targets right after finishing one. We extend the same trigger to fire on settings-changed.

## Implementation

### Extracted helper

Pulled the rescan-trigger logic out of `Operations.cs` into a small reusable method on `NanobotSystem`. Lives in `NanobotSystem.Scanning.cs` next to the related scan code:

```csharp
internal void TriggerImmediateRescan(string reason)
{
    if (!MyAPIGateway.Session.IsServer) return;          // No-op on clients

    var immediateScanTs = MethodProfiler.Start();
    _LastTargetsUpdate = TimeSpan.Zero;                   // Force timer-due

    // Bump the cluster coordinator too so cached results don't suppress the rescan
    var cluster = AssignedCluster;
    if (cluster != null && cluster.Coordinator != null && cluster.Coordinator != this)
    {
        cluster.Coordinator._LastTargetsUpdate = TimeSpan.Zero;
        cluster.Coordinator._rescanForced = true;          // Bypasses FEAT-075 saturated check
    }
    UpdateSourcesAndTargetsTimer();                       // Run the scan now

    if (immediateScanTs != 0L)
    {
        var _reason = reason;
        MethodProfiler.StopAndLog("ImmediateRescanTrigger", immediateScanTs, () =>
            string.Format("entityId={0};reason={1}", _Welder.EntityId, _reason));
    }
}
```

The `reason` field replaces the old `wasWelding`/`wasGrinding` profile-log fields with a richer label (`weldComplete`, `grindComplete`, `settingsChanged`) — useful for distinguishing when each path fires.

### Existing call site updated

`NanobotSystem.Operations.cs:217-240` (work-complete trigger) now calls the helper:

```csharp
if (((State.Welding && !welding) || (State.Grinding && !(grinding || collecting))))
{
    if (!isFullInventoryAndPicking && ready)
    {
        TriggerImmediateRescan(State.Welding ? "weldComplete" : "grindComplete");
    }
}
```

### New call site — settings change

`NanobotSystem.Init.cs:78-79` (`SettingsChanged()`) — append a call after the existing `UpdateCustomInfo(true)`:

```csharp
UpdateCustomInfo(true);

// FEAT-080: terminal settings just changed — force immediate rescan so new targets
// matching the updated settings surface right away instead of waiting up to 10 s.
TriggerImmediateRescan("settingsChanged");
```

`SettingsChanged` is invoked by `NetworkMessagingHandler` after `Settings.AssignReceived` (the merge path that runs on the server when a terminal change comes in over the network — including the local-player single-player loopback). One call per settings update; rate-limited naturally by terminal interaction frequency.

## What this changes for the player

| Before | After |
|---|---|
| Toggle "Allow Build" on. BaR keeps idling for up to 10 s until next scan tick. | Toggle "Allow Build" on. BaR rescans within ~50 ms and picks up projected targets immediately. |
| Add a block to the Weld Priority list. Have to wait for next scan to see it become a target. | Priority change → immediate rescan → block surfaces right away. |
| Change Area Size to a larger value. Distant blocks slowly start appearing as scan ticks elapse. | Area Size change → immediate rescan → all in-range blocks visible at once. |
| Switch Work Mode (e.g. WeldOnly → WeldBeforeGrind). Grind targets don't appear until next scan. | Mode switch → immediate rescan → grind targets appear straight away. |

## What it does NOT change

- **Scan-thread cost**: same as before. The rescan triggered by a settings change is a single scan, not a recurring cost.
- **Scan-throttling**: subsequent scans still respect the timer (`Mod.Settings.TargetsUpdateInterval`) and FEAT-075 saturated-skip / FEAT-071 idle backoff. Only the first scan after a settings change is forced.
- **Behavior on clients**: no-op (no scans run on clients).
- **Behavior on initial settings receive**: also fires once on the first `SettingsChanged` after a block loads. Harmless — the next scan was about to fire anyway, this just brings it forward by a few hundred ms.

## Risk

Low. `UpdateSourcesAndTargetsTimer` was already designed to be called on demand — it has internal early-exit guards for disabled blocks and serializes per-BaR via `Mod.AddAsyncAction`. Calling it more often (once per settings change) just spends a small amount of background-thread work earlier than it would have otherwise.

If a player drags a slider rapidly producing many SettingsChanged events, each triggers a rescan. The async dispatch coalesces — if a scan is already running for a BaR, the second trigger is a no-op until the first completes. So even worst-case sliding doesn't pile up scans.

## Verification

1. **Build clean** — `dotnet build ... -c Release -v minimal` → 0 warnings, 0 errors. ✓
2. **Functional test** — in-game:
   - Place a BaR with idle scan backoff active (~10 s scans).
   - Toggle Work Mode.
   - Confirm `State:` panel updates within ~1 s with new targets.
3. **Profile check** — `ImmediateRescanTrigger` log entries should now include `reason=settingsChanged` events when the player edits the terminal.
4. **Regression — work-complete path** — confirm that finishing a weld/grind still triggers a rescan (now logs `reason=weldComplete` / `reason=grindComplete`).
5. **Regression — clients** — confirm no scan attempt fires on clients (the `IsServer` early-return guards this).

## See also

- FEAT-071 — idle scan backoff. The motivation for this feature (without backoff, scans were every 1-2 s and the wait felt small). With FEAT-071 active, settings-change-rescan is more visible because the otherwise-next scan can be 10 s away.
- FEAT-075 — saturated scan skip. The `_rescanForced` flag was added to bypass that gate; same flag is reused here.
- BUG-103 / BUG-106 / BUG-107 — earlier perf work.

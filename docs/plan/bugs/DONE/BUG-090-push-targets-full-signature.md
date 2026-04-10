# BUG-090: Push-target backoff compares count only, misses same-size container swaps

## Status: Fixed
## Severity: Low
## Version: v2.5.2
## Found In: NanobotSystem.Scanning.cs:1681 (codex P2 review follow-up to BUG-016)

## Description

The `_PushTargetsFull` backoff (introduced by BUG-016) is reset on the cluster-scan apply path by diffing `_PossiblePushTargets.Count` against `_PushTargetsFullCount`. When the push-target *set* changes but the *count* stays the same — e.g., a player swaps a full container for an empty one, or a connector pivot replaces one inventory with another of the same total count — the diff doesn't notice, and `_PushTargetsFull` stays armed until the 60s safety timeout fires. During that window, `ServerTryPushInventory` keeps early-returning at `NanobotSystem.Inventory.cs:37` even though viable empty push targets are present. Worst case: ~60s of unnecessarily delayed auto-pushes. No data loss, no welder stall — welding and grinding continue while the backoff is armed.

## Root Cause

`NanobotSystem.Inventory.cs:98, 145` snapshot only `_PossiblePushTargets.Count` when arming the flag, and `NanobotSystem.Scanning.cs:1681` compares against that snapshot. The comment at `Scanning.cs:1674` states the intent is to reset "when push targets actually changed," but `Count` is a cheap proxy that misses same-size swaps.

## Fix

Replace the count snapshot with a cheap content signature: `count XOR (owner EntityId per inventory)`. Zero allocations, O(n) with n typically < 20, order-independent, catches any single-element swap.

- `NanobotSystem.cs:71` — field renamed `_PushTargetsFullCount` → `_PushTargetsFullSignature` (long).
- `NanobotSystem.Inventory.cs` — added private `ComputePushTargetsSignature()`; both arm sites (`:97-99`, `:144-146`) now store the signature.
- `NanobotSystem.Scanning.cs:1681` — reset check now diffs the signature instead of `Count`.

60s safety timeout is retained unchanged as a backstop.

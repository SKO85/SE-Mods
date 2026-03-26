# BUG-016: Constant push attempts to full push targets

## Status: Fixed
## Severity: Medium
## Version: v2.5.0
## Found In: NanobotSystem.Inventory.cs, NanobotSystem.Operations.cs

## Description

When the BaR's inventory is full and all push targets (cargo containers) are also full, the BaR continuously attempts to push items every tick. The `ServerEmptyTransportInventory()` method is called every tick when transport inventory has items, iterating through all push targets and attempting transfers that always fail. Similarly, `ServerTryPushInventory()` retries every 5 seconds regardless of whether push targets have capacity.

This causes unnecessary per-tick iteration over all push targets and failed transfer API calls — wasted computation in the game loop.

## Root Cause

1. In `ServerTryWeldingGrindingCollecting()` lines 133-148: when idle with non-empty transport, `ServerEmptyTransportInventory(true)` is called every tick. If the welder is full and push targets are full, this call iterates all push targets and fails every time.
2. `ServerTryPushInventory()` has a 5s cooldown but no tracking of whether push targets have capacity.
3. Neither method tracks failed push attempts to back off.

## Fix

Track push failure state with `_PushTargetsFull` flag and a timestamp `_PushTargetsFullSince`. When all push targets reject items, set the flag with a timestamp. Back off push attempts while the flag is set — only retry when push targets are rescanned (which happens every 30s when sources are rescanned, resetting the flag). In `ServerEmptyTransportInventory`, skip the push attempt when `_PushTargetsFull` is true.

Files: NanobotSystem.cs, NanobotSystem.Inventory.cs, NanobotSystem.Scanning.cs

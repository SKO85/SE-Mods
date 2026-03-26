# BUG-015: BaR grinds/collects when welder inventory is full

## Status: Fixed
## Severity: Medium
## Version: v2.5.0
## Found In: NanobotSystem.Grinding.cs, NanobotSystem.Collecting.cs, NanobotSystem.Operations.cs

## Description

When the BaR's welder inventory is completely full and push targets cannot accept items, the BaR still attempts to grind or collect. The existing `State.InventoryFull` check only reflects whether the _transport_ inventory has stuck items. When the transport was previously emptied into the welder (filling it completely), `State.InventoryFull = false` even though there's no room for new grind/collect output.

This causes a wasted grind/collect cycle: items go into transport, transport can't empty to welder (full), push fails (targets full), then `State.InventoryFull = true`. The BaR should detect this proactively.

## Root Cause

`State.InventoryFull` is set exclusively by `ServerEmptyTransportInventory()` based on whether `_TransportInventory` is empty. It does not consider the welder inventory's remaining capacity. When the welder is full and transport is empty, `State.InventoryFull = false` — grinding/collecting proceed, only to discover there's no room after the fact.

## Fix

In `ServerTryWeldingGrindingCollecting()`, after `ServerTryPushInventory()`, check if the welder inventory is full. If so, proactively set `State.InventoryFull = true` so that grinding and collecting are blocked before they waste a cycle.

Files: NanobotSystem.Operations.cs

# BUG-012: Custom info panel shows "Grinding (Transporting)" when collecting
## Status: Fixed
## Severity: Low
## Version: v2.5.0
## Found In: NanobotSystem.Operations.cs / NanobotSystem.State.cs

## Description
When the BaR is only collecting floating objects (not grinding), the custom info panel incorrectly displays "Grinding (Transporting)" during the transport phase of collection.

## Steps to Reproduce
1. Place a BaR block near floating objects with no grind targets.
2. Enable the BaR and let it collect floating items.
3. Open the terminal and observe the info panel during the transport phase.
4. State shows "Grinding (Transporting)" instead of "Collecting (Transporting)".

## Root Cause
In `NanobotSystem.Operations.cs:58`:
```csharp
if (transporting && State.CurrentTransportIsPick) needgrinding = true;
```
This line assumes any "pick" transport originates from grinding, but collecting also sets `CurrentTransportIsPick = true` (Collecting.cs:110). On the next tick, `needgrinding` is set to `true` for a collecting transport, and `GetStateString()` matches `NeedGrinding && Transporting` → "Grinding (Transporting)".

## Fix
Added `CurrentTransportIsCollecting` (server-only) to `SyncBlockState` to distinguish transport origin, and `NeedCollecting` (synced, ProtoMember 44) for the info panel display.

- `SyncBlockState.cs` — Added `NeedCollecting` and `CurrentTransportIsCollecting` properties.
- `NanobotSystem.Collecting.cs` — Sets `CurrentTransportIsCollecting = true` when starting transport.
- `NanobotSystem.Grinding.cs` — Sets `CurrentTransportIsCollecting = false` when starting transport.
- `NanobotSystem.Welding.cs` — Sets `CurrentTransportIsCollecting = false` when starting transport.
- `NanobotSystem.Operations.cs:58` — Routes to `needcollecting` vs `needgrinding` based on `CurrentTransportIsCollecting`. Also tracks `State.NeedCollecting` and includes it in change detection and profiler output.
- `NanobotSystem.State.cs:GetStateString()` — Added `NeedCollecting && Transporting` → "Collecting (Transporting)".

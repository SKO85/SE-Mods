# BUG-087: Effects rendered at unlimited distance, wasting GPU on far-away BaRs

## Status: Fixed
## Severity: Medium
## Version: v2.5.1
## Found In: Effects.cs — UpdateEffects / SetTransportEffects / SetWorkingEffects

## Description

All particle effects (transport traces, welding/grinding sparks) and sounds are rendered regardless of the player's distance from the BaR block. On servers with many BaRs, players render particles and sounds for BaR systems used by other players hundreds or thousands of meters away. The `TryCreateParticleEffect` calls pass `uint.MaxValue` as the render distance, disabling any engine-level distance culling.

This wastes GPU resources and contributes to the global effect caps (`MaxTransportEffects`, `MaxWorkingEffects`) being consumed by BaRs the player can't even see.

Additionally, the global effect caps were too low (50 transport, 80 working) for servers with many active BaRs.

## Steps to Reproduce

1. Join a server with 50+ active BaRs spread across multiple locations
2. Observe particle effects being created for BaRs far from the player's camera
3. Effect caps are consumed by distant BaRs, starving nearby BaRs of their visual effects

## Root Cause

No distance check between the camera and the BaR block in `UpdateEffects`. All effects are created and updated unconditionally.

## Fix

Effects.cs:

1. Add a camera distance check at the top of `UpdateEffects`. When the camera is farther than 1500m (`MaxEffectDistanceSq = 1500.0 * 1500.0`), suppress all effects for that BaR:
   - Skip transport and working effect creation/updates
   - Tear down any active effects so they release their global counter slots
   - Effects resume automatically when the player moves back within range
2. Raise `MaxTransportEffects` from 50 to 150
3. Raise `MaxWorkingEffects` from 80 to 150

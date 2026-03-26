# BUG-009: Sound effects emitted at player position instead of BaR block
## Status: Fixed
## Severity: Medium
## Version: v2.5.0
## Found In: Effects.cs — SetWorkingEffects / UpdateWorkingEffectPosition
## Description
Welding and grinding sound effects were heard at the player's position rather than at
the BaR block's location. When a player was far away from the BaR, the sounds would
still play as if the BaR was next to them.

## Root Cause
Multiple issues in `Effects.cs`:
1. **Destroyed block position fallback** — `UpdateWorkingEffectPosition` accessed
   `CurrentWeldingBlock` / `CurrentGrindingBlock` without checking `IsDestroyed` or
   `CubeGrid.Closed`. Calling `GetWorldBoundingBox` on a destroyed/closed block could
   return a zero or garbage matrix, causing the SE engine to fall back to the listener
   (player) position for the sound emitter.
2. **Unsafe sound array access** — Direct `_Sounds[(int)workingState]` indexing without
   bounds checking; an out-of-range state could crash or produce undefined behavior.
3. **Unsafe cue name check** — `sound.GetCueName()` called without null guard, risking
   NRE when the sound pair returns null.
4. **Effect counter leak on Close()** — `Close()` stopped particle effects but did not
   decrement `_ActiveWorkingEffects` / `_ActiveTransportEffects`, eventually blocking
   all BaRs from creating new effects.

## Fix
All changes in `Effects.cs`:
- Added `IsDestroyed` / `CubeGrid.Closed` guards in `UpdateWorkingEffectPosition` (line ~297-312)
- Bounds-checked sound index: `(soundIndex >= 0 && soundIndex < _Sounds.Length)` (line ~217-218)
- Null-safe cue name: `sound != null ? sound.GetCueName() : null` (line ~221)
- Bounds-checked sound level access with `soundLevel` variable (line ~240)
- Added `Interlocked.Decrement` for effect counters in `Close()` (line ~402, 410)
- Changed `MaxTransportEffects` / `MaxWorkingEffects` from `const` to `static readonly` (line ~26, 28)

Ported from `fix/v2.5.1` branch.

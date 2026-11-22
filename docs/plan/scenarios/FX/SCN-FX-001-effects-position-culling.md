# SCN-FX-001: Weld / grind particle and sound effects at block position, distance-culled
## Area: FX
## Tier: 1
## Preconditions
- Local game (effects are client-side). One BaR welding and one grinding; `DisableParticleEffects` /
  `DisableTickingSound` off server-wide; per-block switches visible.
## Steps
1. Stand next to the BaR: weld beam/particles and sound appear at the target block, ticking sound at
   the BaR; transport effect flies between BaR and block.
2. Walk 1 km away; return.
3. Toggle per-block `DisableParticleEffects` and `DisableTickingSound`; then set the server-wide flags
   via `/nanobars config set DisableParticleEffects true` and reopen the terminal.
4. Change `SoundVolume` slider to 0 and 100.
## Expected outcome
- Effects/sounds emit at the BaR/block position, not the player (BUG-009); state-driven start/stop
  (`Effects.cs`), created on the main thread only (BUG-044).
- Beyond the culling distance no particles/sounds are spawned (BUG-087); they resume on return.
- Per-block switches suppress only that block; server-wide flags hide the switches entirely
  (`Terminal.cs` conditional registration) and force effects off for all blocks.
- Volume slider scales the sound; `SoundVolumeFixed` disables it.
## Perf envelope
`Effects` update ≤ 0.2 ms per BaR on the client; zero cost on a DS.
## Coverage
manual
## Last verified
never
## Related
BUG-009, BUG-044, BUG-087, FEAT-260511.x (per-block effect toggles)

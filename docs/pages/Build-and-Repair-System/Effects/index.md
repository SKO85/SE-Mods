---
layout: default
title: Effects & Sound
parent: Build and Repair System
nav_order: 8
---

# Effects & Sound

The Build and Repair block produces a set of visual and audio effects: welding flashes, grinding sparks, the flying nanobot transport beam, and the ticking sound when the system is working or unable to find a target. Each effect can be controlled per block in the terminal and globally in `ModSettings.xml`.

---

## Effect Types

| Effect | Description |
|---|---|
| `WeldingVisualEffect` | The welding flash on the target block. |
| `WeldingSoundEffect` | The welding sound played near the target. |
| `GrindingVisualEffect` | The grinding sparks on the target block. |
| `GrindingSoundEffect` | The grinding sound played near the target. |
| `TransportVisualEffect` | The flying nanobot trace beam between the BaR and the active target. |

These effect types are controlled server-wide by the `AllowedEffects` setting in `ModSettings.xml` — removing an entry from that list disables the effect for every block. Per block, the terminal offers a **Disable particle effects** toggle (turns off the flying nanobot trace for that block), a **Disable ticking sound** toggle, and a **Sound Volume** slider. There is no per-block toggle for the individual weld / grind effect types.

---

## Disabling Particle Effects

The flying nanobot trace (the visual transport beam) is the most often-disabled effect — it is the one most visible at distance and the one most likely to be cited as visual clutter on a busy base. There are **two ways** to turn it off, depending on whether you want to silence the effect just for one BaR or for the whole server:

### Per-Block (Terminal)

Open the BaR's terminal and tick **Disable particle effects**. The change applies immediately and only to that BaR — other BaRs on the same grid keep their own settings. Players can do this themselves; no admin permission required.

> The per-block toggle is **only visible when the server-wide `DisableParticleEffects` is off**. If the server has already disabled the effects globally, the terminal toggle is hidden.

This is the right choice when only some BaRs are noisy (e.g. a cluster of repair-pad welders close to a window) but you want other BaRs elsewhere on the server to keep their effects.

### Server-Wide (ModSettings.xml)

To disable particle effects for **every** BaR on the server, set `DisableParticleEffects = true` in `ModSettings.xml`:

```xml
<DisableParticleEffects>true</DisableParticleEffects>
```

When this is on, the trace beam is suppressed on every BaR regardless of its terminal toggle — the per-block setting is overridden. This is the right choice on dedicated servers when an admin wants to remove the effect globally without asking every player to toggle their own blocks.

A more surgical alternative is `AllowedEffects`: removing `TransportVisualEffect` (or any other entry) from the space-separated list prevents that effect from playing on every block. Use this when you want the effect permanently gone rather than admin-overridden.

| What you want | Where to change it |
|---|---|
| Disable for one BaR | Terminal → tick **Disable particle effects** |
| Disable for the whole server (admin override) | `ModSettings.xml` → `<DisableParticleEffects>true</DisableParticleEffects>` |
| Disable a specific effect type server-wide | `ModSettings.xml` → remove the entry from `<AllowedEffects>` |

The other visual effects (`WeldingVisualEffect`, `GrindingVisualEffect`) have no per-block toggle — they are controlled only by the `AllowedEffects` server setting. `DisableParticleEffects` specifically targets the flying nanobot trace.

---

## Disabling the Ticking Sound

The ticking / "unable" sound (the recurring sound a BaR plays while it cannot find a target) is independent from the visual / sound effects in the **Effects** section above and has its own pair of toggles. As with particle effects, you can disable it **per block** or **server-wide**:

### Per-Block (Terminal)

Open the BaR's terminal and tick **Disable ticking sound**. The change applies immediately and only to that BaR. Players can do this themselves; no admin permission required.

> The per-block toggle is **only visible when the server-wide `DisableTickingSound` is off**. If the server has already silenced the sound globally, the terminal toggle is hidden because there is nothing left to do.

### Server-Wide (ModSettings.xml)

To silence the ticking sound for **every** BaR and **every** player on the server, set `DisableTickingSound = true` in `ModSettings.xml`:

```xml
<DisableTickingSound>true</DisableTickingSound>
```

When this is on, the per-block toggle disappears from the terminal and the sound is silenced globally regardless of any per-block setting.

The two toggles are OR'd: either one disables the sound. The per-block toggle does **not** override the server-wide one — once the admin silences it globally, individual players cannot turn it back on.

| What you want | Where to change it |
|---|---|
| Silence for one BaR | Terminal → tick **Disable ticking sound** |
| Silence for the whole server | `ModSettings.xml` → `<DisableTickingSound>true</DisableTickingSound>` |

The per-block ticking-sound toggle is independent of the **Sound Volume** slider — the volume slider only scales the weld and grind sound effects, not the ticking sound.

---

## Server-Wide Toggles

| Setting | Default | Purpose |
|---|---|---|
| `DisableTickingSound` | `false` | When `true`, silences the ticking / unable sound for every BaR and every player on the server. Hides the per-block "Disable ticking sound" terminal toggle. |
| `DisableParticleEffects` | `false` | When `true`, disables the flying nanobot trace globally and hides the per-block "Disable particle effects" terminal toggle. |
| `AllowedEffects` | (all five) | Space-separated list of allowed effect types. Remove a value to prevent that effect from playing on every block. |

---

## Transport Visual

The flying nanobot beam between the BaR and its target travels at the welder transport speed of **50 m/s**.

The transport visual is purely cosmetic — items are picked up directly when the welder needs them, regardless of whether the visual is currently mid-flight.

---

## Sound Volume

| Setting | Default | Description |
|---|---|---|
| `SoundVolumeDefault` | `1` | Default per-block sound volume. Range 0 (silent) to 2 (maximum); the default of `1` shows as 50% on the terminal slider. |
| `SoundVolumeFixed` | `false` | When `true`, the per-block sound volume slider is locked server-wide. |

The slider in the terminal controls weld and grind sound volume for that specific block. The ticking sound is not affected by the per-block slider — use the **Disable ticking sound** toggle (or `DisableTickingSound` server-wide) to silence it.

> The mod also provides an in-world **Debug HUD** overlay for admins. It is not part of the Effects controls — see [Debug & Diagnostics → Debug HUD](../Debug-and-Diagnostics/#debug-hud).

---

## Troubleshooting

<details>
<summary>I ticked "Disable particle effects" but I still see effects.</summary>
<div>
<p>Per-block toggles affect only the BaR you edited — other BaRs on the same grid keep their own settings. If multiple BaRs are working the same target, each one has to be configured separately.</p>
<p>The per-block toggle only disables the flying nanobot trace. The weld flash and grind sparks are controlled server-wide via <code>AllowedEffects</code> in <code>ModSettings.xml</code> and cannot be turned off per block.</p>
</div>
</details>

<details>
<summary>The ticking sound is annoying — how do I silence it?</summary>
<div>
<p>Two options:</p>
<ul>
<li><strong>For just one BaR</strong> — open its terminal and tick <strong>Disable ticking sound</strong>. The change is per-block and immediate.</li>
<li><strong>For the whole server</strong> — set <code>DisableTickingSound = true</code> in <code>ModSettings.xml</code>. This silences every BaR for every player and hides the per-block toggle (since there is nothing left to disable).</li>
</ul>
</div>
</details>

<details>
<summary>The "Disable ticking sound" toggle is missing from my terminal.</summary>
<div>
The server admin has set <code>DisableTickingSound = true</code> in <code>ModSettings.xml</code>, which silences the sound globally and hides the per-block toggle. There is nothing for the per-block toggle to do in that state.
</div>
</details>

<details>
<summary>The transport beam never reaches the target before snapping back.</summary>
<div>
The visual transport speed is now <strong>50 m/s</strong>. On a target far from the BaR (e.g. 150 m+ away with a wide work area) the visible beam may not appear to "land" on the target before the next operation starts. This is purely cosmetic — items are picked up synchronously when the welder needs them. Welding speed is unaffected.
</div>
</details>

<details>
<summary>Sound volume slider does nothing.</summary>
<div>
Either <code>SoundVolumeFixed = true</code> in <code>ModSettings.xml</code> (admin-locked), or the weld / grind sound effects are disabled server-wide via <code>AllowedEffects</code>. The slider scales the sound — if the sound is off there is nothing to scale.
</div>
</details>

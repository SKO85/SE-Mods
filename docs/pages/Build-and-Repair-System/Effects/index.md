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

The terminal **Effects** controls let players toggle each type per block. The set of available effect types is filtered by the server-wide `AllowedEffects` setting in `ModSettings.xml` — removing an entry from that list hides it from the terminal entirely.

---

## Disabling Particle Effects

The flying nanobot trace (the visual transport beam) is the most often-disabled effect — it is the one most visible at distance and the one most likely to be cited as visual clutter on a busy base. There are **two ways** to turn it off, depending on whether you want to silence the effect just for one BaR or for the whole server:

### Per-Block (Terminal)

Open the BaR's terminal, find the **Effects** section, and untick **TransportVisualEffect** (and any of the other entries you want gone for that block). The change applies immediately and only to that BaR — other BaRs on the same grid keep their own settings. Players can do this themselves; no admin permission required.

This is the right choice when only some BaRs are noisy (e.g. a cluster of repair-pad welders close to a window) but you want other BaRs elsewhere on the server to keep their effects.

### Server-Wide (ModSettings.xml)

To disable particle effects for **every** BaR on the server, set `DisableParticleEffects = true` in `ModSettings.xml`:

```xml
<DisableParticleEffects>true</DisableParticleEffects>
```

When this is on, the trace beam is suppressed on every BaR regardless of its terminal toggle — the per-block setting is overridden. This is the right choice on dedicated servers when an admin wants to remove the effect globally without asking every player to toggle their own blocks.

A more surgical alternative is `AllowedEffects`: removing `TransportVisualEffect` (or any other entry) from the space-separated list hides that effect from the terminal entirely *and* prevents it from playing. Use this when you want the option permanently gone rather than admin-overridden.

| What you want | Where to change it |
|---|---|
| Disable for one BaR | Terminal → Effects section → untick the specific effect |
| Disable for the whole server (admin override) | `ModSettings.xml` → `<DisableParticleEffects>true</DisableParticleEffects>` |
| Hide the option from the terminal entirely | `ModSettings.xml` → remove the entry from `<AllowedEffects>` |

The same two-tier model applies to the other visual effects (`WeldingVisualEffect`, `GrindingVisualEffect`) via the per-block Effects toggles and the `AllowedEffects` server setting. There is no per-effect global "disable" flag for those — only `DisableParticleEffects` exists, and it specifically targets the transport trace.

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
| `DisableParticleEffects` | `false` | When `true`, disables the flying nanobot trace globally. Individual blocks can also turn it off in the terminal; this setting overrides the per-block setting and forces the effect off. |
| `AllowedEffects` | (all five) | Space-separated list of allowed effect types. Remove a value to hide that effect from the per-block terminal controls and prevent it from playing. |

---

## Transport Visual

The flying nanobot beam between the BaR and its target uses the welder transport speed as its travel rate. As of v2.5.4, the transport speed is set to **50 m/s** (tuned down from 80 m/s during the BUG-103 work). Per release notes:

> Welder Transport Speed Tuned Back Down — purely a tuning change. Duty cycle is no longer gated by the transport timer either way (BUG-103).

The transport visual is purely cosmetic — items are picked up synchronously when the welder needs them, regardless of whether the visual is currently mid-flight.

---

## Sound Volume

| Setting | Default | Description |
|---|---|---|
| `SoundVolumeDefault` | `1.0` | Default per-block sound volume. Range: 0.0 (silent) – 1.0 (full). |
| `SoundVolumeFixed` | `false` | When `true`, the per-block sound volume slider is locked server-wide. |

The slider in the terminal controls weld and grind sound volume for that specific block. The ticking sound is not affected by the per-block slider — use the **Disable ticking sound** toggle (or `DisableTickingSound` server-wide) to silence it.

> The mod also provides an in-world **Debug HUD** overlay for admins. It is not part of the Effects controls — see [Debug & Diagnostics → Debug HUD](../Debug-and-Diagnostics/#debug-hud).

---

## Troubleshooting

<details>
<summary>I unticked an effect in the terminal but it still plays.</summary>
<div>
<p>Per-block toggles affect only the BaR you edited — other BaRs on the same grid keep their own settings. If multiple BaRs are working the same target, each one has its own Effects section to configure.</p>
<p>If a specific effect type is missing from the terminal entirely, the admin has removed it from <code>AllowedEffects</code> in <code>ModSettings.xml</code> — the option cannot be re-enabled per block.</p>
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
Either <code>SoundVolumeFixed = true</code> in <code>ModSettings.xml</code> (admin-locked), or the underlying weld / grind sound effects are themselves disabled in the <strong>Effects</strong> section. The slider scales the sound — if the sound is off there is nothing to scale.
</div>
</details>

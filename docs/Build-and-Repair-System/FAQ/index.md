---
layout: default
title: FAQ
parent: Build and Repair System
nav_order: 2
---

# Frequently Asked Questions

---

## General

<details>
<summary>What does this mod do?</summary>

The SKO Nanobot Build and Repair System automatically welds, repairs, grinds, and collects floating objects within its range. It replaces tedious manual work with an automated system that can be configured per block through the terminal and tuned server-wide through a configuration file.

</details>

<details>
<summary>Does this mod replace the original mod or do I need both?</summary>

This mod fully replaces the original. It uses the same block type, so you can simply remove the original mod from your world and add this one — your existing Build and Repair blocks will continue to work in the same saved world.

</details>

<details>
<summary>What is the difference between the two versions of this mod?</summary>

- **[Nanobot Build and Repair System (Maintained)](https://steamcommunity.com/sharedfiles/filedetails/?id=2111073562)** — A rebalanced version where the block requires more resources and is heavier to build.
- **[Nanobot Build and Repair System - Original Resources](https://steamcommunity.com/sharedfiles/filedetails/?id=3099489876)** — Identical in functionality and fixes, but uses the same build components as the original mod. Only the required build resources differ between the two.

</details>

<details>
<summary>Can I use multiple versions of this mod at the same time?</summary>

No. Running multiple versions of the mod in the same world will not work and may break things. Use only one version at a time.

</details>

<details>
<summary>Does it work in Creative mode?</summary>

Yes. In Creative mode the system will weld and build blocks without requiring components to be present in inventory, matching the behaviour of the original mod.

</details>

<details>
<summary>Does it work with the Shields mod?</summary>

Yes. The mod includes built-in checks for the Shields mod. When a target is protected by a shield, the system will skip it. This check is enabled by default and can be disabled in `ModSettings.xml` if needed.

</details>

<details>
<summary>Does it work with DLC blocks?</summary>

Yes. The system checks whether the owner of the Build and Repair block has the required DLC before attempting to build a projected block. If the owner does not own the DLC, that block is skipped to prevent failed build attempts.

</details>

<details>
<summary>Does it work with the multigrid projection plugin?</summary>

Yes, welding of projected grids using the multigrid-projection plugin is supported.

</details>

---

## Installation & Troubleshooting

<details>
<summary>The mod does not seem to work. How do I troubleshoot or reinstall it?</summary>

Follow these steps in order:

1. Exit the game completely.
2. In Steam, open the game's **Properties → Betas** and make sure **None** is selected to ensure you are running the latest game version.
3. Go to the mod page on Steam, click **Unsubscribe**, and wait 5–10 seconds for Steam to remove the files.
4. Click **Subscribe** again and wait 5–10 seconds for the download to complete.
5. Start the game.
6. In your mod list, place this mod towards the **end of the list** so it loads after other mods.
7. If progression is enabled in your world, the Build and Repair block must be researched before it appears. It is located near the Welder in the progression tree. If progression is disabled, search for "Build and" in the block list.
8. If you are using a custom `ModSettings.xml`, make sure it is not an outdated version. See the [Configuration File](https://github.com/SKO85/SE-Mods/wiki/Configuration-File) page for the current format.
9. If Steam still does not download the mod correctly, try manually deleting the mod's folder from your Steam workshop content directory, then reload the session to trigger a fresh download.
10. If you are using server-side or client-side plugins such as **Multi-Grid-Projector**, try disabling them to see if the problem goes away. Some plugins patch game API calls in ways that can interfere with mod behaviour. If you identify a plugin conflict, please open an issue so it can be investigated.

</details>

<details>
<summary>I cannot find the Build and Repair block in the build menu.</summary>

- Type `/nanobars -help` in chat. If a help window appears with the mod version, the mod is loaded. If nothing happens, the mod is not active in your world or server.
- The block appears under the **Large Blocks**, **Small Blocks**, and **Tools** categories.
- If progression is enabled, you need to research the block first. It is located near the Welder in the progression tree.

<img width="895" height="263" alt="Progression tree showing the Build and Repair block near the Welder" src="https://github.com/user-attachments/assets/6c60ce2f-bc04-4264-9fba-c1a94efa9610" />

</details>

<details>
<summary>The mod shows an initialisation error in the control panel info panel.</summary>

This usually means the mod was not fully downloaded on the server — a known issue with Steam on dedicated servers where mods are sometimes only partially downloaded. The server then starts with an incomplete mod, which causes errors.

Stop the server, delete the mod's cached folder from the workshop content directory, and restart to force a clean download.

</details>

---

## Block Seems Stuck

<details>
<summary>The Build and Repair block is not doing anything. What should I check?</summary>

Work through this checklist:

- Is the block **powered** and **enabled**?
- Is **Auto Pull/Push** enabled? Without it, the system cannot pull the components it needs to weld.
- Check the **work mode** — try switching between modes to see if that helps.
- Is the inventory **full**, or are required components **missing**?
- Check the **Help Others** option and try toggling it.
- Are you inside a **Safe Zone** that does not have Weld, Grind, or Build enabled?
- Do you have **Shields** active on the target? Shields prevent grinding.
- Are you using a server plugin that **protects grids**, such as the `!protect` command from ALE PCU Transferrer? Protected grids cannot be welded or ground.
- Check the **info panel** in the terminal — it often shows a specific reason why the system is idle.

</details>

---

## Welding & Grinding Issues

<details>
<summary>The block is grinding my own grid. What is wrong?</summary>

Two common causes:

- You have set a **grind color** that matches your build color. The system interprets all blocks with that color as grind targets. Check and update the grind color setting in the terminal.
- Some blocks on your grid may have been transferred to an **enemy** or **neutral** owner, making them valid grind targets. Check block ownership.

</details>

<details>
<summary>The block will not weld a specific target block. Why?</summary>

- Try welding the same block manually with your hand torch. If that also fails, something is physically in the way or a required DLC is missing.
- Check that you own the **DLC** required to build the target block.
- Check for **obstructions** — a wheel or misaligned component is a common culprit. Remove the obstruction, let the system weld the rest, then weld the blocked component manually or via the system afterwards.
- If you are using a plugin such as **Multi-Grid-Projector**, try disabling it and testing again. Plugins that patch game API calls can affect welding behaviour in unexpected ways.

</details>

---

## Configuration (Server Admins)

<details>
<summary>Can I change settings like range, sounds, and particle effects?</summary>

Yes. Many options can be configured through the `ModSettings.xml` file. See the [Configuration File](https://github.com/SKO85/SE-Mods/wiki/Configuration-File) wiki page for the full list of available options.

</details>

<details>
<summary>How do I create the server configuration file?</summary>

Run `/nanobars -cwsf` in-game to generate a `ModSettings.xml` file in your local Space Engineers folder. Copy the file to your dedicated server, edit the values as needed, and restart the server for the changes to take effect.

</details>

<details>
<summary>How do I disable the ticking sound for all players?</summary>

Set `DisableTickingSound` to `true` in `ModSettings.xml`. This silences the ticking/unable sound globally for all blocks and all players on the server.

</details>

<details>
<summary>How do I disable the flying nanobot particle effects for all players?</summary>

Set `DisableParticleEffects` to `true` in `ModSettings.xml`. This disables the flying nanobot trace animations globally. Players can also toggle this per block in the terminal, unless the global setting is already forcing it off.

</details>

<details>
<summary>How do I limit how many systems can work on the same grid at once?</summary>

Use the following settings in `ModSettings.xml`:

- `MaxSystemsPerTargetGrid` — the maximum number of systems allowed to work on the same target grid simultaneously (default: `10`).
- `DisableLimitSystemsPerTargetGrid` — set to `true` to remove the limit entirely.

This prevents many systems piling onto a single grid while others nearby are ignored.

</details>

<details>
<summary>How do I disable reputation loss when grinding enemy or neutral grids?</summary>

Set `DecreaseFactionReputationOnGrinding` to `false` in `ModSettings.xml`. By default, grinding grids belonging to other factions or NPCs causes a reputation penalty, matching manual grinding behaviour.

</details>

---

## Safe Zones

<details>
<summary>Does the system respect Safe Zones?</summary>

Yes. The system checks Safe Zone rules before welding, grinding, or building projections:

- Grinding enemies inside a Safe Zone you or your faction do not own is blocked.
- Grinding inside your own Safe Zone is allowed when the relevant Safe Zone option is enabled in the Safe Zone settings panel.

These checks can be disabled in `ModSettings.xml` if needed.

</details>

<details>
<summary>The system is welding or grinding a protected grid. How do I stop it?</summary>

If you are using the **ALE PCU Transferrer** plugin for Torch, use the `!protect` command on the grid. The system will detect the protection flags and skip it entirely. Use `!unprotect` to make the grid accessible again.

</details>

---

## Terminal & Priorities

<details>
<summary>How do the Weld and Grind priority lists work?</summary>

The priority lists control which block types the system targets first. Each block class (e.g. Armor, Thrusters, Reactors) can be enabled or disabled and reordered using the **Up** and **Down** buttons in the terminal. The system processes enabled classes from top to bottom.

Use the **Enable All** and **Disable All** buttons to quickly toggle every entry at once.

</details>

<details>
<summary>What does "Ignore Priority Order" do?</summary>

When enabled, the system ignores the order of the priority list and simply targets the nearest available block, regardless of its block class. The enabled/disabled state of each class is still respected — only the ordering is bypassed.

</details>

<details>
<summary>How do I reset a block back to its default settings?</summary>

Open the block's terminal and click **Reset All Settings**. This restores all settings for that block to their defaults, including the priority list states.

</details>

---

## Companion Script

<details>
<summary>Where can I find the companion auto-queuing script?</summary>

A maintained version of the script is available on the Steam Workshop:
[Nanobot Build and Repair System Queuing / Display / Scripting (Maintained)](https://steamcommunity.com/sharedfiles/filedetails/?id=3472701905)

The script source is also included in this repository under `SKO-Nanobot-BuildAndRepair-System-Script/`.

</details>

<details>
<summary>What does the companion script do and how do I set it up?</summary>

The companion script runs on a Programmable Block and handles automatic assembler queuing and multi-display status output for one or more Build and Repair System groups. It can show current weld/grind targets, missing components, and transport status on LCD panels or cockpit screens.

**Setup:**
1. Place a Programmable Block on your grid.
2. Open the script from the Steam Workshop link above (or from `SKO-Nanobot-BuildAndRepair-System-Script/Script.cs`) and paste it into the Programmable Block editor.
3. Edit the `BuildAndRepairSystemQueuingGroups` array at the top of the script to match your block and assembler names or group names.
4. Compile and run the script. It updates automatically on every 100-tick cycle.

</details>

---

## Community & Support

<details>
<summary>Where can I get help or ask questions?</summary>

Join the Discord server and ask in the **#help** channel:
[https://discord.gg/5XkQW5tdQM](https://discord.gg/5XkQW5tdQM)

You can also open an issue on GitHub if you believe you have found a bug:
[https://github.com/SKO85/SE-Mods/issues](https://github.com/SKO85/SE-Mods/issues)

</details>

<details>
<summary>Can I donate to support development?</summary>

Yes, donations are very welcome and appreciated:
[Donate via PayPal](https://www.paypal.com/paypalme/SKO85GAMING)

</details>

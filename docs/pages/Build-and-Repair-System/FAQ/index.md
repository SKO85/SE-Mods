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
<div>
The SKO Nanobot Build and Repair System automatically welds, repairs, grinds, and collects floating objects within its range. It replaces tedious manual work with an automated system that can be configured per block through the terminal and tuned server-wide through a configuration file.
</div>
</details>

<details>
<summary>Does this mod replace the original mod or do I need both?</summary>
<div>
This mod fully replaces the original. It uses the same block type, so you can simply remove the original mod from your world and add this one — your existing Build and Repair blocks will continue to work in the same saved world.
</div>
</details>

<details>
<summary>What is the difference between the two versions of this mod?</summary>
<div>
<p><strong><a href="https://steamcommunity.com/sharedfiles/filedetails/?id=2111073562">Nanobot Build and Repair System (Maintained) - Nerfed Version</a></strong> — A rebalanced version where the block requires more resources and is heavier to build.</p>
<p><strong><a href="https://steamcommunity.com/sharedfiles/filedetails/?id=3099489876">Nanobot Build and Repair System (Maintained) - Original Resources Version</a></strong> — Identical in functionality and fixes, but uses the same build components as the original mod. Only the required build resources differ between the two.</p>
</div>
</details>

<details>
<summary>Can I use multiple versions of this mod at the same time?</summary>
<div>
No. Running multiple versions of the mod in the same world will not work and may break things. Use only one version at a time.
</div>
</details>

<details>
<summary>Does it work in Creative mode?</summary>
<div>
Yes. In Creative mode the system will weld and build blocks without requiring components to be present in inventory, matching the behaviour of the original mod.
</div>
</details>

<details>
<summary>Does it work with the Shields mod?</summary>
<div>
Yes. The mod includes built-in checks for the Shields mod. When a target is protected by a shield, the system will skip it. This check is enabled by default and can be disabled in <code>ModSettings.xml</code> if needed.
</div>
</details>

<details>
<summary>Does it work with DLC blocks?</summary>
<div>
Yes. The system checks whether the owner of the Build and Repair block has the required DLC before attempting to build a projected block. If the owner does not own the DLC, that block is skipped to prevent failed build attempts.
</div>
</details>

<details>
<summary>Does it work with the multigrid projection plugin?</summary>
<div>
<p>Yes, welding of projected grids using the multigrid-projection plugin is supported.</p>
<p><strong>Known limitation:</strong> In some cases the multigrid-projection plugin can break welding. The plugin patches native game API calls, which may not always work as expected. If you encounter a block that cannot be welded, try welding it manually with your hand welder or a standard ship welder first. If manual welding also fails, the issue is caused by the plugin — not by the Build and Repair system. Please report such cases to the multigrid-projection plugin team.</p>
</div>
</details>

<details>
<summary>What blocks can the system pull components from and push items to?</summary>
<div>
<p>The system scans for <strong>source inventories</strong> (to pull components for welding) and <strong>push targets</strong> (to offload items after grinding or when the inventory is full). Both use the same set of supported block types. The block must be connected to the Build and Repair block via the <strong>conveyor network</strong>.</p>
<p><strong>Supported block types:</strong></p>
<ul>
  <li>Cargo Containers</li>
  <li>Connectors</li>
  <li>Conveyor Sorters</li>
  <li>Assemblers</li>
  <li>Refineries</li>
  <li>Ship Grinders</li>
  <li>Ship Welders (excluding other Build and Repair blocks, to prevent circular transfers)</li>
  <li>Cryo Chambers</li>
</ul>
<p>Blocks on connected grids (via connectors, pistons, rotors) are also included as long as they are reachable through the conveyor system. The system rescans for sources and push targets every 30 seconds.</p>
</div>
</details>

---

## Installation & Troubleshooting

<details>
<summary>The mod does not seem to work. How do I troubleshoot or reinstall it?</summary>
<div>
<p>Follow these steps in order:</p>
<ol>
  <li>Exit the game completely.</li>
  <li>In Steam, open the game's <strong>Properties → Betas</strong> and make sure <strong>None</strong> is selected to ensure you are running the latest game version.</li>
  <li>Go to the mod page on Steam, click <strong>Unsubscribe</strong>, and wait 5–10 seconds for Steam to remove the files.</li>
  <li>Click <strong>Subscribe</strong> again and wait 5–10 seconds for the download to complete.</li>
  <li>Start the game.</li>
  <li>In your mod list, place this mod towards the <strong>end of the list</strong> so it loads after other mods.</li>
  <li>If progression is enabled in your world, the Build and Repair block must be researched before it appears. It is located near the Welder in the progression tree. If progression is disabled, search for "Build and" in the block list.</li>
  <li>If you are using a custom <code>ModSettings.xml</code>, make sure it is not an outdated version. See the <a href="../Config/">Configuration File</a> page for the current format.</li>
  <li>If Steam still does not download the mod correctly, try manually deleting the mod's folder from your Steam workshop content directory, then reload the session to trigger a fresh download.</li>
  <li>If you are using server-side or client-side plugins such as <strong>Multi-Grid-Projector</strong>, try disabling them to see if the problem goes away. Some plugins patch game API calls in ways that can interfere with mod behaviour. If you identify a plugin conflict, please open an issue so it can be investigated.</li>
</ol>
</div>
</details>

<details>
<summary>I cannot find the Build and Repair block in the build menu.</summary>
<div>
<ul>
  <li>Type <code>/nanobars -help</code> in chat. If a help window appears with the mod version, the mod is loaded. If nothing happens, the mod is not active in your world or server.</li>
  <li>The block appears under the <strong>Large Blocks</strong>, <strong>Small Blocks</strong>, and <strong>Tools</strong> categories.</li>
  <li>If progression is enabled, you need to research the block first. It is located near the Welder in the progression tree.</li>
</ul>
<img width="895" height="263" alt="Progression tree showing the Build and Repair block near the Welder" src="https://github.com/user-attachments/assets/6c60ce2f-bc04-4264-9fba-c1a94efa9610" />
</div>
</details>

<details>
<summary>The mod shows an initialisation error in the control panel info panel.</summary>
<div>
<p>This usually means the mod was not fully downloaded on the server — a known issue with Steam on dedicated servers where mods are sometimes only partially downloaded. The server then starts with an incomplete mod, which causes errors.</p>
<p>Stop the server, delete the mod's cached folder from the workshop content directory, and restart to force a clean download.</p>
</div>
</details>

<details>
<summary>The mod is not working correctly after an update.</summary>
<div>
<p>In some cases issues can be caused by an outdated configuration file (<code>ModSettings.xml</code>) that contains settings from an older version. To rule this out:</p>
<ol>
  <li>Make a backup of your current <code>ModSettings.xml</code>.</li>
  <li>Delete the original file and restart the server or session.</li>
  <li>Generate a fresh configuration file using <code>/nanobars config create</code>.</li>
  <li>Compare the new file with your backup and merge any custom settings you need back into the new file.</li>
</ol>
<p>This ensures you are using a configuration file that matches the current version of the mod.</p>
</div>
</details>

---

## Block Seems Stuck

<details>
<summary>The Build and Repair block is not doing anything. What should I check?</summary>
<div>
<p>Work through this checklist:</p>
<ul>
  <li>Is the block <strong>powered</strong> and <strong>enabled</strong>?</li>
  <li>Is <strong>Auto Pull/Push</strong> enabled? Without it, the system cannot pull the components it needs to weld.</li>
  <li>Check the <strong>work mode</strong> — try switching between modes to see if that helps.</li>
  <li>Is the inventory <strong>full</strong>, or are required components <strong>missing</strong>?</li>
  <li>Check the <strong>Help Others</strong> option and try toggling it.</li>
  <li>Are you inside a <strong>Safe Zone</strong> that does not have Weld, Grind, or Build enabled?</li>
  <li>Do you have <strong>Shields</strong> active on the target? Shields prevent grinding.</li>
  <li>Are you using a server plugin that <strong>protects grids</strong>, such as the <code>!protect</code> command from ALE PCU Transferrer? Protected grids cannot be welded or ground.</li>
  <li>Check whether the block's search mode is set to <strong>Fly</strong> or <strong>Walk</strong> mode. Try switching between them — the wrong mode for your situation can prevent the system from finding targets.</li>
  <li>Check the <strong>info panel</strong> in the terminal — it often shows a specific reason why the system is idle.</li>
</ul>
</div>
</details>

---

## Performance

<details>
<summary>I have many Build and Repair blocks and my server sim speed is dropping. What can I do?</summary>
<div>
<p>Version 2.5.0 includes major performance improvements for servers running many systems. If you are on an older version, update first.</p>
<p>If sim speed is still low after updating, try these steps:</p>
<ul>
  <li>Place Build and Repair blocks close together so they form a <strong>cluster</strong>. Clustered blocks share scanning work automatically, dramatically reducing CPU usage.</li>
  <li>Lower <code>MaxBackgroundTasks</code> in <code>ModSettings.xml</code> (default: 4). This limits how many background scans run in parallel.</li>
  <li>Increase <code>EmptyGridRescanDelaySeconds</code> (default: 30). Grids with no targets are skipped for this many seconds.</li>
  <li>Reduce <code>MaxSystemsPerTargetGrid</code> to limit how many systems pile onto one grid.</li>
  <li>The mod automatically throttles when sim speed drops below 1.0 — let it recover before adding more blocks.</li>
</ul>
</div>
</details>

<details>
<summary>What is the Cluster Scan Coordinator?</summary>
<div>
When multiple Build and Repair blocks share the same working area, they automatically elect a single coordinator to scan for targets. The coordinator scans once and shares the results with all members. This eliminates redundant scanning and can reduce scan CPU usage by roughly 80% with 10 co-located blocks. If the coordinator is disabled or removed, a new one is elected automatically. You do not need to configure anything — it works out of the box.
</div>
</details>

---

## Weld Modes

<details>
<summary>What are the different weld modes?</summary>
<div>
<p>The terminal dropdown offers three weld modes:</p>
<ul>
  <li><strong>Weld to full</strong> (default) — Welds every block to 100% integrity.</li>
  <li><strong>Weld to functional</strong> — Welds blocks just enough to become functional (lights on, doors work, thrusters fire). Saves components and time when you don't need full armour integrity.</li>
  <li><strong>Skeleton only</strong> — Only places projected blocks (the first component). Does not weld or repair existing blocks. Great for quickly laying out an entire blueprint from a projector, then switching to Full or Functional to finish the job.</li>
</ul>
</div>
</details>

<details>
<summary>How do I quickly build a large ship from a projection?</summary>
<div>
<ol>
  <li>Set up your projector with the blueprint.</li>
  <li>Set all your Build and Repair blocks to <strong>Skeleton only</strong> mode.</li>
  <li>Make sure <strong>Build new</strong> is enabled in the terminal.</li>
  <li>The blocks will rapidly place all projected blocks (one component each).</li>
  <li>Once all blocks are placed, switch to <strong>Weld to full</strong> or <strong>Weld to functional</strong> to weld them up.</li>
</ol>
<p>This two-step approach is much faster than welding each block to completion before placing the next one.</p>
</div>
</details>

<details>
<summary>I switched from the old "Weld to functional only" checkbox. Where did it go?</summary>
<div>
It has been replaced with the <strong>Weld mode</strong> dropdown in the terminal. If your block previously had "Weld to functional only" checked, it will automatically appear as <strong>Weld to functional</strong> in the new dropdown. No action is needed — your saved settings are preserved.
</div>
</details>

---

## Scanning & Detection

<details>
<summary>Why does it take a few seconds before my Build and Repair blocks start welding a new projection?</summary>
<div>
<p>When you first activate a projector, the Build and Repair blocks do not immediately know about the projected blocks. The cluster coordinator needs to scan and discover them first. This scan runs on a timer (roughly every 10 seconds), so there can be a short delay before the first projected block is detected and welding begins.</p>
<p>This "cold start" is a trade-off for performance — scanning less frequently reduces CPU load, especially on servers with many blocks. Once the first few blocks have been welded and new scans complete, more projected blocks become visible and welding speeds up quickly.</p>
<p>Improving the cold-start detection speed is planned for a future update. For now, patience for the first few seconds is expected.</p>
</div>
</details>

---

## Welding & Grinding Issues

<details>
<summary>The block is grinding my own grid. What is wrong?</summary>
<div>
<p>Two common causes:</p>
<ul>
  <li>You have set a <strong>grind color</strong> that matches your build color. The system interprets all blocks with that color as grind targets. Check and update the grind color setting in the terminal.</li>
  <li>Some blocks on your grid may have been transferred to an <strong>enemy</strong> or <strong>neutral</strong> owner, making them valid grind targets. Check block ownership.</li>
</ul>
</div>
</details>

<details>
<summary>The block will not weld a specific target block. Why?</summary>
<div>
<ul>
  <li>Try welding the same block manually with your hand torch. If that also fails, something is physically in the way or a required DLC is missing.</li>
  <li>Check that you own the <strong>DLC</strong> required to build the target block.</li>
  <li>Check for <strong>obstructions</strong> — a wheel or misaligned component is a common culprit. Remove the obstruction, let the system weld the rest, then weld the blocked component manually or via the system afterwards.</li>
  <li>If you are using a plugin such as <strong>Multi-Grid-Projector</strong>, try disabling it and testing again. Plugins that patch game API calls can affect welding behaviour in unexpected ways.</li>
</ul>
</div>
</details>

---

## Debug Mode & Diagnostics

<details>
<summary>What is Debug Mode and how do I use it?</summary>
<div>
<p>Debug Mode is a config option in <code>ModSettings.xml</code>. When enabled, all Build and Repair blocks show extra diagnostic information in the terminal custom info panel, such as scan timings, active target counts, cluster membership, and internal state flags.</p>
<p>This is intended for testing and debugging only — it should not be left enabled during normal play. It can help diagnose why a block is not behaving as expected (e.g. waiting for initial scan, push targets full, grid limit reached).</p>
</div>
</details>

<details>
<summary>How do I run the built-in profiler?</summary>
<div>
<p>The profiler is an admin-only tool accessed through chat commands. It measures the mod's performance impact and writes detailed log files.</p>
<ul>
  <li><code>/nanobars profile start [seconds] [minDurationMs]</code> — start a profiling session (defaults to 120 seconds auto-stop)</li>
  <li><code>/nanobars profile stop</code> — stop the session and write the summary</li>
  <li><code>/nanobars profile status</code> — check if profiling is active</li>
  <li><code>/nanobars profile minduration &lt;ms&gt;</code> — set the minimum method duration for logging</li>
  <li><code>/nanobars profile help</code> — show profiling command help</li>
</ul>
<p>Log files are saved to the mod's storage folder. Share these with the developer if you are reporting a performance issue.</p>
</div>
</details>

---

## Configuration (Server Admins)

<details>
<summary>Can I change settings like range, sounds, and particle effects?</summary>
<div>
Yes. Many options can be configured through the <code>ModSettings.xml</code> file. See the <a href="../Config/">Configuration File</a> page for the full list of available options.
</div>
</details>

<details>
<summary>How do I create the server configuration file?</summary>
<div>
<p>Run <code>/nanobars config create</code> in-game (admin only). This saves the current settings to <code>ModSettings.xml</code> in the mod's storage folder.</p>
<p>On a dedicated server, you can also change settings at runtime using <code>/nanobars config set &lt;setting&gt; &lt;value&gt;</code> and then save them with <code>/nanobars config save</code>. Most settings take effect immediately, but some changes (such as range, power, and welder-specific settings) require a session restart to apply.</p>
</div>
</details>

<details>
<summary>Can I change settings without restarting the server?</summary>
<div>
<p>Yes. As of v2.5.0, most settings can be changed at runtime using chat commands (admin only):</p>
<ul>
  <li><code>/nanobars config list</code> — see all settings and their current values</li>
  <li><code>/nanobars config set MaxGrindsPerTick 8</code> — change a setting immediately</li>
  <li><code>/nanobars config save</code> — save changes to <code>ModSettings.xml</code> so they persist after restart</li>
  <li><code>/nanobars config reload</code> — reload settings from <code>ModSettings.xml</code></li>
  <li><code>/nanobars config reset</code> — reset all settings to defaults</li>
</ul>
<p>Most settings take effect immediately. However, some changes (such as range, power, and welder-specific settings) require a session restart to apply.</p>
</div>
</details>

<details>
<summary>How do I disable the ticking sound for all players?</summary>
<div>
Set <code>DisableTickingSound</code> to <code>true</code> in <code>ModSettings.xml</code>. This silences the ticking/unable sound globally for all blocks and all players on the server.
</div>
</details>

<details>
<summary>How do I disable the flying nanobot particle effects for all players?</summary>
<div>
Set <code>DisableParticleEffects</code> to <code>true</code> in <code>ModSettings.xml</code>. This disables the flying nanobot trace animations globally. Players can also toggle this per block in the terminal, unless the global setting is already forcing it off.
</div>
</details>

<details>
<summary>How do I limit how many systems can work on the same grid at once?</summary>
<div>
<p>Use the following settings in <code>ModSettings.xml</code>:</p>
<ul>
  <li><code>MaxSystemsPerTargetGrid</code> — the maximum number of systems allowed to work on the same target grid simultaneously. Defaults to <code>20</code> in local and listen-server games and <code>10</code> on dedicated servers. Setting this in the config file overrides whichever default applies.</li>
  <li><code>DisableLimitSystemsPerTargetGrid</code> — set to <code>true</code> to remove the limit entirely.</li>
</ul>
<p>This prevents many systems piling onto a single grid while others nearby are ignored.</p>
</div>
</details>

<details>
<summary>How do I disable reputation loss when grinding enemy or neutral grids?</summary>
<div>
Set <code>DecreaseFactionReputationOnGrinding</code> to <code>false</code> in <code>ModSettings.xml</code>. By default, grinding grids belonging to other factions or NPCs causes a reputation penalty, matching manual grinding behaviour.
</div>
</details>

---

## Safe Zones

<details>
<summary>Does the system respect Safe Zones?</summary>
<div>
<p>Yes. The system checks Safe Zone rules before welding, grinding, or building projections:</p>
<ul>
  <li>Grinding enemies inside a Safe Zone you or your faction do not own is blocked.</li>
  <li>Grinding inside your own Safe Zone is allowed when the relevant Safe Zone option is enabled in the Safe Zone settings panel.</li>
</ul>
<p>These checks can be disabled in <code>ModSettings.xml</code> if needed.</p>
</div>
</details>

<details>
<summary>My grid is partially inside a Safe Zone and some blocks are not working. Why?</summary>
<div>
<p>When a grid straddles a Safe Zone boundary, Build and Repair blocks inside the zone have different permissions than those outside. The system automatically creates separate work groups for blocks inside and outside the Safe Zone, so each group has a coordinator that matches its permissions.</p>
<p>If all your blocks are inside a Safe Zone that blocks welding or grinding, they will be idle — this is correct behaviour. Move blocks outside the zone or adjust the Safe Zone settings to allow the operations you need.</p>
</div>
</details>

<details>
<summary>The system is welding or grinding a protected grid. How do I stop it?</summary>
<div>
If you are using the <strong>ALE PCU Transferrer</strong> plugin for Torch, use the <code>!protect</code> command on the grid. The system will detect the protection flags and skip it entirely. Use <code>!unprotect</code> to make the grid accessible again.
</div>
</details>

---

## Terminal & Priorities

<details>
<summary>How do the Weld and Grind priority lists work?</summary>
<div>
<p>The priority lists control which block types the system targets first. Each block class (e.g. Armor, Thrusters, Reactors) can be enabled or disabled and reordered using the <strong>Up</strong> and <strong>Down</strong> buttons in the terminal. The system processes enabled classes from top to bottom.</p>
<p>Use the <strong>Enable All</strong> and <strong>Disable All</strong> buttons to quickly toggle every entry at once.</p>
</div>
</details>

<details>
<summary>What does "Ignore Priority Order" do?</summary>
<div>
When enabled, the system ignores the order of the priority list and simply targets the nearest available block, regardless of its block class. The enabled/disabled state of each class is still respected — only the ordering is bypassed.
</div>
</details>

<details>
<summary>How do I reset a block back to its default settings?</summary>
<div>
Open the block's terminal and click <strong>Reset All Settings</strong>. This restores all settings for that block to their defaults, including the priority list states.
</div>
</details>

---

## Companion Script

<details>
<summary>Where can I find the companion auto-queuing script?</summary>
<div>
<p><strong>Important:</strong> the companion script only works with SKO's maintained versions of the mod. It will not work with the original mod by Dummy08.</p>
<p>A maintained version of the script is available on the Steam Workshop:
<a href="https://steamcommunity.com/sharedfiles/filedetails/?id=3472701905">Nanobot Build and Repair System Queuing / Display / Scripting (Maintained)</a></p>
<p>The script source is also included in this repository under <code>SKO-Nanobot-BuildAndRepair-System-Script/</code>.</p>
<p>See the <a href="../Companion-Script/">Companion Script documentation</a> for setup and configuration details.</p>
</div>
</details>

<details>
<summary>What does the companion script do and how do I set it up?</summary>
<div>
<p>The companion script runs on a Programmable Block and handles automatic assembler queuing and multi-display status output for one or more Build and Repair System groups. It can show current weld/grind targets, missing components, and transport status on LCD panels or cockpit screens.</p>
<p><strong>Setup:</strong></p>
<ol>
  <li>Place a Programmable Block on your grid.</li>
  <li>Open the script from the Steam Workshop link above and paste it into the Programmable Block editor.</li>
  <li>Edit the <code>BuildAndRepairSystemQueuingGroups</code> array at the top of the script to match your block and assembler names or group names.</li>
  <li>Compile and run the script. It updates automatically on every 100-tick cycle.</li>
</ol>
</div>
</details>

---

## Community & Support

<details>
<summary>Where can I get help or ask questions?</summary>
<div>
<p>Join the Discord server and ask in the <strong>#help-topics</strong> channel:
<a href="https://discord.gg/5XkQW5tdQM">https://discord.gg/5XkQW5tdQM</a></p>
<p>You can also open an issue on GitHub if you believe you have found a bug:
<a href="https://github.com/SKO85/SE-Mods/issues">https://github.com/SKO85/SE-Mods/issues</a></p>
</div>
</details>

<details>
<summary>Can I donate to support development?</summary>
<div>
Yes, donations are very welcome and appreciated:
<a href="https://www.paypal.com/paypalme/SKO85GAMING">Donate via PayPal</a>
</div>
</details>

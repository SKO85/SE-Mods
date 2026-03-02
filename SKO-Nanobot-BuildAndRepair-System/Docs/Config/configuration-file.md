# Configuration File

The mod can be configured through a `ModSettings.xml` file. All settings are optional — any value omitted from the file will use its default. The file is shared across all Build and Repair blocks on the server.

> **Note:** The server (or session) must be restarted after any change to `ModSettings.xml` for the new values to take effect.

---

## Creating the File

### Single Player

1. Load your world as normal.
2. Type the following command in chat:
   ```
   /nanobars -cwsf
   ```
3. A confirmation message appears in chat with the file path:

   <img width="611" height="70" alt="Chat confirmation message" src="https://github.com/user-attachments/assets/b433d5a6-2311-49d5-a679-0d1634574ce5" />

4. The file is created at one of these paths depending on which mod version you use:
   > `C:\Users\[USERNAME]\AppData\Roaming\SpaceEngineers\Saves\[######]\MyWorldName\Storage\SKO-Nanobot-BuildAndRepair-System_SKO-Nanobot-BuildAndRepair-System\ModSettings.xml`
   >
   > `C:\Users\[USERNAME]\AppData\Roaming\SpaceEngineers\Saves\[######]\MyWorldName\Storage\SKO-Nanobot-BuildAndRepair-System-Original_SKO-Nanobot-BuildAndRepair-System\ModSettings.xml`

5. Open the file in a text editor (e.g. Notepad++) and edit the values as needed.

   ![ModSettings.xml file shown in a text editor](https://github.com/user-attachments/assets/e0e51cbb-a9dd-43a3-9d71-b2c94bfbf0cd)

### Dedicated Server (Torch)

Place the `ModSettings.xml` file at:
```
[TorchPath]\Instance\Storage\<Nanobot-Mod-Folder>\ModSettings.xml
```

You can generate a base file from a local game using the command above, then copy and edit it for the server.

![Server folder example](https://github.com/user-attachments/assets/e36c2816-940d-46ef-87c5-ace114343d24)

---

## Settings Reference

### General Settings

| Setting | Type | Default | Min | Max | Description |
|---|---|---|---|---|---|
| `Range` | int | `100` m | `2` m | `2000` m | Operating range **radius** of each Build and Repair block. The actual working area diameter is twice this value (e.g. `100` → 200 m diameter). |
| `MaximumOffset` | int | `200` m | `0` m | `2000` m | Maximum offset distance that players can configure per block. |
| `MaxBackgroundTasks` | int | `4` | `1` | `10` | Number of background tasks the mod may run in parallel. Higher values can improve throughput but increase CPU load. |
| `MaximumRequiredElectricPowerStandby` | float | `0.05` MW | — | — | Power draw of each block while idle (standby). |
| `MaximumRequiredElectricPowerTransport` | float | `0.1` MW | — | — | Additional power draw while a block is transporting components. |

---

### Behaviour Settings

| Setting | Type | Default | Description |
|---|---|---|---|
| `SafeZoneCheckEnabled` | bool | `true` | When enabled, the system respects Safe Zone rules and will not weld, grind, or build inside zones where those actions are not permitted. |
| `ShieldCheckEnabled` | bool | `true` | When enabled, the system checks for the Shields mod and skips targets protected by an active shield. |
| `DecreaseFactionReputationOnGrinding` | bool | `true` | When enabled, grinding grids belonging to other factions or NPCs reduces your reputation with them, matching the behaviour of manual grinding. Set to `false` to disable reputation loss. |
| `DeleteBotsWhenDead` | bool | `true` | When enabled, bot corpses (Wolves, Spiders, etc.) are deleted after their inventory has been emptied. Set to `false` to leave them in the world. |
| `AssignToSystemEnabled` | bool | `true` | When enabled, the exclusive block-ownership mechanism is active — a block being worked on is locked to a specific Build and Repair system until the work is complete. Set to `false` to disable this behaviour. |

---

### System Limit Settings

| Setting | Type | Default | Description |
|---|---|---|---|
| `MaxSystemsPerTargetGrid` | int | `10` | Maximum number of Build and Repair systems that may simultaneously work on the same target grid. Prevents many systems piling onto a single grid while others nearby are ignored. |
| `DisableLimitSystemsPerTargetGrid` | bool | `false` | When set to `true`, the `MaxSystemsPerTargetGrid` limit is removed entirely and any number of systems may work on the same grid at once. |

---

### Sound & Visual Settings

| Setting | Type | Default | Description |
|---|---|---|---|
| `DisableTickingSound` | bool | `false` | When set to `true`, the ticking/unable sound is disabled globally for all blocks and all players. Players can also toggle this individually per block in the terminal, unless this global setting forces it off. |
| `DisableParticleEffects` | bool | `false` | When set to `true`, the flying nanobot trace animations (pick and deliver) are disabled globally for all blocks. Players can also toggle this per block in the terminal, unless this global setting forces it off. Welding and grinding sparks are unaffected. |

---

### Welder Sub-Settings (`Welder` section)

These settings are nested inside a `<Welder>` element in the XML.

#### Power

| Setting | Type | Default | Description |
|---|---|---|---|
| `MaximumRequiredElectricPowerWelding` | float | `0.2` MW | Power draw while actively welding. |
| `MaximumRequiredElectricPowerGrinding` | float | `0.2` MW | Power draw while actively grinding. |

#### Speed Multipliers

| Setting | Type | Default | Min | Max | Description |
|---|---|---|---|---|---|
| `WeldingMultiplier` | float | `1.0` | `0.001` | `1000` | Speed multiplier for welding. A value of `2.0` makes welding twice as fast. |
| `GrindingMultiplier` | float | `1.0` | `0.001` | `1000` | Speed multiplier for grinding. A value of `0.5` makes grinding half as fast. |

#### Search Modes

| Setting | Type | Default | Description |
|---|---|---|---|
| `AllowedSearchModes` | flags | `Grids, BoundingBox` | The search modes players are allowed to select. Possible values: `Grids`, `BoundingBox`. Combine with commas for multiple. |
| `SearchModeDefault` | enum | `Grids` | The default search mode applied to newly placed blocks. |

#### Work Modes

| Setting | Type | Default | Description |
|---|---|---|---|
| `AllowedWorkModes` | flags | All modes | The work modes players are allowed to select. Possible values: `WeldBeforeGrind`, `GrindBeforeWeld`, `GrindIfWeldGetStuck`, `WeldOnly`, `GrindOnly`. |
| `WorkModeDefault` | enum | `WeldBeforeGrind` | The default work mode applied to newly placed blocks. |

#### Lock Settings (prevent players from changing specific options)

| Setting | Type | Default | Description |
|---|---|---|---|
| `AllowBuildFixed` | bool | `false` | When `true`, players cannot change the Allow Build option. |
| `AllowBuildDefault` | bool | `true` | Default value for Allow Build on newly placed blocks. |
| `AreaSizeFixed` | bool | `false` | When `true`, players cannot adjust the working area size. |
| `AreaOffsetFixed` | bool | `false` | When `true`, players cannot adjust the working area offset. |
| `ShowAreaFixed` | bool | `false` | When `true`, players cannot toggle the area visualisation. |
| `PriorityFixed` | bool | `false` | When `true`, players cannot modify the weld or grind priority lists. |
| `CollectPriorityFixed` | bool | `false` | When `true`, players cannot modify the component collection priority list. |
| `ScriptControllFixed` | bool | `false` | When `true`, players cannot toggle Script Controlled mode. |
| `SoundVolumeFixed` | bool | `false` | When `true`, players cannot adjust the sound volume. |
| `SoundVolumeDefault` | float | `0.5` | Default sound volume for newly placed blocks (`0.0` = silent, `1.0` = full). |

#### Push / Collect Defaults

| Setting | Type | Default | Description |
|---|---|---|---|
| `PushIngotOreImmediatelyFixed` | bool | `false` | When `true`, players cannot change the Push Ingots/Ore Immediately option. |
| `PushIngotOreImmediatelyDefault` | bool | `true` | Default value for Push Ingots/Ore Immediately on newly placed blocks. |
| `PushComponentImmediatelyFixed` | bool | `false` | When `true`, players cannot change the Push Components Immediately option. |
| `PushComponentImmediatelyDefault` | bool | `true` | Default value for Push Components Immediately on newly placed blocks. |
| `PushItemsImmediatelyFixed` | bool | `false` | When `true`, players cannot change the Push Items Immediately option. |
| `PushItemsImmediatelyDefault` | bool | `true` | Default value for Push Items Immediately on newly placed blocks. |
| `CollectIfIdleFixed` | bool | `false` | When `true`, players cannot change the Collect If Idle option. |
| `CollectIfIdleDefault` | bool | `false` | Default value for Collect If Idle on newly placed blocks. |

#### Grind Janitor (Auto-Grind)

| Setting | Type | Default | Description |
|---|---|---|---|
| `AllowedGrindJanitorRelations` | flags | `NoOwnership, Enemies, Neutral` | Which ownership relations players are allowed to auto-grind. Possible values: `NoOwnership`, `Enemies`, `Neutral`. |
| `UseGrindJanitorFixed` | bool | `false` | When `true`, players cannot change the auto-grind relation setting. |
| `UseGrindJanitorDefault` | flags | `NoOwnership, Enemies` | Default auto-grind relation on newly placed blocks. |
| `GrindJanitorOptionsDefault` | flags | _(none)_ | Default grind janitor options on newly placed blocks. |

#### Color Settings

| Setting | Type | Default | Description |
|---|---|---|---|
| `UseIgnoreColorFixed` | bool | `false` | When `true`, players cannot change the ignore color setting. |
| `UseIgnoreColorDefault` | bool | `true` | Default value for Use Ignore Color on newly placed blocks. |
| `IgnoreColorDefault` | float[3] | `[321, 100, 51]` | Default ignore color as HSV values. |
| `UseGrindColorFixed` | bool | `false` | When `true`, players cannot change the grind color setting. |
| `UseGrindColorDefault` | bool | `true` | Default value for Use Grind Color on newly placed blocks. |
| `GrindColorDefault` | float[3] | `[321, 100, 50]` | Default grind color as HSV values. |

#### Visual Effects

| Setting | Type | Default | Description |
|---|---|---|---|
| `AllowedEffects` | flags | All effects enabled | Controls which visual and sound effects are permitted. Possible values: `WeldingVisualEffect`, `WeldingSoundEffect`, `GrindingVisualEffect`, `GrindingSoundEffect`, `TransportVisualEffect`. Remove a value to disable that effect globally for all blocks. |

---

## Example File

```xml
<?xml version="1.0" encoding="utf-8"?>
<SyncModSettings>
  <Range>100</Range>
  <MaximumOffset>200</MaximumOffset>
  <MaxBackgroundTasks>4</MaxBackgroundTasks>
  <SafeZoneCheckEnabled>true</SafeZoneCheckEnabled>
  <ShieldCheckEnabled>true</ShieldCheckEnabled>
  <DecreaseFactionReputationOnGrinding>true</DecreaseFactionReputationOnGrinding>
  <DeleteBotsWhenDead>true</DeleteBotsWhenDead>
  <DisableTickingSound>false</DisableTickingSound>
  <DisableParticleEffects>false</DisableParticleEffects>
  <AssignToSystemEnabled>true</AssignToSystemEnabled>
  <MaxSystemsPerTargetGrid>10</MaxSystemsPerTargetGrid>
  <DisableLimitSystemsPerTargetGrid>false</DisableLimitSystemsPerTargetGrid>
  <Welder>
    <WeldingMultiplier>1</WeldingMultiplier>
    <GrindingMultiplier>1</GrindingMultiplier>
    <AllowedWorkModes>WeldBeforeGrind GrindBeforeWeld GrindIfWeldGetStuck WeldOnly GrindOnly</AllowedWorkModes>
    <WorkModeDefault>WeldBeforeGrind</WorkModeDefault>
    <PriorityFixed>false</PriorityFixed>
    <AllowedEffects>WeldingVisualEffect WeldingSoundEffect GrindingVisualEffect GrindingSoundEffect TransportVisualEffect</AllowedEffects>
  </Welder>
</SyncModSettings>
```

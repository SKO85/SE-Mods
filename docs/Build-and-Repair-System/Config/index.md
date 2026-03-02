---
layout: default
title: Configuration File
parent: Build and Repair System
nav_order: 1
has_children: true
---

# Configuration File

The mod can be configured through a `ModSettings.xml` file. All settings are optional — any value omitted from the file will use its default. The file is shared across all Build and Repair blocks on the server.

> **Note:** The server (or session) must be restarted after any change to `ModSettings.xml` for the new values to take effect.

---

## Creating the File

### Single Player

1. Load your world as normal.
2. Type the following command in chat:
   ```plaintext
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
```plaintext
[TorchPath]\Instance\Storage\<Nanobot-Mod-Folder>\ModSettings.xml
```

You can generate a base file from a local game using the command above, then copy and edit it for the server.

![Server folder example](https://github.com/user-attachments/assets/e36c2816-940d-46ef-87c5-ace114343d24)

---

## Settings Reference

| Section | Description |
| --- | --- |
| [General Settings](general-settings/) | Range, power, background tasks, behaviour, system limits, sound and visuals |
| [Welder Settings](welder-settings/) | Power, speed multipliers, work modes, search modes, locks, push/collect, grind janitor, colors, effects |

---

## Example File

See [ModSettings.xml](ModSettings.xml) for a complete example showing all available settings with their default values.

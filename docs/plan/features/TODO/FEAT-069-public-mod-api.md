# FEAT-069: Public Mod API for initializing BaR settings from other mods

## Status: Proposed
## Priority: Medium
## Version: TBD

## Summary

Expose a small, versioned, cross-mod API (delegate-dictionary over `SendModMessage` / `RegisterMessageHandler`) so other mods — particularly mod packs — can read and seed BaR settings (power usage, multipliers, limits, etc.) from their own C# code at load time, without editing `ModSettings.xml`.

## Motivation

A mod pack developer wants to reference BaR and initialize defaults (e.g. power usage) from their mod's code so every server running the pack gets consistent values out of the box, without each server admin hand-editing `ModSettings.xml`. Today there is no such surface: the only ways to change settings are `/nanobars config set …` (chat, admin-only) and the XML file.

We already have the runtime plumbing — `Mod.Settings` is live-tunable, `Mod.SettingsChanged()` re-reads the power sink via the `SetRequiredInputFuncByType` callback (`NanobotSystem.Init.cs:63`), and `NetworkMessagingHandler.BroadcastModSettings()` syncs changes to clients. The missing piece is a stable public entry point other mods can call.

## Design

### Precedence (user-specified)

Settings are layered in this order, lowest to highest:

1. Hard-coded defaults (`SyncModSettings` constructor).
2. Mod pack initialization via `TryInitializeSetting(...)` — **accepted only when `Mod.CustomSettingsLoaded == false`** (i.e. no admin `ModSettings.xml` exists). If the admin file is present, mod pack initialization is rejected whole-object and admin wins.
3. Admin `ModSettings.xml`, loaded in `Mod.Init()` exactly as today.
4. Runtime overrides via `SetSetting(...)` (API) or `/nanobars config set …` (chat) — unconditional, apply at any time after init.

Mod packs that want forced values (not just defaults) use `SetSetting` instead of `TryInitializeSetting`. Admins who want mod-pack defaults to apply should simply not ship a `ModSettings.xml`.

**Persistence:** in-memory only. API calls never write `ModSettings.xml`.

### API surface (v1)

- **Channel:** a single fixed random `long` on `MyAPIGateway.Utilities.RegisterMessageHandler` (TBD at implementation time; verify no collision with known mod channels such as DefenseShields `1365616918`).
- **Handshake:** identical pattern to `Mods/ShieldApi.cs`, reversed direction — BaR is the producer.
  - On handler message `"ApiEndpointRequest"` (string), BaR responds with `SendModMessage(Channel, _endpoints)` where `_endpoints` is an `ImmutableDictionary<string, Delegate>`.
  - On `Mod.Init()` BaR also sends an unsolicited announcement so consumers registered earlier in the session get the dict immediately.
  - `HandleMessage` ignores its own echo (mirrors `ShieldApi.HandleMessage`).
- **Server-only enforcement:** every delegate short-circuits with an error string when `!MyAPIGateway.Session.IsServer`, matching the existing `Mod.SettingsValid = IsServer` contract (`Mod.cs:247`).

**Exposed delegates (7 entries):**

| Key | Signature | Behaviour |
|---|---|---|
| `GetApiVersion` | `Func<int>` | Returns `1`. Bump on breaking changes. |
| `GetSettingNames` | `Func<string[]>` | All names from the shared settings registry. |
| `GetSettingType` | `Func<string, string>` | Type label (`bool` / `int` / `float` / enum names) or `null`. |
| `GetSetting` | `Func<string, string>` | Current value as string, `null` if unknown. |
| `SetSetting` | `Func<string, string, string>` | Unconditional. Returns `null` on success, error string otherwise. Calls `Mod.SettingsChanged()` + `BroadcastModSettings()`. |
| `TryInitializeSetting` | `Func<string, string, string>` | Same as `SetSetting` but rejects with `"custom-config-present"` when `Mod.CustomSettingsLoaded == true`. |
| `HasCustomConfig` | `Func<bool>` | `Mod.CustomSettingsLoaded`, so callers can decide how to behave. |

### Shared settings registry refactor

The `SettingEntry` class, `_entries` array, `_lookup` dictionary, and `BoolSetting/IntSetting/FloatSetting/EnumSetting` factory helpers currently live inside `Chat/Commands/ConfigCommand.cs` (lines 11–100, 297–368). Extract them into a new static `Models/SettingsRegistry` so both chat commands and the mod API share one source of truth.

The registry must also include the four power settings that are currently **not** exposed to chat commands but are already runtime-tunable via `Mod.SettingsChanged()`:

- `MaximumRequiredElectricPowerStandby` → `Mod.Settings.MaximumRequiredElectricPowerStandby`
- `MaximumRequiredElectricPowerTransport` → `Mod.Settings.MaximumRequiredElectricPowerTransport`
- `MaximumRequiredElectricPowerWelding` → `Mod.Settings.Welder.MaximumRequiredElectricPowerWelding`
- `MaximumRequiredElectricPowerGrinding` → `Mod.Settings.Welder.MaximumRequiredElectricPowerGrinding`

Side effect: `/nanobars config list` will start showing these four settings. Intentional — they were always tunable, just not discoverable.

### Consumer drop-in sample

Ship a ready-to-copy `BarModApi.cs` in `docs/pages/Build-and-Repair-System/ModAPI/` that other mod devs can drop into their project, modelled on `Mods/ShieldApi.cs` (register handler, send `"ApiEndpointRequest"` on load, cast delegates on first reply, expose typed wrapper methods). Include an example header comment showing `api.TryInitializeFloat("MaximumRequiredElectricPowerWelding", 0.5f)` from the consumer mod's `Init()`.

### Constraints

- **C# 6 only** (project `LangVersion`): no pattern matching, no tuples, no `out var`, no local functions.
- **No `System.Threading` / `System.Reflection`**: API uses string-typed getter/setter delegates over existing `SettingEntry.Get()` / `.Set(string)`.
- **Message IDs**: API uses its own long channel via `MyAPIGateway.Utilities.SendModMessage` — **not** the `NetworkMessagingHandler` 40000–40104 range — so there is no collision with existing BaR network messages.

## Files Affected

- **New:** `SKO-Nanobot-BuildAndRepair-System/Data/Scripts/SKO-Nanobot-BuildAndRepair-System/Models/SettingsRegistry.cs` — `SettingEntry`, registry array, lookup, factory helpers, + 4 new power entries.
- **New:** `SKO-Nanobot-BuildAndRepair-System/Data/Scripts/SKO-Nanobot-BuildAndRepair-System/Mods/BarModApi.cs` — handler registration, `_endpoints` dict, per-delegate bodies, `Register()` / `Unregister()`.
- `SKO-Nanobot-BuildAndRepair-System/Data/Scripts/SKO-Nanobot-BuildAndRepair-System/Chat/Commands/ConfigCommand.cs` — strip the embedded registry, delegate to `SettingsRegistry.TryGet` / `SettingsRegistry.All` from `ExecuteSet`/`Get`/`List`.
- `SKO-Nanobot-BuildAndRepair-System/Data/Scripts/SKO-Nanobot-BuildAndRepair-System/Mod.cs` — call `BarModApi.Register()` in `Init()` after `NetworkMessagingHandler` is up, `BarModApi.Unregister()` in `UnloadData()`.
- **New:** `docs/pages/Build-and-Repair-System/ModAPI/BarModApi.cs` — drop-in consumer sample.
- **New:** `docs/pages/Build-and-Repair-System/ModAPI/README.md` — short docs: channel id, key list, precedence model, server-only caveat, usage example.
- `docs/plan/state.md` — add FEAT-069 row.

## Testing

1. **Build:** `dotnet build SKO-SE-Mods.sln -c Release -v minimal`. No new warnings.
2. **Chat regression:** SP world, Testing variant. `/nanobars config list` shows the existing 22 entries **plus** the 4 `MaximumRequiredElectricPower*` entries. `/nanobars config set MaximumRequiredElectricPowerWelding 0.5` reports success and a placed BaR's power usage updates live in its terminal info panel.
3. **ModAPI smoke test:** throwaway test mod in a separate `Testing` folder that uses the sample `BarModApi.cs`.
   - On a fresh world (no `ModSettings.xml`): `api.TryInitializeFloat("MaximumRequiredElectricPowerWelding", 0.5f)` returns `null` (success) and `/nanobars config get MaximumRequiredElectricPowerWelding` returns `0.50`.
   - After `/nanobars config save` + session restart: the same call returns `"custom-config-present"` and the previous value is preserved. `api.Set("WeldingMultiplier", "2.0")` still succeeds unconditionally.
4. **Unknown / invalid input:** `api.Set("NonExistentSetting", "5")` returns `"unknown-setting"`; `api.Set("Range", "notanumber")` returns the validator error from `IntSetting.Set`.
5. **Client sync:** DS + remote client. After a server-side `SetSetting`, the custom info panel of a placed BaR on the client reflects the new power number within ~1–2 s (validates `BroadcastModSettings` + per-BaR `SettingsChanged()`).
6. **Unregister:** quit to main menu — no unload exceptions logged.

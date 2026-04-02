using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Utils;
using System;
using System.Collections.Generic;
using System.Text;

namespace SKONanobotBuildAndRepairSystem.Chat.Commands
{
    public static class ConfigCommand
    {
        private sealed class SettingEntry
        {
            public string Name;
            public string TypeLabel;
            public Func<string> Get;
            public Func<string, string> Set; // returns null on success, error message on failure
        }

        private static SettingEntry[] _entries;
        private static Dictionary<string, SettingEntry> _lookup;

        private static void EnsureInitialized()
        {
            if (_entries != null) return;

            _entries = new SettingEntry[]
            {
                BoolSetting("DebugMode",
                    () => Mod.Settings.DebugMode,
                    v => { Mod.Settings.DebugMode = v; }),
                EnumSetting("LogLevel",
                    () => Mod.Settings.LogLevel.ToString(),
                    s => {
                        try { Mod.Settings.LogLevel = (Logging.Level)Enum.Parse(typeof(Logging.Level), s, true); return null; }
                        catch { return string.Format("Expected one of: {0}", string.Join(", ", Enum.GetNames(typeof(Logging.Level)))); }
                    },
                    string.Join("|", Enum.GetNames(typeof(Logging.Level)))),
                IntSetting("Range",
                    () => Mod.Settings.Range,
                    v => { Mod.Settings.Range = v; }, 1, 1000),
                IntSetting("MaximumOffset",
                    () => Mod.Settings.MaximumOffset,
                    v => { Mod.Settings.MaximumOffset = v; }, 0, 1000),
                IntSetting("MaxBackgroundTasks",
                    () => Mod.Settings.MaxBackgroundTasks,
                    v => { Mod.Settings.MaxBackgroundTasks = v; }, 1, 10),
                IntSetting("MaxSystemsPerTargetGrid",
                    () => Mod.Settings.MaxSystemsPerTargetGrid,
                    v => { Mod.Settings.MaxSystemsPerTargetGrid = v; }, 1, 100),
                BoolSetting("AssignToSystemEnabled",
                    () => Mod.Settings.AssignToSystemEnabled,
                    v => { Mod.Settings.AssignToSystemEnabled = v; }),
                BoolSetting("DisableLimitSystemsPerTargetGrid",
                    () => Mod.Settings.DisableLimitSystemsPerTargetGrid,
                    v => { Mod.Settings.DisableLimitSystemsPerTargetGrid = v; }),
                BoolSetting("SafeZoneCheckEnabled",
                    () => Mod.Settings.SafeZoneCheckEnabled,
                    v => { Mod.Settings.SafeZoneCheckEnabled = v; }),
                BoolSetting("ShieldCheckEnabled",
                    () => Mod.Settings.ShieldCheckEnabled,
                    v => { Mod.Settings.ShieldCheckEnabled = v; }),
                BoolSetting("DecreaseFactionReputationOnGrinding",
                    () => Mod.Settings.DecreaseFactionReputationOnGrinding,
                    v => { Mod.Settings.DecreaseFactionReputationOnGrinding = v; }),
                BoolSetting("DeleteBotsWhenDead",
                    () => Mod.Settings.DeleteBotsWhenDead,
                    v => { Mod.Settings.DeleteBotsWhenDead = v; }),
                BoolSetting("DisableTickingSound",
                    () => Mod.Settings.DisableTickingSound,
                    v => { Mod.Settings.DisableTickingSound = v; }),
                BoolSetting("DisableParticleEffects",
                    () => Mod.Settings.DisableParticleEffects,
                    v => { Mod.Settings.DisableParticleEffects = v; }),
                IntSetting("EmptyGridRescanDelaySeconds",
                    () => Mod.Settings.EmptyGridRescanDelaySeconds,
                    v => { Mod.Settings.EmptyGridRescanDelaySeconds = v; }, 0, 300),
                IntSetting("StaggerGroupCount",
                    () => Mod.Settings.StaggerGroupCount,
                    v => { Mod.Settings.StaggerGroupCount = v; }, 0, 10),
                IntSetting("MaxGrindsPerTick",
                    () => Mod.Settings.MaxGrindsPerTick,
                    v => { Mod.Settings.MaxGrindsPerTick = v; }, 0, 100),
                IntSetting("AssignmentTtlSeconds",
                    () => Mod.Settings.AssignmentTtlSeconds,
                    v => { Mod.Settings.AssignmentTtlSeconds = v; }, 2, 30),
                FloatSetting("WeldingMultiplier",
                    () => Mod.Settings.Welder.WeldingMultiplier,
                    v => { Mod.Settings.Welder.WeldingMultiplier = v; }, 0.1f, 100f),
                FloatSetting("GrindingMultiplier",
                    () => Mod.Settings.Welder.GrindingMultiplier,
                    v => { Mod.Settings.Welder.GrindingMultiplier = v; }, 0.1f, 100f),
                IntSetting("WorkSpeed",
                    () => Mod.Settings.Welder.WorkSpeed,
                    v => { Mod.Settings.Welder.WorkSpeed = v; }, 1, 10),
            };

            _lookup = new Dictionary<string, SettingEntry>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _entries.Length; i++)
                _lookup[_entries[i].Name] = _entries[i];
        }

        public static ChatCommandResult Execute(string[] args)
        {
            EnsureInitialized();
            if (args.Length < 2)
                return ShowHelp();

            switch (args[1])
            {
                case "set":
                    return ExecuteSet(args);
                case "get":
                    return ExecuteGet(args);
                case "list":
                    return ExecuteList();
                case "save":
                case "create":
                    return ExecuteSave();
                case "reload":
                    return ExecuteReload();
                case "reset":
                    return ExecuteReset();
                case "delete":
                    return ExecuteDelete();
                default:
                    return ShowHelp();
            }
        }

        public static ChatCommandResult ShowHelp()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Config Commands (admin-only, server-side):");
            sb.AppendLine();
            sb.AppendLine("/nanobars config list");
            sb.AppendLine("  Lists all configurable settings with current values.");
            sb.AppendLine();
            sb.AppendLine("/nanobars config get <setting>");
            sb.AppendLine("  Gets the current value of a setting.");
            sb.AppendLine();
            sb.AppendLine("/nanobars config set <setting> <value>");
            sb.AppendLine("  Sets a setting to the specified value.");
            sb.AppendLine();
            sb.AppendLine("/nanobars config save  (or: config create)");
            sb.AppendLine("  Saves current settings to the world folder (ModSettings.xml).");
            sb.AppendLine("  Creates the file if it doesn't exist yet.");
            sb.AppendLine();
            sb.AppendLine("/nanobars config reload");
            sb.AppendLine("  Reloads settings from ModSettings.xml (world or local storage).");
            sb.AppendLine();
            sb.AppendLine("/nanobars config reset");
            sb.AppendLine("  Resets all settings to defaults (keeps ModSettings.xml).");
            sb.AppendLine();
            sb.AppendLine("/nanobars config delete");
            sb.AppendLine("  Resets all settings to defaults and deletes ModSettings.xml.");
            sb.AppendLine();
            sb.AppendLine("Examples:");
            sb.AppendLine("  /nanobars config set DebugMode true");
            sb.AppendLine("  /nanobars config set MaxGrindsPerTick 20");
            sb.AppendLine("  /nanobars config set StaggerGroupCount 2");
            sb.AppendLine("  /nanobars config get Range");

            return ChatCommandResult.MissionScreen(sb.ToString(), "Nanobot Build and Repair System", "Config Help");
        }

        private static ChatCommandResult ExecuteSet(string[] args)
        {
            if (args.Length < 4)
                return ChatCommandResult.Error("Usage: /nanobars config set <setting> <value>");

            SettingEntry entry;
            if (!_lookup.TryGetValue(args[2], out entry))
                return ChatCommandResult.Error(string.Format("Unknown setting: {0}. Use '/nanobars config list' to see available settings.", args[2]));

            var error = entry.Set(args[3]);
            if (error != null)
                return ChatCommandResult.Error(string.Format("Invalid value for {0}: {1}", entry.Name, error));

            Mod.SettingsChanged();
            Handlers.NetworkMessagingHandler.BroadcastModSettings();
            return ChatCommandResult.Success(string.Format("{0} = {1}", entry.Name, entry.Get()));
        }

        private static ChatCommandResult ExecuteGet(string[] args)
        {
            if (args.Length < 3)
                return ChatCommandResult.Error("Usage: /nanobars config get <setting>");

            SettingEntry entry;
            if (!_lookup.TryGetValue(args[2], out entry))
                return ChatCommandResult.Error(string.Format("Unknown setting: {0}. Use '/nanobars config list' to see available settings.", args[2]));

            return ChatCommandResult.Success(string.Format("{0} = {1}", entry.Name, entry.Get()));
        }

        private static ChatCommandResult ExecuteList()
        {
            EnsureInitialized();
            var sb = new StringBuilder();
            sb.AppendLine("Configurable Settings (current values):");
            sb.AppendLine();

            for (int i = 0; i < _entries.Length; i++)
            {
                var e = _entries[i];
                sb.AppendLine(string.Format("  {0} = {1}  ({2})", e.Name, e.Get(), e.TypeLabel));
            }

            return ChatCommandResult.MissionScreen(sb.ToString(), "Nanobot Build and Repair System", "Config Settings");
        }

        private static string GetSettingsFilePath()
        {
            try
            {
                var worldPath = Sandbox.ModAPI.MyAPIGateway.Session.CurrentPath;
                if (!string.IsNullOrEmpty(worldPath))
                    return worldPath + "\\Storage\\" + typeof(SyncModSettings).Name + "\\ModSettings.xml";
            }
            catch { }
            return "ModSettings.xml (world storage)";
        }

        private static ChatCommandResult ExecuteSave()
        {
            SyncModSettings.Save(Mod.Settings, true);
            Mod.CustomSettingsLoaded = true;
            return ChatCommandResult.Success(string.Format("Settings saved to: {0}", GetSettingsFilePath()));
        }

        private static ChatCommandResult ExecuteReload()
        {
            var loaded = SyncModSettings.Load();
            if (loaded == null)
                return ChatCommandResult.Error("Failed to load settings. Check server log for details.");

            Mod.Settings = loaded;
            Mod.SettingsChanged();
            Handlers.NetworkMessagingHandler.BroadcastModSettings();
            return ChatCommandResult.Success(string.Format("Settings reloaded from: {0}", GetSettingsFilePath()));
        }

        private static SyncModSettings CreateDefaults()
        {
            var defaults = new SyncModSettings();
            if (Sandbox.ModAPI.MyAPIGateway.Multiplayer != null && Sandbox.ModAPI.MyAPIGateway.Multiplayer.MultiplayerActive)
                defaults.MaxSystemsPerTargetGrid = 10;
            else
                defaults.MaxSystemsPerTargetGrid = 20;
            return defaults;
        }

        private static ChatCommandResult ExecuteReset()
        {
            Mod.Settings = CreateDefaults();
            Mod.SettingsChanged();
            Handlers.NetworkMessagingHandler.BroadcastModSettings();
            return ChatCommandResult.Success("Settings reset to defaults. A session/server restart is recommended for all changes to take full effect.");
        }

        private static ChatCommandResult ExecuteDelete()
        {
            // Reset settings to defaults.
            Mod.Settings = CreateDefaults();
            Mod.CustomSettingsLoaded = false;
            Mod.SettingsChanged();
            Handlers.NetworkMessagingHandler.BroadcastModSettings();

            // Delete ModSettings.xml from world and local storage.
            var deleted = false;
            try
            {
                if (Sandbox.ModAPI.MyAPIGateway.Utilities.FileExistsInWorldStorage("ModSettings.xml", typeof(SyncModSettings)))
                {
                    Sandbox.ModAPI.MyAPIGateway.Utilities.DeleteFileInWorldStorage("ModSettings.xml", typeof(SyncModSettings));
                    deleted = true;
                }
            }
            catch { }
            try
            {
                if (Sandbox.ModAPI.MyAPIGateway.Utilities.FileExistsInLocalStorage("ModSettings.xml", typeof(SyncModSettings)))
                {
                    Sandbox.ModAPI.MyAPIGateway.Utilities.DeleteFileInLocalStorage("ModSettings.xml", typeof(SyncModSettings));
                    deleted = true;
                }
            }
            catch { }

            var restart = " A session/server restart is recommended for all changes to take full effect.";
            if (deleted)
                return ChatCommandResult.Success(string.Format("Settings reset to defaults. Deleted: {0}.{1}", GetSettingsFilePath(), restart));

            return ChatCommandResult.Success("Settings reset to defaults. No ModSettings.xml file found to delete." + restart);
        }

        #region Setting builders

        private static SettingEntry BoolSetting(string name, Func<bool> getter, Action<bool> setter)
        {
            return new SettingEntry
            {
                Name = name,
                TypeLabel = "bool",
                Get = () => getter().ToString(),
                Set = s =>
                {
                    bool v;
                    if (!bool.TryParse(s, out v)) return "Expected 'true' or 'false'";
                    setter(v);
                    return null;
                }
            };
        }

        private static SettingEntry IntSetting(string name, Func<int> getter, Action<int> setter, int min = int.MinValue, int max = int.MaxValue)
        {
            var label = (min != int.MinValue || max != int.MaxValue)
                ? string.Format("int, {0}..{1}", min, max)
                : "int";
            return new SettingEntry
            {
                Name = name,
                TypeLabel = label,
                Get = () => getter().ToString(),
                Set = s =>
                {
                    int v;
                    if (!int.TryParse(s, out v)) return "Expected an integer";
                    if (v < min || v > max) return string.Format("Value must be between {0} and {1}", min, max);
                    setter(v);
                    return null;
                }
            };
        }

        private static SettingEntry FloatSetting(string name, Func<float> getter, Action<float> setter, float min = float.MinValue, float max = float.MaxValue)
        {
            var label = (min != float.MinValue || max != float.MaxValue)
                ? string.Format("float, {0}..{1}", min, max)
                : "float";
            return new SettingEntry
            {
                Name = name,
                TypeLabel = label,
                Get = () => getter().ToString("F2"),
                Set = s =>
                {
                    float v;
                    if (!float.TryParse(s, out v)) return "Expected a number";
                    if (v < min || v > max) return string.Format("Value must be between {0} and {1}", min, max);
                    setter(v);
                    return null;
                }
            };
        }

        private static SettingEntry EnumSetting(string name, Func<string> getter, Func<string, string> setter, string typeLabel)
        {
            return new SettingEntry
            {
                Name = name,
                TypeLabel = typeLabel,
                Get = getter,
                Set = setter
            };
        }

        #endregion
    }
}

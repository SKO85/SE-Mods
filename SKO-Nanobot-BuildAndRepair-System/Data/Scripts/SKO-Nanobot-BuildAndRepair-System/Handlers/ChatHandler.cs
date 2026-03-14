using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Profiling;
using System;
using System.Text;

namespace SKONanobotBuildAndRepairSystem.Handlers
{
    public class ChatHandler
    {
        private const string CmdKey = "/nanobars";
        private const string CmdKeyAlias = "/nanoboars";
        private const string CmdHelp = "-help";
        private const string CmdCwsf = "-cwsf";
        private const string CmdProfile = "profile";
        private const string CmdProfileStart = "start";
        private const string CmdProfileStop = "stop";
        private const string CmdProfileStatus = "status";
        private const string CmdProfileHelp = "help";
        private const string CmdProfileMinDuration = "minduration";
        private const string CmdSim = "sim";
        private const string CmdSimReset = "reset";

        #region Registration

        private static bool _registered = false;
        private static bool _chatHandlerRegistered = false;

        public static void Register()
        {
            if (_registered || MyAPIGateway.Utilities == null)
                return;

            // Register event for messages entered in the chat.
            if (!_chatHandlerRegistered)
            {
                MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
                _chatHandlerRegistered = true;
            }

            _registered = true;
        }

        public static void Unregister()
        {
            if (!_registered)
                return;

            try
            {
                // Unregister event for handling messages entered in the chat.
                if (_chatHandlerRegistered)
                {
                    MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
                    _chatHandlerRegistered = false;
                }
            }
            catch (Exception)
            {
            }

            _registered = false;
        }

        #endregion Registration

        private static void OnMessageEntered(string messageText, ref bool sendToOthers)
        {
            if (string.IsNullOrWhiteSpace(messageText)) return;
            var cmd = messageText.ToLower();
            if (!cmd.StartsWith(CmdKey) && !cmd.StartsWith(CmdKeyAlias)) return;

            sendToOthers = false;
            var keyLength = cmd.StartsWith(CmdKeyAlias) ? CmdKeyAlias.Length : CmdKey.Length;
            var args = cmd.Remove(0, keyLength).Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var console = MyAPIGateway.Utilities;

            if (args.Length == 0 || args[0] == CmdHelp)
            {
                ShowHelp();
                return;
            }

            switch (args[0])
            {
                case CmdCwsf:
                    if (MyAPIGateway.Session.IsServer)
                    {
                        SyncModSettings.Save(Mod.Settings, true);
                        console.ShowMessage("Nanobars", "Settings saved to world folder. Filename: ModSettings.xml");
                    }
                    else
                    {
                        console.ShowMessage("Nanobars", "Command not allowed on client");
                    }
                    break;

                case CmdSim:
                    if (!IsAdminOnServer(console)) break;

                    if (args.Length < 2)
                    {
                        // Show current setting
                        if (Mod.SimSpeedOverride.HasValue)
                            console.ShowMessage("Nanobars", string.Format("Sim-speed override: {0:F2} (real: {1:F2})",
                                Mod.SimSpeedOverride.Value,
                                MyAPIGateway.Physics != null ? MyAPIGateway.Physics.ServerSimulationRatio : 1.0f));
                        else
                            console.ShowMessage("Nanobars", string.Format("Sim-speed override: off (real: {0:F2})",
                                MyAPIGateway.Physics != null ? MyAPIGateway.Physics.ServerSimulationRatio : 1.0f));
                        break;
                    }

                    if (args[1] == CmdSimReset)
                    {
                        Mod.SimSpeedOverride = null;
                        console.ShowMessage("Nanobars", "Sim-speed override removed. Using server sim-speed.");
                        break;
                    }

                    float simValue;
                    if (!float.TryParse(args[1], out simValue))
                    {
                        console.ShowMessage("Nanobars", "Invalid value. Usage: /nanobars sim <0.1-1.0|reset>");
                        break;
                    }

                    if (simValue < 0.1f || simValue > 1.0f)
                    {
                        console.ShowMessage("Nanobars", "Value must be between 0.1 and 1.0");
                        break;
                    }

                    Mod.SimSpeedOverride = simValue;
                    console.ShowMessage("Nanobars", string.Format("Sim-speed override set to {0:F2}", simValue));
                    break;

                case CmdProfile:
                    if (!IsAdminOnServer(console)) break;

                    if (args.Length < 2)
                    {
                        ShowProfileHelp(console);
                        break;
                    }

                    if (args[1] == CmdProfileStart)
                    {
                        var autoStopSeconds = MethodProfiler.DefaultAutoStopDurationSeconds;
                        if (args.Length >= 3 && !int.TryParse(args[2], out autoStopSeconds))
                        {
                            console.ShowMessage("Nanobars", "Invalid seconds. Usage: /nanobars profile start [seconds] [minDurationMs]");
                            break;
                        }

                        if (args.Length >= 4)
                        {
                            int minDuration;
                            if (!int.TryParse(args[3], out minDuration))
                            {
                                console.ShowMessage("Nanobars", "Invalid minDurationMs. Usage: /nanobars profile start [seconds] [minDurationMs]");
                                break;
                            }

                            string minDurMsg;
                            if (!MethodProfiler.SetMinDurationMs(minDuration, out minDurMsg))
                            {
                                console.ShowMessage("Nanobars", minDurMsg);
                                break;
                            }
                        }

                        string message;
                        MethodProfiler.StartSession(autoStopSeconds, out message);
                        console.ShowMessage("Nanobars", message + string.Format(" (MinDurationMs={0})", MethodProfiler.MinDurationMs));
                    }
                    else if (args[1] == CmdProfileStop)
                    {
                        string message;
                        MethodProfiler.StopSession(out message);
                        console.ShowMessage("Nanobars", message);
                    }
                    else if (args[1] == CmdProfileStatus)
                    {
                        console.ShowMessage("Nanobars", MethodProfiler.GetStatusMessage());
                    }
                    else if (args[1] == CmdProfileMinDuration)
                    {
                        if (args.Length < 3)
                        {
                            console.ShowMessage("Nanobars", string.Format("Current MinDurationMs={0}. Usage: /nanobars profile minduration <ms>", MethodProfiler.MinDurationMs));
                            break;
                        }

                        int minDuration;
                        if (!int.TryParse(args[2], out minDuration))
                        {
                            console.ShowMessage("Nanobars", "Invalid value. Usage: /nanobars profile minduration <ms>");
                            break;
                        }

                        string minDurMsg;
                        MethodProfiler.SetMinDurationMs(minDuration, out minDurMsg);
                        console.ShowMessage("Nanobars", minDurMsg);
                    }
                    else if (args[1] == CmdProfileHelp)
                    {
                        ShowProfileHelp(console);
                    }
                    else
                    {
                        ShowProfileHelp(console);
                    }
                    break;

                default:
                    ShowHelp();
                    break;
            }
        }

        private static bool IsAdminOnServer(VRage.Game.ModAPI.IMyUtilities console)
        {
            if (!MyAPIGateway.Session.IsServer)
            {
                console.ShowMessage("Nanobars", "Command not allowed on client");
                return false;
            }

            var player = MyAPIGateway.Session.Player;
            if (player != null)
            {
                var promoteLevel = player.PromoteLevel.ToString();
                var isAdmin = promoteLevel == "Admin" || promoteLevel == "SpaceMaster" || promoteLevel == "Owner";
                if (!isAdmin)
                {
                    console.ShowMessage("Nanobars", "Command requires admin permissions");
                    return false;
                }
            }

            return true;
        }

        private static void ShowHelp()
        {
            var sb = new StringBuilder();

            sb.AppendLine($"Version: {Constants.ModVersion}");
            sb.AppendLine();
            sb.AppendLine($"[-cwsf]: Creates a settings file inside your current world folder (local-only)");
            sb.AppendLine($"[profile help]: Shows profiling command syntax");
            sb.AppendLine();
            sb.AppendLine($"Issues: Report issues or suggestions (GitHub)");
            sb.AppendLine($"https://github.com/SKO85/SE-Mods/issues");
            sb.AppendLine();
            sb.AppendLine($"WIKI / Documentation (GitHub)");
            sb.AppendLine($"https://github.com/SKO85/SE-Mods/wiki");
            sb.AppendLine();
            sb.AppendLine($"FAQ / Troubleshooting (GitHub)");
            sb.AppendLine($"https://github.com/SKO85/SE-Mods/wiki/FAQ---Frequently-Asked-Questions");
            sb.AppendLine();
            sb.AppendLine($"Discord: Contact Developer via Discord");
            sb.AppendLine($"https://discord.gg/5XkQW5tdQM");
            sb.AppendLine();
            sb.AppendLine($"Have fun!\nSKO85");

            MyAPIGateway.Utilities.ShowMissionScreen("Nanobot Build and Repair System", "Help", "", sb.ToString());
        }

        private static void ShowProfileHelp(VRage.Game.ModAPI.IMyUtilities console)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Profiling Commands (admin-only, server-side):");
            sb.AppendLine();
            sb.AppendLine("/nanobars profile start [seconds] [minDurationMs]");
            sb.AppendLine($"  Starts a profiling session. Defaults: seconds={MethodProfiler.DefaultAutoStopDurationSeconds}, minDurationMs={MethodProfiler.MinDurationMs}");
            sb.AppendLine("  Use 0 for seconds to disable auto-stop.");
            sb.AppendLine();
            sb.AppendLine("/nanobars profile stop");
            sb.AppendLine("  Stops the current profiling session and writes summary.");
            sb.AppendLine();
            sb.AppendLine("/nanobars profile status");
            sb.AppendLine("  Shows whether profiling is running and current settings.");
            sb.AppendLine();
            sb.AppendLine("/nanobars profile minduration <ms>");
            sb.AppendLine("  Sets the minimum method duration (0-10000ms) for logging samples.");
            sb.AppendLine("  Only calls taking >= this threshold are written to log files.");
            sb.AppendLine();
            sb.AppendLine("/nanobars profile help");
            sb.AppendLine("  Shows this help.");

            MyAPIGateway.Utilities.ShowMissionScreen("Nanobot Build and Repair System", "Profile Help", "", sb.ToString());
        }
    }
}
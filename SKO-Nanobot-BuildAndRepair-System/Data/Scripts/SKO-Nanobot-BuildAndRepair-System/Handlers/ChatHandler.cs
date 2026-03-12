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

                case CmdProfile:
                    if (!MyAPIGateway.Session.IsServer)
                    {
                        console.ShowMessage("Nanobars", "Command not allowed on client");
                        break;
                    }

                    var player = MyAPIGateway.Session.Player;
                    if (player != null)
                    {
                        var promoteLevel = player.PromoteLevel.ToString();
                        var isAdmin = promoteLevel == "Admin" || promoteLevel == "SpaceMaster" || promoteLevel == "Owner";
                        if (!isAdmin)
                        {
                            console.ShowMessage("Nanobars", "Command requires admin permissions");
                            break;
                        }
                    }

                    if (!MethodProfiler.IsConfigured)
                    {
                        console.ShowMessage("Nanobars", "Profiling is not enabled in ModSettings.xml (EnableMethodProfiling=false).");
                        break;
                    }

                    if (args.Length < 2)
                    {
                        console.ShowMessage("Nanobars", "Usage: /nanobars profile start [seconds]|stop|status");
                        break;
                    }

                    if (args[1] == CmdProfileStart)
                    {
                        var autoStopSeconds = MethodProfiler.DefaultAutoStopDurationSeconds;
                        if (args.Length >= 3 && !int.TryParse(args[2], out autoStopSeconds))
                        {
                            console.ShowMessage("Nanobars", "Invalid seconds. Usage: /nanobars profile start [seconds]");
                            break;
                        }

                        string message;
                        MethodProfiler.StartSession(autoStopSeconds, out message);
                        console.ShowMessage("Nanobars", message);
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
                    else
                    {
                        console.ShowMessage("Nanobars", "Usage: /nanobars profile start [seconds]|stop|status");
                    }
                    break;

                default:
                    ShowHelp();
                    break;
            }
        }

        private static void ShowHelp()
        {
            var sb = new StringBuilder();

            sb.AppendLine($"Version: {Constants.ModVersion}");
            sb.AppendLine();
            sb.AppendLine($"[-cwsf]: Creates a settings file inside your current world folder (local-only)");
            sb.AppendLine($"[profile start [seconds]]: Starts profiling (defaults to auto-stop after {MethodProfiler.DefaultAutoStopDurationSeconds}s)");
            sb.AppendLine($"[profile stop]: Stops profiling and closes profiler files");
            sb.AppendLine($"[profile status]: Shows whether profiling is enabled/running and current settings");
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
    }
}
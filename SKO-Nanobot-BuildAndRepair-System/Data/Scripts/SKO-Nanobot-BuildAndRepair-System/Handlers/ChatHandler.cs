using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Models;
using System;
using System.Text;

namespace SKONanobotBuildAndRepairSystem.Handlers
{
    public class ChatHandler
    {
        private const string CmdKey = "/nanobars";
        private const string CmdHelp = "-help";
        private const string CmdCwsf = "-cwsf";

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
            if (!cmd.StartsWith(CmdKey)) return;

            sendToOthers = false;
            var args = cmd.Remove(0, CmdKey.Length).Trim().Split(' ');
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
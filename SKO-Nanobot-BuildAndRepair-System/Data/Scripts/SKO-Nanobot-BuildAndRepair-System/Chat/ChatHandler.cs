using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Chat.Commands;
using SKONanobotBuildAndRepairSystem.Handlers;
using System;

namespace SKONanobotBuildAndRepairSystem.Chat
{
    public class ChatHandler
    {
        private const string CmdKey = "/nanobars";
        private const string CmdKeyAlias = "/nanoboars";

        #region Registration

        private static bool _registered = false;
        private static bool _chatHandlerRegistered = false;

        public static void Register()
        {
            if (_registered || MyAPIGateway.Utilities == null)
                return;

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

            // Client-side display commands (no server roundtrip needed)
            if (args.Length == 0 || args[0] == "-help")
            {
                var isAdmin = IsAdmin();
                ShowResult(HelpCommand.Execute(isAdmin));
                return;
            }

            if (args[0] == "profile" && (args.Length < 2 || args[1] == "help"))
            {
                ShowResult(ProfileCommand.ShowHelp());
                return;
            }

            // "profile summary" is a local HUD toggle — don't forward to server
            if (args[0] == "profile" && args.Length >= 2 && args[1] == "summary")
            {
                if (!IsLocalAdmin(console)) return;
                var enabled = HudHandler.ToggleProfileSummary();
                console.ShowMessage("Nanobars", enabled
                    ? "Profile summary HUD enabled (top-right). Use the same command to toggle off."
                    : "Profile summary HUD disabled.");
                return;
            }

            if (args[0] == "config" && (args.Length < 2 || args[1] == "help"))
            {
                ShowResult(ConfigCommand.ShowHelp());
                return;
            }

            if (args[0] == "mods")
            {
                if (!IsLocalAdmin(console)) return;
                ShowResult(GetModsStatus());
                return;
            }

            // "debug" command: local subcommands (show/hide/left/right) stay client-side,
            // server subcommands (on/off/true/false) are forwarded to the server.
            if (args[0] == "debug")
            {
                if (!IsLocalAdmin(console)) return;

                if (args.Length < 2)
                {
                    // No args: show current status
                    var status = string.Format("DebugMode: {0} (server) | Local HUD: {1}",
                        Mod.Settings.DebugMode ? "ON" : "OFF",
                        HudHandler.LocalDebugVisible ? "shown" : "hidden");
                    console.ShowMessage("Nanobars", status);
                    if (!HudHandler.LocalDebugVisible)
                        console.ShowMessage("Nanobars", "Use /nanobars debug show to enable the HUD overlay.");
                    return;
                }

                var sub = args[1];

                // Local-only: show/hide the HUD panel on this client
                if (sub == "show")
                {
                    HudHandler.SetLocalDebugVisible(true);
                    console.ShowMessage("Nanobars", "Debug HUD shown locally.");
                    return;
                }
                if (sub == "hide")
                {
                    HudHandler.SetLocalDebugVisible(false);
                    console.ShowMessage("Nanobars", "Debug HUD hidden locally.");
                    return;
                }

                // Local-only: position + auto-show
                if (sub == "left" || sub == "right")
                {
                    HudHandler.SetPosition(sub == "right");
                    HudHandler.SetLocalDebugVisible(true);
                    console.ShowMessage("Nanobars", string.Format("Debug HUD positioned {0} and shown.", sub));
                    return;
                }

                // Server-side: on/off/true/false → forwarded as config set DebugMode
                if (sub == "on" || sub == "true")
                {
                    console.ShowMessage("Nanobars", "Use /nanobars debug show to view the HUD, /nanobars debug hide to hide it.");
                    args = new[] { "config", "set", "DebugMode", "true" };
                }
                else if (sub == "off" || sub == "false")
                {
                    HudHandler.SetLocalDebugVisible(false);
                    args = new[] { "config", "set", "DebugMode", "false" };
                }
                else
                {
                    console.ShowMessage("Nanobars", "Usage: /nanobars debug [on|off|show|hide|left|right]");
                    return;
                }
            }

            // All remaining commands require server execution
            var commandText = string.Join(" ", args);

            if (MyAPIGateway.Session.IsServer)
            {
                if (!IsLocalAdmin(console)) return;

                string responseMessage;
                bool isError;
                bool useMissionScreen;
                string screenTitle;
                string screenSubtitle;
                ExecuteServerCommand(commandText, 0, out responseMessage, out isError, out useMissionScreen, out screenTitle, out screenSubtitle);

                if (useMissionScreen)
                    console.ShowMissionScreen(screenTitle ?? "Nanobot Build and Repair System", screenSubtitle ?? "", "", responseMessage);
                else
                    console.ShowMessage("Nanobars", responseMessage);
            }
            else
            {
                if (!IsLocalAdmin(console)) return;

                NetworkMessagingHandler.MsgModCommandSend(commandText);
                console.ShowMessage("Nanobars", "Command sent to server...");
            }
        }

        /// <summary>
        /// Executes a server-side command and returns the result.
        /// Called both from local server chat and from remote command forwarding.
        /// The commandText should be the args after the /nanobars prefix (e.g. "profile start 60").
        /// </summary>
        public static void ExecuteServerCommand(string commandText, ulong senderSteamId, out string responseMessage, out bool isError, out bool useMissionScreen, out string screenTitle, out string screenSubtitle)
        {
            var args = commandText.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (args.Length == 0)
            {
                responseMessage = "No command specified.";
                isError = true;
                useMissionScreen = false;
                screenTitle = null;
                screenSubtitle = null;
                return;
            }

            ChatCommandResult result;

            // "debug on/off" forwarded from client as "config set DebugMode true/false"
            // (already rewritten by OnMessageEntered before reaching here)

            switch (args[0])
            {
                case "sim":
                    result = SimCommand.Execute(args);
                    break;
                case "profile":
                    result = ProfileCommand.Execute(args, senderSteamId);
                    break;
                case "config":
                    result = ConfigCommand.Execute(args);
                    break;
                default:
                    result = ChatCommandResult.Error("Unknown command: " + args[0]);
                    break;
            }

            responseMessage = result.Message;
            isError = result.IsError;
            useMissionScreen = result.UseMissionScreen;
            screenTitle = result.ScreenTitle;
            screenSubtitle = result.ScreenSubtitle;
        }

        private static bool IsAdmin()
        {
            var player = MyAPIGateway.Session.Player;
            if (player == null) return true;
            var promoteLevel = player.PromoteLevel.ToString();
            return promoteLevel == "Admin" || promoteLevel == "SpaceMaster" || promoteLevel == "Owner";
        }

        private static bool IsLocalAdmin(VRage.Game.ModAPI.IMyUtilities console)
        {
            if (!IsAdmin())
            {
                console.ShowMessage("Nanobars", "Command requires admin permissions");
                return false;
            }
            return true;
        }

        private static ChatCommandResult GetModsStatus()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("Mod Integrations:\n");

            // TextHudAPI (BuildInfo)
            var hudActive = HudHandler.IsApiReady;
            sb.AppendFormat("  TextHudAPI (BuildInfo):  {0}\n", hudActive ? "Active" : "Not detected");

            // DefenseShields
            var shieldLoaded = Mod.Shield != null;
            var shieldReady = shieldLoaded && Mod.Shield.IsReady;
            var shieldEnabled = Mod.Settings.ShieldCheckEnabled;
            if (!shieldEnabled)
                sb.Append("  DefenseShields:         Disabled (ShieldCheckEnabled=false)\n");
            else if (shieldReady)
                sb.Append("  DefenseShields:         Active\n");
            else if (shieldLoaded)
                sb.Append("  DefenseShields:         Loaded (not ready)\n");
            else
                sb.Append("  DefenseShields:         Not detected\n");

            return ChatCommandResult.Success(sb.ToString());
        }

        private static void ShowResult(ChatCommandResult result)
        {
            if (result.UseMissionScreen)
                MyAPIGateway.Utilities.ShowMissionScreen(
                    result.ScreenTitle ?? "Nanobot Build and Repair System",
                    result.ScreenSubtitle ?? "", "", result.Message);
            else
                MyAPIGateway.Utilities.ShowMessage("Nanobars", result.Message);
        }
    }
}

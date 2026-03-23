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
                ShowResult(HelpCommand.Execute());
                return;
            }

            if (args[0] == "profile" && (args.Length < 2 || args[1] == "help"))
            {
                ShowResult(ProfileCommand.ShowHelp());
                return;
            }

            if (args[0] == "config" && (args.Length < 2 || args[1] == "help"))
            {
                ShowResult(ConfigCommand.ShowHelp());
                return;
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

            switch (args[0])
            {
                case "-cwsf":
                    result = SaveSettingsCommand.Execute();
                    break;
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

        private static bool IsLocalAdmin(VRage.Game.ModAPI.IMyUtilities console)
        {
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

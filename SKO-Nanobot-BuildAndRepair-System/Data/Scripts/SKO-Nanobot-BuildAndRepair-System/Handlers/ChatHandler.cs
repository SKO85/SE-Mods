using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Localization;
using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Utils;
using System;
using System.IO;
using VRage;

namespace SKONanobotBuildAndRepairSystem.Handlers
{
    public class ChatHandler
    {
        private const string CmdKey = "/nanobars";
        private const string CmdHelp1 = "-?";
        private const string CmdHelp2 = "-help";
        private const string CmdCwsf = "-cwsf";
        private const string CmdCpsf = "-cpsf";
        private const string CmdLogLevel = "-loglevel";
        private const string CmdWriteTranslation = "-writetranslation";
        private const string CmdLogLevel_All = "all";
        private const string CmdLogLevel_Default = "default";

        #region Registration
        private static bool _registered = false;

        public static void Register()
        {
            if (_registered || MyAPIGateway.Utilities == null)
                return;

            // Register event for messages entered in the chat.
            MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;

            _registered = true;
        }

        public static void Unregister()
        {
            if (!_registered)
                return;
            
            try
            {
                // Unregister event for handling messages entered in the chat.
                MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
            }
            catch (Exception)
            {
            }

            _registered = false;
        }
        #endregion

        private static void OnMessageEntered(string messageText, ref bool sendToOthers)
        {
            if (string.IsNullOrEmpty(messageText)) return;
            var cmd = messageText.ToLower();
            if (cmd.StartsWith(CmdKey))
            {
                if (Logging.Instance.ShouldLog(Logging.Level.Verbose)) Logging.Instance.Write(Logging.Level.Verbose, "BuildAndRepairSystemMod: Cmd: {0}", messageText);
                var args = cmd.Remove(0, CmdKey.Length).Trim().Split(' ');
                if (args.Length > 0)
                {
                    if (Logging.Instance.ShouldLog(Logging.Level.Verbose)) Logging.Instance.Write(Logging.Level.Verbose, "BuildAndRepairSystemMod: Cmd args[0]: {0}", args[0]);
                    switch (args[0].Trim())
                    {
                        case CmdCpsf:
                            if (MyAPIGateway.Session.IsServer)
                            {
                                SyncModSettings.Save(Mod.Settings, false);
                                MyAPIGateway.Utilities.ShowMessage(CmdKey, "Settings file created inside mod folder");
                            }
                            else
                            {
                                MyAPIGateway.Utilities.ShowMessage(CmdKey, "command not allowed on client");
                            }
                            break;

                        case CmdCwsf:
                            if (MyAPIGateway.Session.IsServer)
                            {
                                SyncModSettings.Save(Mod.Settings, true);
                                MyAPIGateway.Utilities.ShowMessage(CmdKey, "Settings file created inside world folder");
                            }
                            else
                            {
                                MyAPIGateway.Utilities.ShowMessage(CmdKey, "command not allowed on client");
                            }
                            break;

                        case CmdLogLevel:
                            if (args.Length > 1)
                            {
                                switch (args[1].Trim())
                                {
                                    case CmdLogLevel_All:
                                        MyAPIGateway.Utilities.ShowMessage(CmdKey, string.Format("Logging level switched to All [{0:X}]", Logging.Instance.LogLevel));                                        
                                        break;

                                    case CmdLogLevel_Default:
                                    default:
                                        Logging.Instance.LogLevel = Mod.Settings.LogLevel;
                                        MyAPIGateway.Utilities.ShowMessage(CmdKey, string.Format("Logging level switched to Default [{0:X}]", Logging.Instance.LogLevel));
                                        break;
                                }
                            }
                            break;

                        case CmdWriteTranslation:
                            if (Logging.Instance.ShouldLog(Logging.Level.Verbose)) Logging.Instance.Write(Logging.Level.Verbose, "BuildAndRepairSystemMod: CmdWriteTranslation");
                            if (args.Length > 1)
                            {
                                MyLanguagesEnum lang;
                                if (Enum.TryParse(args[1], true, out lang))
                                {
                                    LocalizationHelper.ExportDictionary(lang.ToString() + ".txt", Texts.GetDictionary(lang));
                                    MyAPIGateway.Utilities.ShowMessage(CmdKey, string.Format(lang.ToString() + ".txt writtenwa."));
                                }
                                else
                                {
                                    MyAPIGateway.Utilities.ShowMessage(CmdKey, string.Format("'{0}' is not a valid language name {1}", args[1], string.Join(",", Enum.GetNames(typeof(MyLanguagesEnum)))));
                                }
                            }
                            break;

                        case CmdHelp1:
                        case CmdHelp2:
                        default:
                            MyAPIGateway.Utilities.ShowMissionScreen("NanobotBuildAndRepairSystem", "Help", "", GetHelpText());
                            break;
                    }
                }
                else
                {
                    MyAPIGateway.Utilities.ShowMissionScreen("NanobotBuildAndRepairSystem", "Help", "", GetHelpText());
                }
                sendToOthers = false;
            }
        }

        private static string GetHelpText()
        {
            var text = string.Format(
                Texts.Cmd_HelpClient.String, 
                Constants.ModVersion, 
                CmdHelp1, 
                CmdHelp2, 
                CmdLogLevel, 
                CmdLogLevel_All, 
                CmdLogLevel_Default, 
                CmdWriteTranslation, 
                string.Join(",", Enum.GetNames(typeof(MyLanguagesEnum))), 
                MyAPIGateway.Utilities.GamePaths.UserDataPath + Path.DirectorySeparatorChar + "Storage" + Path.DirectorySeparatorChar + MyAPIGateway.Utilities.GamePaths.ModScopeName);

            if (MyAPIGateway.Session.IsServer)
            {
                text += string.Format(Texts.Cmd_HelpServer.String, CmdCwsf, CmdCpsf);
            }

            return text;
        }
    }
}

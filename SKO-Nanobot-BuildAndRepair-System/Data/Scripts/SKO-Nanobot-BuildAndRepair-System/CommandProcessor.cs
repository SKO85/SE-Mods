﻿using Sandbox.ModAPI;
using System;
using System.Text;
using VRage;

namespace SKONanobotBuildAndRepairSystem
{
    public static class CommandProcessor
    {
        private const string CmdKey = "/nanobars";
        private const string CmdHelp = "-help";
        private const string CmdCwsf = "-cwsf";
        private const string CmdCpsf = "-cpsf";
        private const string CmdLogLevel = "-loglevel";
        private const string CmdLogLevel_All = "all";
        private const string CmdWriteTranslation = "-writetranslation";
        private const string CmdScanAround = "-scan-around";
        private const string CmdScanView = "-scan-view";
        private const string ClearGPS = "-cleargps";

        public static void OnChatCommand(string messageText, ref bool sendToOthers)
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
                case CmdScanView:
                    Voxels.Scanner.ScanView();
                    break;
                case CmdScanAround:
                    Voxels.Scanner.ScanAround();
                    break;
                case ClearGPS:
                    Voxels.Scanner.ClearGPS(3);
                    break;
                case CmdCpsf:
                    if (MyAPIGateway.Session.IsServer)
                    {
                        SyncModSettings.Save(NanobotBuildAndRepairSystemMod.Settings, false);
                        // console.ShowMessage(CmdKey, "Settings saved to global mod folder");

                        var path = $"{MyAPIGateway.Utilities.GamePaths.UserDataPath}\\Storage\\{MyAPIGateway.Utilities.GamePaths.ModScopeName}";
                        MyAPIGateway.Utilities.ShowMessage("BaR Mod:", $"Config file has been saved in:\n\n{path}\n\nFilename: ModSettings.xml");
                    }
                    else
                    {
                        console.ShowMessage(CmdKey, "Command not allowed on client");
                    }
                    break;

                case CmdCwsf:
                    if (MyAPIGateway.Session.IsServer)
                    {
                        SyncModSettings.Save(NanobotBuildAndRepairSystemMod.Settings, true);
                        console.ShowMessage(CmdKey, "Settings saved to world folder");
                    }
                    else
                    {
                        console.ShowMessage(CmdKey, "Command not allowed on client");
                    }
                    break;

                case CmdLogLevel:
                    if (args.Length > 1)
                    {
                        if (args[1] == CmdLogLevel_All)
                        {
                            Logging.Instance.LogLevel = Logging.Level.All;
                            console.ShowMessage(CmdKey, string.Format("Log level set to ALL [{0:X}]", (int)Logging.Instance.LogLevel));
                        }
                        else
                        {
                            Logging.Instance.LogLevel = NanobotBuildAndRepairSystemMod.Settings.LogLevel;
                            console.ShowMessage(CmdKey, string.Format("Log level set to DEFAULT [{0:X}]", (int)Logging.Instance.LogLevel));
                        }
                    }
                    break;

                case CmdWriteTranslation:
                    if (args.Length > 1)
                    {
                        MyLanguagesEnum lang;
                        if (Enum.TryParse(args[1], true, out lang))
                        {
                            LocalizationHelper.ExportDictionary(string.Format("{0}.txt", lang), Texts.GetDictionary(lang));
                            console.ShowMessage(CmdKey, string.Format("{0}.txt written.", lang));
                        }
                        else
                        {
                            console.ShowMessage(CmdKey, "Invalid language name.");
                        }
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

            sb.AppendLine($"Version: {Constants.Version}");
            sb.AppendLine();
            sb.AppendLine($"[-loglevel all;default]: Set the logging level. Warning: Setting level to 'all' could produce very large log-files.");
            sb.AppendLine();
            sb.AppendLine($"[-cwsf]: Creates a settings file inside your current world folder.");
            sb.AppendLine();
            sb.AppendLine($"[--cpsf]: Creates a settings file inside the global mod storage folder.");
            sb.AppendLine();
            sb.AppendLine($"> Issues / Suggestions?");
            sb.AppendLine($"To report issues or suggestions, go to:");
            sb.AppendLine($"https://github.com/SKO85/SE-Mods/issues");
            sb.AppendLine();
            sb.AppendLine($"> Documentation / WIKI");
            sb.AppendLine($"For documentation and release notes, go to:");
            sb.AppendLine($"https://github.com/SKO85/SE-Mods/wiki");
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine($"Have fun!\nSKO85");

            //var lang = MyAPIGateway.Session.Config.Language;
            //var text = string.Format(Texts.Cmd_HelpClient.String, "V2.1.5", CmdHelp1, CmdHelp2,
            //    CmdLogLevel, CmdLogLevel_All, CmdLogLevel_Default,
            //    CmdWriteTranslation, string.Join(",", Enum.GetNames(typeof(MyLanguagesEnum))),
            //    MyAPIGateway.Utilities.GamePaths.UserDataPath + "/Storage/" + MyAPIGateway.Utilities.GamePaths.ModScopeName);

            //if (MyAPIGateway.Session.IsServer)
            //{
            //    text += string.Format(Texts.Cmd_HelpServer.String, CmdCwsf, CmdCpsf);
            //}

            MyAPIGateway.Utilities.ShowMissionScreen("Nanobot Build and Repair System", "Help", "", sb.ToString());
        }
    }
}

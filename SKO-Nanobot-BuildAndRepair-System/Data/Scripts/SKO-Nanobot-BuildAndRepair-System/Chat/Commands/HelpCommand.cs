using System.Text;

namespace SKONanobotBuildAndRepairSystem.Chat.Commands
{
    public static class HelpCommand
    {
        public static ChatCommandResult Execute()
        {
            var sb = new StringBuilder();

            sb.AppendLine(string.Format("Version: {0}", Constants.ModVersion));
            sb.AppendLine();
            sb.AppendLine("[-cwsf]: Creates a settings file inside your current world folder (local-only)");
            sb.AppendLine("[config help]: Shows config command syntax");
            sb.AppendLine("[profile help]: Shows profiling command syntax");
            sb.AppendLine("[sim <value|reset>]: Override sim-speed for BaR calculations");
            sb.AppendLine();
            sb.AppendLine("Issues: Report issues or suggestions (GitHub)");
            sb.AppendLine("https://github.com/SKO85/SE-Mods/issues");
            sb.AppendLine();
            sb.AppendLine("WIKI / Documentation (GitHub)");
            sb.AppendLine("https://github.com/SKO85/SE-Mods/wiki");
            sb.AppendLine();
            sb.AppendLine("FAQ / Troubleshooting (GitHub)");
            sb.AppendLine("https://github.com/SKO85/SE-Mods/wiki/FAQ---Frequently-Asked-Questions");
            sb.AppendLine();
            sb.AppendLine("Discord: Contact Developer via Discord");
            sb.AppendLine("https://discord.gg/5XkQW5tdQM");
            sb.AppendLine();
            sb.AppendLine("Have fun!\nSKO85");

            return ChatCommandResult.MissionScreen(sb.ToString(), "Nanobot Build and Repair System", "Help");
        }
    }
}

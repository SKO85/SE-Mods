using System.Text;

namespace SKONanobotBuildAndRepairSystem.Chat.Commands
{
    public static class HelpCommand
    {
        public static ChatCommandResult Execute(bool isAdmin)
        {
            var sb = new StringBuilder();

            sb.AppendLine(string.Format("Version: {0} (build {1})", Constants.ModVersion, Constants.BuildId));
            sb.AppendLine();

            sb.AppendLine("--- Player Commands ---");
            sb.AppendLine();
            sb.AppendLine("[version]: Show client and (on dedicated servers) server mod version");
            sb.AppendLine();

            if (isAdmin)
            {
                sb.AppendLine("--- Admin Commands ---");
                sb.AppendLine();
                sb.AppendLine("[config help]: Shows config command syntax");
                sb.AppendLine("[config create]: Creates/saves settings file (ModSettings.xml) in world folder");
                sb.AppendLine("[config delete]: Resets settings to defaults and deletes ModSettings.xml");
                sb.AppendLine("[profile help]: Shows profiling command syntax");
                sb.AppendLine("[profile summary]: Toggle profile summary HUD (top-right)");
                sb.AppendLine("[sim <value|reset>]: Override sim-speed for BaR calculations");
                sb.AppendLine("[debug on|off]: Enable/disable debug mode (server-wide)");
                sb.AppendLine("[debug show|hide]: Show/hide the debug HUD locally");
                sb.AppendLine("[debug left|right]: Set HUD position and show");
                sb.AppendLine("[mods]: Shows status of mod integrations (TextHudAPI, DefenseShields)");
                sb.AppendLine("[systems help]: Shows systems management command syntax");
                sb.AppendLine("[systems list]: List all BaR blocks on the server");
                sb.AppendLine("[systems count]: Show BaR count per player and faction");
                sb.AppendLine("[systems enable|disable all|--grid|--owner]: Enable/disable BaR blocks");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("Admin commands are available for players with Admin, SpaceMaster, or Owner permissions.");
                sb.AppendLine();
            }

            sb.AppendLine("--- Documentation ---");
            sb.AppendLine();
            sb.AppendLine("Visit the mod website for FAQ, troubleshooting, documentation,");
            sb.AppendLine("configuration settings, release notes, and more:");
            sb.AppendLine("https://sko85.github.io/SE-Mods");
            sb.AppendLine();
            sb.AppendLine("--- Support ---");
            sb.AppendLine();
            sb.AppendLine("Report issues, ask questions, or suggest features on GitHub:");
            sb.AppendLine("https://github.com/SKO85/SE-Mods/issues");
            sb.AppendLine();
            sb.AppendLine("Join the Discord for help and discussion:");
            sb.AppendLine("https://discord.gg/5XkQW5tdQM");
            sb.AppendLine();
            sb.AppendLine("Have fun!\nSKO85");

            return ChatCommandResult.MissionScreen(sb.ToString(), "Nanobot Build and Repair System", "Help");
        }
    }
}

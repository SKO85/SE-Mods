using SKONanobotBuildAndRepairSystem.Profiling;
using System.Text;

namespace SKONanobotBuildAndRepairSystem.Chat.Commands
{
    public static class ProfileCommand
    {
        public static ChatCommandResult Execute(string[] args, ulong senderSteamId)
        {
            // args[0] = "profile"
            if (args.Length < 2)
                return ShowHelp();

            if (args[1] == "start")
            {
                var autoStopSeconds = MethodProfiler.DefaultAutoStopDurationSeconds;
                if (args.Length >= 3 && !int.TryParse(args[2], out autoStopSeconds))
                    return ChatCommandResult.Error("Invalid seconds. Usage: /nanobars profile start [seconds] [minDurationMs] [sessionName]");

                if (args.Length >= 4)
                {
                    int minDuration;
                    if (!int.TryParse(args[3], out minDuration))
                        return ChatCommandResult.Error("Invalid minDurationMs. Usage: /nanobars profile start [seconds] [minDurationMs] [sessionName]");

                    string minDurMsg;
                    if (!MethodProfiler.SetMinDurationMs(minDuration, out minDurMsg))
                        return ChatCommandResult.Error(minDurMsg);
                }

                // Optional session name (args[4]).
                string sessionName = args.Length >= 5 ? args[4] : null;

                string message;
                MethodProfiler.StartSession(autoStopSeconds, senderSteamId, out message, sessionName);
                return ChatCommandResult.Success(message + string.Format(" (MinDurationMs={0})", MethodProfiler.MinDurationMs));
            }

            if (args[1] == "stop")
            {
                string message;
                MethodProfiler.StopSession(out message);
                return ChatCommandResult.Success(message);
            }

            if (args[1] == "status")
                return ChatCommandResult.Success(MethodProfiler.GetStatusMessage());

            // "summary" is handled locally in ChatHandler (HUD toggle, not forwarded to server)

            if (args[1] == "minduration")
            {
                if (args.Length < 3)
                    return ChatCommandResult.Success(string.Format("Current MinDurationMs={0}. Usage: /nanobars profile minduration <ms>", MethodProfiler.MinDurationMs));

                int minDuration;
                if (!int.TryParse(args[2], out minDuration))
                    return ChatCommandResult.Error("Invalid value. Usage: /nanobars profile minduration <ms>");

                string minDurMsg;
                MethodProfiler.SetMinDurationMs(minDuration, out minDurMsg);
                return ChatCommandResult.Success(minDurMsg);
            }

            if (args[1] == "clear")
            {
                if (args.Length < 3)
                    return ChatCommandResult.Error("Usage: /nanobars profile clear <sessionName|all>");

                return ChatCommandResult.Success(MethodProfiler.ClearSession(args[2]));
            }

            if (args[1] == "list")
            {
                return ChatCommandResult.Success(MethodProfiler.GetSessionListText());
            }

            return ShowHelp();
        }

        public static ChatCommandResult ShowHelp()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Profiling Commands (admin-only, server-side):");
            sb.AppendLine();
            sb.AppendLine("/nanobars profile start [seconds] [minDurationMs] [sessionName]");
            sb.AppendLine(string.Format("  Starts a profiling session. Defaults: seconds={0}, minDurationMs={1}",
                MethodProfiler.DefaultAutoStopDurationSeconds, MethodProfiler.MinDurationMs));
            sb.AppendLine("  Use 0 for seconds to disable auto-stop.");
            sb.AppendLine("  Session name is optional — defaults to yyyyMMddHHmmss-profiling.");
            sb.AppendLine();
            sb.AppendLine("/nanobars profile stop");
            sb.AppendLine("  Stops the current profiling session and writes summary.");
            sb.AppendLine();
            sb.AppendLine("/nanobars profile status");
            sb.AppendLine("  Shows whether profiling is running and current settings.");
            sb.AppendLine();
            sb.AppendLine("/nanobars profile summary");
            sb.AppendLine("  Toggle the profile summary HUD panel (top-right).");
            sb.AppendLine("  Shows domains, top methods, and heaviest grids. Stays visible after profiling stops.");
            sb.AppendLine();
            sb.AppendLine("/nanobars profile list");
            sb.AppendLine("  Lists all stored profiling sessions.");
            sb.AppendLine();
            sb.AppendLine("/nanobars profile clear <sessionName|all>");
            sb.AppendLine("  Deletes log files for a specific session or all sessions.");
            sb.AppendLine();
            sb.AppendLine("/nanobars profile minduration <ms>");
            sb.AppendLine("  Sets the minimum method duration (0-10000ms) for logging samples.");
            sb.AppendLine("  Only calls taking >= this threshold are written to log files.");
            sb.AppendLine();
            sb.AppendLine("/nanobars profile help");
            sb.AppendLine("  Shows this help.");

            return ChatCommandResult.MissionScreen(sb.ToString(), "Nanobot Build and Repair System", "Profile Help");
        }
    }
}

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
                    return ChatCommandResult.Error("Invalid seconds. Usage: /nanobars profile start [seconds] [minDurationMs]");

                if (args.Length >= 4)
                {
                    int minDuration;
                    if (!int.TryParse(args[3], out minDuration))
                        return ChatCommandResult.Error("Invalid minDurationMs. Usage: /nanobars profile start [seconds] [minDurationMs]");

                    string minDurMsg;
                    if (!MethodProfiler.SetMinDurationMs(minDuration, out minDurMsg))
                        return ChatCommandResult.Error(minDurMsg);
                }

                string message;
                MethodProfiler.StartSession(autoStopSeconds, senderSteamId, out message);
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

            return ShowHelp();
        }

        public static ChatCommandResult ShowHelp()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Profiling Commands (admin-only, server-side):");
            sb.AppendLine();
            sb.AppendLine("/nanobars profile start [seconds] [minDurationMs]");
            sb.AppendLine(string.Format("  Starts a profiling session. Defaults: seconds={0}, minDurationMs={1}",
                MethodProfiler.DefaultAutoStopDurationSeconds, MethodProfiler.MinDurationMs));
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

            return ChatCommandResult.MissionScreen(sb.ToString(), "Nanobot Build and Repair System", "Profile Help");
        }
    }
}

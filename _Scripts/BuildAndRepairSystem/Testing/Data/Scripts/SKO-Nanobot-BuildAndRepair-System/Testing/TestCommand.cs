using SKONanobotBuildAndRepairSystem.Chat;
using SKONanobotBuildAndRepairSystem.Profiling;
using SKONanobotBuildAndRepairSystem.Testing.Scenarios;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing
{
    /// <summary>/nanobars test list | run &lt;id|all&gt; [xN] | status | abort | origin &lt;x&gt; &lt;y&gt; &lt;z&gt; | origin reset</summary>
    internal static class TestCommand
    {
        private const string Usage = "Usage: /nanobars test list | run <id|all> [xN] | status | abort | origin <x> <y> <z>|reset";

        public static ChatCommandResult Execute(string[] args, ulong senderSteamId)
        {
            // args[0] = "test"
            if (args.Length < 2) return ChatCommandResult.Success(TestRunner.Status() + "\n" + Usage);

            switch (args[1].ToLowerInvariant())
            {
                case "list": return List();
                case "run": return Run(args, senderSteamId);
                case "status": return ChatCommandResult.Success(TestRunner.Status());
                case "abort":
                    if (!TestRunner.IsBusy) return ChatCommandResult.Success("Nothing running.");
                    TestRunner.Abort();
                    return ChatCommandResult.Success("Aborting current scenario; queue cleared.");
                case "origin": return Origin(args);
                default: return ChatCommandResult.Error(Usage);
            }
        }

        private static ChatCommandResult List()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Scenarios:");
            for (int i = 0; i < ScenarioRegistry.All.Length; i++)
            {
                var s = ScenarioRegistry.All[i];
                sb.Append("  ").Append(s.Id).Append(" - ").Append(s.Description)
                  .Append(" (timeout ").Append(s.TimeoutSeconds).AppendLine(" s)");
            }
            sb.AppendLine();
            sb.AppendLine(Usage);
            return ChatCommandResult.MissionScreen(sb.ToString(), "Nanobot Build and Repair System", "Test scenarios");
        }

        private static ChatCommandResult Run(string[] args, ulong senderSteamId)
        {
            if (args.Length < 3) return ChatCommandResult.Error(Usage);
            if (TestRunner.IsBusy) return ChatCommandResult.Error("A test session is already running. Use 'test status' or 'test abort'.");
            if (MethodProfiler.IsRunning) return ChatCommandResult.Error("A profiling session is active; stop it first (/nanobars profile stop).");

            var scenarios = new List<ITestScenario>();
            if (string.Equals(args[2], "all", System.StringComparison.OrdinalIgnoreCase))
            {
                scenarios.AddRange(ScenarioRegistry.All);
            }
            else
            {
                var s = ScenarioRegistry.Find(args[2]);
                if (s == null) return ChatCommandResult.Error("Unknown scenario '" + args[2] + "'. Use 'test list'.");
                scenarios.Add(s);
            }

            var repeat = 1;
            if (args.Length >= 4)
            {
                var r = args[3];
                if (r.StartsWith("x") || r.StartsWith("X")) r = r.Substring(1);
                if (!int.TryParse(r, NumberStyles.Integer, CultureInfo.InvariantCulture, out repeat) || repeat < 1 || repeat > 20)
                    return ChatCommandResult.Error("Repeat must be x1..x20.");
            }

            var msg = TestRunner.Enqueue(scenarios, repeat, senderSteamId);
            if (msg == null) return ChatCommandResult.Error("Runner busy.");
            return msg.StartsWith("Queued") ? ChatCommandResult.Success(msg) : ChatCommandResult.Error(msg);
        }

        private static ChatCommandResult Origin(string[] args)
        {
            if (args.Length >= 3 && string.Equals(args[2], "reset", System.StringComparison.OrdinalIgnoreCase))
            {
                TestRunner.OriginOverride = null;
                return ChatCommandResult.Success("Test origin reset (300 m in front of the caller).");
            }
            if (args.Length < 5) return ChatCommandResult.Error("Usage: /nanobars test origin <x> <y> <z> | reset");
            double x, y, z;
            if (!double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
                !double.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out y) ||
                !double.TryParse(args[4], NumberStyles.Float, CultureInfo.InvariantCulture, out z))
                return ChatCommandResult.Error("Origin coordinates must be numbers.");
            TestRunner.OriginOverride = new Vector3D(x, y, z);
            return ChatCommandResult.Success(string.Format(CultureInfo.InvariantCulture, "Test origin set to {0:F0},{1:F0},{2:F0}.", x, y, z));
        }
    }
}

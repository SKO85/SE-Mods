using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Chat;
using SKONanobotBuildAndRepairSystem.Profiling;
using SKONanobotBuildAndRepairSystem.Testing.Scenarios;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing
{
    /// <summary>/nanobars test list | run &lt;id|all&gt; [xN] | status | abort | clear | origin &lt;x&gt; &lt;y&gt; &lt;z&gt; | origin reset</summary>
    internal static class TestCommand
    {
        private const string Usage = "Usage: /nanobars test list | run <id|id,id|id-id|all> [xN] | status | abort | clear | origin <x> <y> <z>|reset";

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
                case "clear":
                    var cleared = TestRunner.ClearResults();
                    return cleared.StartsWith("Deleted") ? ChatCommandResult.Success(cleared) : ChatCommandResult.Error(cleared);
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
            // Scenarios assert component use (stock, shortfall, accounting); creative welds for free.
            if (MyAPIGateway.Session.CreativeMode) return ChatCommandResult.Error("The test harness needs a survival world (creative mode welds without components).");

            var scenarios = new List<ITestScenario>();
            if (string.Equals(args[2], "all", System.StringComparison.OrdinalIgnoreCase))
            {
                scenarios.AddRange(ScenarioRegistry.All);
            }
            else
            {
                string error;
                if (!Select(args[2], scenarios, out error)) return ChatCommandResult.Error(error);
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

        /// <summary>
        /// "S03", "S03,S20,S27", "S28-S33" or mixed ("S01-S05,S17"); ranges follow the registry order.
        /// Duplicates are dropped, order is the order given.
        /// </summary>
        private static bool Select(string spec, List<ITestScenario> into, out string error)
        {
            error = null;
            var parts = spec.Split(',');
            for (int p = 0; p < parts.Length; p++)
            {
                var part = parts[p].Trim();
                if (part.Length == 0) continue;
                var dash = part.IndexOf('-');
                if (dash < 0)
                {
                    var s = ScenarioRegistry.Find(part);
                    if (s == null) { error = "Unknown scenario '" + part + "'. Use 'test list'."; return false; }
                    if (!into.Contains(s)) into.Add(s);
                    continue;
                }

                var from = IndexOf(part.Substring(0, dash).Trim());
                var to = IndexOf(part.Substring(dash + 1).Trim());
                if (from < 0 || to < 0) { error = "Unknown scenario in range '" + part + "'. Use 'test list'."; return false; }
                if (from > to) { var t = from; from = to; to = t; }
                for (int i = from; i <= to; i++)
                    if (!into.Contains(ScenarioRegistry.All[i])) into.Add(ScenarioRegistry.All[i]);
            }
            if (into.Count == 0) { error = Usage; return false; }
            return true;
        }

        private static int IndexOf(string id)
        {
            for (int i = 0; i < ScenarioRegistry.All.Length; i++)
                if (string.Equals(ScenarioRegistry.All[i].Id, id, System.StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
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

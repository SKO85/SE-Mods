using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-API-001 — the PB property contract: every BuildAndRepair.* id the companion script uses is registered
    // by the mod, and the in-game check (harness S09) lists exactly those ids.
    public class CompanionScriptContractTests
    {
        private static readonly Regex Id = new Regex("\"(BuildAndRepair\\.[A-Za-z.]+)\"", RegexOptions.Compiled);

        // Control helpers: Create(\n // Id:\n "UseIgnoreColor", ...)
        private static readonly Regex ControlId = new Regex(@"Create\(\s*(?://[^\n]*\n\s*)*""([A-Za-z]+)""", RegexOptions.Compiled);

        private static HashSet<string> Ids(string text)
        {
            return new HashSet<string>(Id.Matches(text).Cast<Match>().Select(m => m.Groups[1].Value));
        }

        private static string ModDir
        {
            get { return Path.Combine(TestEnvironment.RepoRoot, "SKO-Nanobot-BuildAndRepair-System", "Data", "Scripts", "SKO-Nanobot-BuildAndRepair-System"); }
        }

        [Fact]
        public void EveryScriptProperty_IsRegisteredByTheMod()
        {
            var used = Ids(File.ReadAllText(CompanionScriptHost.ScriptPath));

            // Explicit properties (Terminal/Properties.cs), plus every terminal control, which Terminal.cs mirrors as a
            // "BuildAndRepair." + control.Id property (CreateProperty(control)). Exact registration is checked in game (S09).
            var registered = Ids(File.ReadAllText(Path.Combine(ModDir, "Terminal", "Properties.cs")));
            foreach (var file in Directory.GetFiles(Path.Combine(ModDir, "Terminal"), "*.cs"))
                foreach (Match m in ControlId.Matches(File.ReadAllText(file)))
                    registered.Add("BuildAndRepair." + m.Groups[1].Value);

            var missing = used.Where(id => !registered.Contains(id)).OrderBy(id => id).ToList();
            Assert.True(missing.Count == 0, "used by Script.cs but not registered by the mod: " + string.Join(", ", missing));
        }

        // Harness S81-S83 swap the group config between this marker and the next "\n};" — keep that block's shape.
        [Fact]
        public void GroupConfigBlock_HasTheShapeTheHarnessReplaces()
        {
            var script = File.ReadAllText(CompanionScriptHost.ScriptPath);
            const string start = "static BuildAndRepairSystemQueuingGroup[] BuildAndRepairSystemQueuingGroups = {";
            var at = script.IndexOf(start, System.StringComparison.Ordinal);
            Assert.True(at >= 0, "group config marker not found");
            var end = script.IndexOf("\n};", at, System.StringComparison.Ordinal);
            Assert.True(end > at, "group config block not closed by a line starting with \"};\"");
            Assert.Contains("BuildAndRepairGroup1", script.Substring(at, end - at));
        }

        [Fact]
        public void HarnessS09_ChecksExactlyTheScriptProperties()
        {
            var used = Ids(File.ReadAllText(CompanionScriptHost.ScriptPath));
            var s09 = Ids(File.ReadAllText(Path.Combine(TestEnvironment.RepoRoot, "_Scripts", "BuildAndRepairSystem", "Testing", "Data", "Scripts",
                "SKO-Nanobot-BuildAndRepair-System", "Testing", "Scenarios", "S09_PbApi.cs")));

            Assert.True(used.SetEquals(s09), "S09 list differs from Script.cs — only in script: " + string.Join(", ", used.Except(s09)) +
                " | only in S09: " + string.Join(", ", s09.Except(used)));
        }
    }
}

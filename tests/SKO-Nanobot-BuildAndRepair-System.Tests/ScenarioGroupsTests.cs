using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // Harness groups (`/nanobars test run @name`): every scenario but the S00 warm-up is in a group, every group member
    // is a registered scenario, and harness.md documents every group.
    public class ScenarioGroupsTests
    {
        private const BindingFlags StaticAll = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private static Type Registry
        {
            get { return typeof(NanobotSystem).Assembly.GetType("SKONanobotBuildAndRepairSystem.Testing.Scenarios.ScenarioRegistry", true); }
        }

        private static List<string> ScenarioIds()
        {
            var all = (Array)Registry.GetField("All", StaticAll).GetValue(null);
            var ids = new List<string>();
            foreach (var s in all) ids.Add((string)s.GetType().GetProperty("Id").GetValue(s, null));
            return ids;
        }

        private static Dictionary<string, string[]> Groups()
        {
            var groups = (Array)Registry.GetField("Groups", StaticAll).GetValue(null);
            var result = new Dictionary<string, string[]>();
            foreach (var g in groups)
                result[(string)g.GetType().GetField("Name").GetValue(g)] = (string[])g.GetType().GetField("Ids").GetValue(g);
            return result;
        }

        [Fact]
        public void EveryScenarioButTheWarmUp_IsInAGroup()
        {
            var grouped = new HashSet<string>(Groups().Values.SelectMany(ids => ids));
            var missing = ScenarioIds().Where(id => id != "S00" && !grouped.Contains(id)).ToList();
            Assert.True(missing.Count == 0, "scenarios without a group: " + string.Join(", ", missing));
        }

        [Fact]
        public void GroupMembers_AreRegisteredScenarios_WithoutDuplicates()
        {
            var ids = new HashSet<string>(ScenarioIds());
            foreach (var g in Groups())
            {
                var unknown = g.Value.Where(id => !ids.Contains(id)).ToList();
                Assert.True(unknown.Count == 0, "@" + g.Key + " lists unknown scenarios: " + string.Join(", ", unknown));
                Assert.True(g.Value.Distinct().Count() == g.Value.Length, "@" + g.Key + " lists a scenario twice");
            }
        }

        [Fact]
        public void HarnessDoc_DocumentsEveryGroup()
        {
            var doc = File.ReadAllText(Path.Combine(TestEnvironment.RepoRoot, "docs", "plan", "scenarios", "harness.md"));
            var at = doc.IndexOf("## Groups", StringComparison.Ordinal);
            Assert.True(at >= 0, "harness.md has no '## Groups' section");
            var section = doc.Substring(at);
            var missing = Groups().Keys.Where(name => section.IndexOf("`@" + name + "`", StringComparison.Ordinal) < 0).ToList();
            Assert.True(missing.Count == 0, "groups missing in harness.md: " + string.Join(", ", missing));
        }
    }
}

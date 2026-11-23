using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    /// <summary>
    /// Enforces the scenario-catalogue rules (docs/plan/scenarios/README.md):
    /// a coverage entry that is not "planned:" must name a test / harness scenario that exists,
    /// and scenarios/state.md must index exactly the SCN files on disk.
    /// </summary>
    public class CatalogueConsistencyTests
    {
        private static readonly Regex CoverageToken = new Regex(@"(planned:\s*)?(ingame|unit):([A-Za-z0-9_.]+)", RegexOptions.Compiled);

        private static string ScenariosDir
        {
            get { return Path.Combine(TestEnvironment.RepoRoot, "docs", "plan", "scenarios"); }
        }

        private static IEnumerable<string> ScenarioFiles()
        {
            return Directory.GetFiles(ScenariosDir, "SCN-*.md", SearchOption.AllDirectories);
        }

        private static string IdFromFile(string path)
        {
            var m = Regex.Match(Path.GetFileName(path), @"^(SCN-[A-Z]+-\d{3})");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string CoverageSection(string text)
        {
            var m = Regex.Match(text, @"^## Coverage\s*$(.*?)(?=^## )", RegexOptions.Multiline | RegexOptions.Singleline);
            return m.Success ? m.Groups[1].Value : string.Empty;
        }

        private static HashSet<string> HarnessScenarioIds()
        {
            var registry = typeof(Mod).Assembly.GetType("SKONanobotBuildAndRepairSystem.Testing.Scenarios.ScenarioRegistry", true);
            var all = (Array)registry.GetField("All").GetValue(null);
            return new HashSet<string>(all.Cast<object>().Select(s => (string)s.GetType().GetProperty("Id").GetValue(s)), StringComparer.OrdinalIgnoreCase);
        }

        private static bool UnitTestExists(string reference)
        {
            var parts = reference.Split('.');
            var type = typeof(CatalogueConsistencyTests).Assembly.GetTypes().FirstOrDefault(t => t.Name == parts[0]);
            if (type == null) return false;
            if (parts.Length == 1) return true;
            return type.GetMethod(parts[1], BindingFlags.Public | BindingFlags.Instance) != null;
        }

        [Fact]
        public void StateIndex_MatchesScenarioFiles()
        {
            var fileIds = new HashSet<string>(ScenarioFiles().Select(IdFromFile).Where(id => id != null));
            var state = File.ReadAllText(Path.Combine(ScenariosDir, "state.md"));
            var indexIds = new HashSet<string>(Regex.Matches(state, @"^\| (SCN-[A-Z]+-\d{3}) \|", RegexOptions.Multiline).Cast<Match>().Select(m => m.Groups[1].Value));

            Assert.True(fileIds.SetEquals(indexIds),
                "files without index row: " + string.Join(", ", fileIds.Except(indexIds)) +
                " | index rows without file: " + string.Join(", ", indexIds.Except(fileIds)));
        }

        /// <summary>harness.md documents every registered in-game scenario (summary row + section) and no stale ones.</summary>
        [Fact]
        public void HarnessDoc_DocumentsEveryRegisteredScenario()
        {
            var doc = File.ReadAllText(Path.Combine(ScenariosDir, "harness.md"));
            var sections = new HashSet<string>(Regex.Matches(doc, @"^### (S\d{2,3})\s*$", RegexOptions.Multiline).Cast<Match>().Select(m => m.Groups[1].Value));
            var summary = doc.Substring(doc.IndexOf("## Summary", StringComparison.Ordinal)); // rows must sit in the Summary table
            summary = summary.Substring(0, summary.IndexOf("\n## ", 1, StringComparison.Ordinal));
            var rows = new HashSet<string>(Regex.Matches(summary, @"^\| (S\d{2,3}) \|", RegexOptions.Multiline).Cast<Match>().Select(m => m.Groups[1].Value));
            var registered = HarnessScenarioIds();
            registered.Add("S00"); // hidden warm-up, queued by the runner

            var missing = registered.Where(id => !sections.Contains(id) || !rows.Contains(id)).OrderBy(id => id).ToList();
            var stale = sections.Union(rows).Where(id => !registered.Contains(id)).OrderBy(id => id).ToList();
            Assert.True(missing.Count == 0 && stale.Count == 0,
                "harness.md out of sync — undocumented: " + string.Join(", ", missing) + " | documented but not registered: " + string.Join(", ", stale));
        }

        [Fact]
        public void CoverageEntries_NameExistingTestsAndScenarios()
        {
            var harness = HarnessScenarioIds();
            var problems = new List<string>();
            foreach (var file in ScenarioFiles())
            {
                foreach (Match token in CoverageToken.Matches(CoverageSection(File.ReadAllText(file))))
                {
                    if (token.Groups[1].Success && token.Groups[1].Length > 0) continue; // planned: — not required to exist yet
                    var kind = token.Groups[2].Value;
                    var target = token.Groups[3].Value.TrimEnd('.');
                    var ok = kind == "ingame" ? harness.Contains(target) : UnitTestExists(target);
                    if (!ok) problems.Add(IdFromFile(file) + " -> " + kind + ":" + target);
                }
            }
            Assert.True(problems.Count == 0, "Coverage names something that does not exist: " + string.Join("; ", problems));
        }
    }
}

using SKONanobotBuildAndRepairSystem.Handlers;
using SKONanobotBuildAndRepairSystem.Utils;
using System.Linq;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-CLUSTER-002 — saturation is strictly greater-than (GridSystemCount includes the BaR itself).
    public class GridSaturationTrackerTests : ModTestBase
    {
        [Fact]
        public void Rebuild_UsesStrictlyGreaterThan()
        {
            Mod.Settings.MaxSystemsPerTargetGrid = 10;
            Mod.GridSystemCount[1] = 10;
            Mod.GridSystemCount[2] = 11;

            var tracker = new GridSaturationTracker();
            tracker.Rebuild();

            Assert.False(tracker.Contains(1));
            Assert.True(tracker.Contains(2));
            Assert.Equal(1, tracker.Count);
        }

        [Fact]
        public void DecrementGridCount_RemovesKeyAtZero_AndIgnoresUnknown()
        {
            Mod.IncrementGridCount(5);
            Mod.IncrementGridCount(5);
            Mod.DecrementGridCount(5);
            Assert.Equal(1, Mod.GridSystemCount[5]);
            Mod.DecrementGridCount(5);
            Assert.False(Mod.GridSystemCount.ContainsKey(5));
            Mod.DecrementGridCount(99); // no throw, no negative entry
            Assert.False(Mod.GridSystemCount.ContainsKey(99));
        }
    }

    public class UtilsTextTests
    {
        [Fact]
        public void WrapText_KeepsLinesWithinLimit()
        {
            var text = "The Build and Repair System welds, grinds and collects automatically within its area.";
            var wrapped = UtilsText.WrapText(text, 20);
            Assert.All(wrapped.Split('\n'), line => Assert.True(line.TrimEnd('\r').Length <= 20, "line too long: " + line));
            Assert.Equal(text.Replace(" ", ""), wrapped.Replace("\r", "").Replace("\n", "").Replace(" ", ""));
        }

        [Fact]
        public void WrapText_ShortTextUnchanged()
        {
            Assert.Equal("short", UtilsText.WrapText("short", 40));
        }
    }

    // Harness registry sanity (FEAT-260910.2): ids unique, lookup case-insensitive.
    public class ScenarioRegistryTests
    {
        private static System.Type Registry
        {
            get { return typeof(Mod).Assembly.GetType("SKONanobotBuildAndRepairSystem.Testing.Scenarios.ScenarioRegistry", true); }
        }

        private static object[] All()
        {
            return ((System.Array)Registry.GetField("All").GetValue(null)).Cast<object>().ToArray();
        }

        private static string IdOf(object scenario)
        {
            return (string)scenario.GetType().GetProperty("Id").GetValue(scenario);
        }

        [Fact]
        public void ScenarioIds_AreUnique()
        {
            var ids = All().Select(IdOf).ToList();
            Assert.NotEmpty(ids);
            Assert.Equal(ids.Count, ids.Distinct(System.StringComparer.OrdinalIgnoreCase).Count());
        }

        [Fact]
        public void Find_IsCaseInsensitive()
        {
            var first = IdOf(All()[0]);
            var found = Registry.GetMethod("Find").Invoke(null, new object[] { first.ToLowerInvariant() });
            Assert.NotNull(found);
        }
    }
}

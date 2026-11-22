using System.Reflection;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-BUDGET-004 / SCN-BUDGET-008 — BUG-261122.14: per-BaR stagger groups. Clusters of any size fire every cycle
    // (a 6+ cluster used to fire every 3rd); solo BaRs keep the BUG-102 cap of 2; low sim speed still throttles.
    public class StaggerGroupsTests
    {
        private static int Groups(int clusterSize, int modWide, float sim)
        {
            return (int)typeof(NanobotSystem).GetMethod("ResolveEffectiveGroups", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { clusterSize, modWide, sim });
        }

        [Theory]
        [InlineData(2)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(10)]
        [InlineData(40)]
        public void Cluster_AnySize_FiresEveryCycle(int size)
        {
            Assert.Equal(1, Groups(size, 3, 1f));
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(2, 2)]
        [InlineData(3, 2)]
        public void Solo_UsesModWideStagger_CappedAt2(int modWide, int expected)
        {
            Assert.Equal(expected, Groups(1, modWide, 1f));
        }

        [Theory]
        [InlineData(10, 0.95f, 1)]  // healthy server: no penalty
        [InlineData(10, 0.8f, 2)]   // 1 + ceil(0.2 * 3)
        [InlineData(10, 0.4f, 3)]   // capped at the mod-wide stagger
        [InlineData(1, 0.5f, 3)]    // solo: 2 + 2, capped at 3
        public void LowSimSpeed_StillThrottles(int size, float sim, int expected)
        {
            Assert.Equal(expected, Groups(size, 3, sim));
        }
    }
}

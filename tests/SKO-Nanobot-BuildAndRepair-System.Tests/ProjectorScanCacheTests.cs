using NSubstitute;
using SKONanobotBuildAndRepairSystem.Caches;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using VRage.Game.ModAPI;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-WELD-002 — BUG-261122.3 projector cold start drops the stale "empty" verdicts;
    // BUG-261122.4 filtered GetBlocks on the scan thread survives a grid change.
    public class ProjectorScanCacheTests : ModTestBase
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly Type GridScanCache = typeof(NanobotSystem).Assembly.GetType("SKONanobotBuildAndRepairSystem.Cluster.GridScanCache");

        private static object ScanCacheGet(long gridId)
        {
            return GridScanCache.GetMethod("TryGet").Invoke(null, new object[] { gridId, 1 });
        }

        [Fact]
        public void ColdStart_DropsEmptyAndScanCacheEntriesOfTheFiringGrids()
        {
            var system = new NanobotSystem();
            var emptyGrids = (ConcurrentDictionary<long, TimeSpan>)typeof(NanobotSystem).GetField("_EmptyGridCache", Any).GetValue(system);
            ((List<long>)typeof(NanobotSystem).GetField("_coldStartGridIds", Any).GetValue(system)).Add(42);
            ((List<long>)typeof(NanobotSystem).GetField("_coldStartPhase2GridIds", Any).GetValue(system)).Add(43);
            emptyGrids[42] = Now;
            emptyGrids[43] = Now;
            emptyGrids[7] = Now;
            var set = GridScanCache.GetMethod("Set");
            set.Invoke(null, new object[] { 42L, 1, null, null });
            set.Invoke(null, new object[] { 7L, 1, null, null });
            try
            {
                typeof(NanobotSystem).GetMethod("DropColdStartGridCaches", Any).Invoke(system, null);

                Assert.False(emptyGrids.ContainsKey(42));
                Assert.False(emptyGrids.ContainsKey(43));
                Assert.True(emptyGrids.ContainsKey(7));
                Assert.Null(ScanCacheGet(42));
                Assert.NotNull(ScanCacheGet(7));
            }
            finally
            {
                GridScanCache.GetMethod("Clear").Invoke(null, null);
            }
        }

        [Fact]
        public void FilteredGetBlocks_GridChangedMidEnumeration_KeepsThePartialList()
        {
            var grid = Substitute.For<IMyCubeGrid>();
            grid.When(g => g.GetBlocks(Arg.Any<List<IMySlimBlock>>(), Arg.Any<Func<IMySlimBlock, bool>>())).Do(ci =>
            {
                ci.Arg<List<IMySlimBlock>>().Add(Substitute.For<IMySlimBlock>());
                throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");
            });
            var blocks = new List<IMySlimBlock>();

            SharedGridBlockCache.GetBlocks(grid, blocks, b => b != null);

            Assert.Single(blocks);
        }
    }
}

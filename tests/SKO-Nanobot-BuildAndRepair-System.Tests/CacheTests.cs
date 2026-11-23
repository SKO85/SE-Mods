using NSubstitute;
using SKONanobotBuildAndRepairSystem.Caches;
using SKONanobotBuildAndRepairSystem.Cluster;
using System.Collections;
using System.Reflection;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using Xunit;
using MyAPIGateway = Sandbox.ModAPI.MyAPIGateway;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-CLUSTER-003 — the scan caches: SharedEntityCache (entities in a box, 4 s, containment hits),
    // SharedGridBlockCache (block list with the BUG-149 partial-list guard) and GridScanCache (per-grid candidates).
    public class CacheTests : ModTestBase
    {
        private readonly IMyEntities _entities;
        private int _engineCalls;

        public CacheTests()
        {
            SharedEntityCache.Clear();
            ScanCache("Clear");
            _entities = Substitute.For<IMyEntities>();
            var box = new BoundingBoxD();
            _entities.GetTopMostEntitiesInBox(ref box).ReturnsForAnyArgs(ci =>
            {
                _engineCalls++;
                return new List<IMyEntity> { Substitute.For<IMyEntity>(), Substitute.For<IMyEntity>() };
            });
            MyAPIGateway.Entities = _entities;
        }

        public override void Dispose()
        {
            MyAPIGateway.Entities = null;
            SharedEntityCache.Clear();
            ScanCache("Clear");
            base.Dispose();
        }

        private static BoundingBoxD Box(double cx, double half)
        {
            return new BoundingBoxD(new Vector3D(cx - half, -half, -half), new Vector3D(cx + half, half, half));
        }

        private List<IMyEntity> Get(BoundingBoxD box)
        {
            return SharedEntityCache.GetEntitiesInBox(ref box);
        }

        [Fact]
        public void EntityCache_SameBoxWithinTtl_AsksTheEngineOnce()
        {
            Get(Box(10, 40));
            Now += TimeSpan.FromSeconds(3);
            var second = Get(Box(10, 40));
            Assert.Equal(1, _engineCalls);
            Assert.Equal(2, second.Count);
        }

        [Fact]
        public void EntityCache_ReturnsACopy_CallerMayModifyIt()
        {
            var first = Get(Box(10, 40));
            first.Clear();
            Assert.Equal(2, Get(Box(10, 40)).Count);
        }

        [Fact]
        public void EntityCache_ContainedBoxHits_LargerBoxMisses()
        {
            Get(Box(10, 40));
            Get(Box(12, 30)); // inside the cached box, same 50 m cell
            Assert.Equal(1, _engineCalls);
            Get(Box(10, 45)); // bigger than the cached box
            Assert.Equal(2, _engineCalls);
        }

        [Fact]
        public void EntityCache_ExpiresAfterFourSeconds()
        {
            Get(Box(10, 40));
            Now += TimeSpan.FromSeconds(4.1);
            Get(Box(10, 40));
            Assert.Equal(2, _engineCalls);
        }

        [Fact]
        public void EntityCache_FarAwayBox_IsAMiss()
        {
            Get(Box(10, 40));
            Get(Box(5000, 40));
            Assert.Equal(2, _engineCalls);
        }

        [Fact]
        public void EntityCache_CleanupEvictsOnlyOldEntries()
        {
            Get(Box(10, 40));
            Now += TimeSpan.FromSeconds(9); // > 2x TTL
            Get(Box(5000, 40));             // fresh entry
            SharedEntityCache.Cleanup();
            Get(Box(5000, 40));             // still cached
            Assert.Equal(2, _engineCalls);
            Get(Box(10, 40));               // evicted -> engine again
            Assert.Equal(3, _engineCalls);
        }

        [Fact]
        public void GridBlockCache_ReturnsAllBlocks()
        {
            var grid = Substitute.For<IMyCubeGrid>();
            grid.When(g => g.GetBlocks(Arg.Any<List<IMySlimBlock>>(), Arg.Any<Func<IMySlimBlock, bool>>()))
                .Do(ci => { for (int i = 0; i < 5; i++) ci.Arg<List<IMySlimBlock>>().Add(Substitute.For<IMySlimBlock>()); });
            Assert.Equal(5, SharedGridBlockCache.GetBlocks(grid).Count);
        }

        [Fact]
        public void GridBlockCache_GridChangedMidRead_KeepsThePartialList()
        {
            var grid = Substitute.For<IMyCubeGrid>();
            grid.When(g => g.GetBlocks(Arg.Any<List<IMySlimBlock>>(), Arg.Any<Func<IMySlimBlock, bool>>()))
                .Do(ci =>
                {
                    ci.Arg<List<IMySlimBlock>>().Add(Substitute.For<IMySlimBlock>());
                    ci.Arg<List<IMySlimBlock>>().Add(Substitute.For<IMySlimBlock>());
                    throw new InvalidOperationException("Collection was modified");
                });
            Assert.Equal(2, SharedGridBlockCache.GetBlocks(grid).Count);
        }

        // GridScanCache is internal: reached through reflection (test code only).
        private static object ScanCache(string method, params object[] args)
        {
            var type = typeof(NanobotSystem).Assembly.GetType("SKONanobotBuildAndRepairSystem.Cluster.GridScanCache", true);
            return type.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
        }

        private static object TryGet(long gridId, int paramsHash) { return ScanCache("TryGet", gridId, paramsHash); }

        private static void Set(long gridId, int paramsHash, List<ClusterTargetCandidate> weld)
        {
            ScanCache("Set", gridId, paramsHash, weld, null);
        }

        private static int WeldCount(object entry)
        {
            var list = (ICollection)entry.GetType().GetField("WeldCandidates").GetValue(entry);
            return list != null ? list.Count : 0;
        }

        private static List<ClusterTargetCandidate> Candidates(int n)
        {
            var list = new List<ClusterTargetCandidate>();
            for (int i = 0; i < n; i++) list.Add(new ClusterTargetCandidate(Substitute.For<IMySlimBlock>(), 0));
            return list;
        }

        [Fact]
        public void GridScanCache_HitNeedsTheSameScanParameters()
        {
            Set(42, 7, Candidates(3));
            Assert.NotNull(TryGet(42, 7));
            Assert.Null(TryGet(42, 8)); // other settings (colours, modes, ...) -> rescan
            Assert.Null(TryGet(43, 7));
        }

        [Fact]
        public void GridScanCache_StoresACopy()
        {
            var weld = Candidates(3);
            Set(42, 7, weld);
            weld.Clear();
            Assert.Equal(3, WeldCount(TryGet(42, 7)));
        }

        [Fact]
        public void GridScanCache_InvalidateIfEmpty_DropsOnlyEmptyResults()
        {
            Set(1, 7, null);
            Set(2, 7, Candidates(1));
            ScanCache("InvalidateIfEmpty", 1L);
            ScanCache("InvalidateIfEmpty", 2L);
            Assert.Null(TryGet(1, 7));
            Assert.NotNull(TryGet(2, 7));
            ScanCache("Invalidate", 2L);
            Assert.Null(TryGet(2, 7));
        }
    }
}

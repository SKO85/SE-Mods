using NSubstitute;
using SKONanobotBuildAndRepairSystem.Cluster;
using SKONanobotBuildAndRepairSystem.Handlers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using VRage.Game;
using VRage.Game.ModAPI;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-CORE-001 — BUG-261122.10 new damage ends the empty-grid skip and the idle scan backoff.
    public class GridChangeHandlerTests : ModTestBase
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static readonly Type ScanCache = typeof(Mod).Assembly.GetType("SKONanobotBuildAndRepairSystem.Cluster.GridScanCache");

        public GridChangeHandlerTests()
        {
            GridChangeHandler.Clear();
        }

        public override void Dispose()
        {
            GridChangeHandler.Unregister();
            GridChangeHandler.Clear();
            base.Dispose();
        }

        private static void Track(IMyCubeGrid grid)
        {
            typeof(GridChangeHandler).GetMethod("Track", Any).Invoke(null, new object[] { grid });
        }

        // BUG-261122.11: a hand-placed frame (block added) ends the empty-grid skip like damage does.
        [Fact]
        public void BlockAdded_OnATrackedGrid_StampsIt()
        {
            var grid = Substitute.For<IMyCubeGrid>();
            grid.EntityId.Returns(7L);
            Track(grid);
            Track(grid); // once only
            Assert.Equal(1, GridChangeHandler.TrackedGrids);

            var block = Substitute.For<IMySlimBlock>();
            block.CubeGrid.Returns(grid);
            grid.OnBlockAdded += Raise.Event<Action<IMySlimBlock>>(block);

            Assert.True(GridChangeHandler.ChangedSince(7, Now - S(5)));
        }

        [Fact]
        public void ClosedGrid_IsNoLongerTracked()
        {
            var grid = Substitute.For<IMyCubeGrid>();
            grid.EntityId.Returns(8L);
            Track(grid);

            grid.OnClose += Raise.Event<Action<VRage.ModAPI.IMyEntity>>(grid);

            Assert.Equal(0, GridChangeHandler.TrackedGrids);
        }

        private static IMySlimBlock BlockOn(long gridId)
        {
            var grid = Substitute.For<IMyCubeGrid>();
            grid.EntityId.Returns(gridId);
            var block = Substitute.For<IMySlimBlock>();
            block.CubeGrid.Returns(grid);
            return block;
        }

        private static TimeSpan S(double seconds)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        private static void CacheSet(long gridId, List<ClusterTargetCandidate> weld)
        {
            ScanCache.GetMethod("Set", Any).Invoke(null, new object[] { gridId, 0, weld, null });
        }

        private static bool CacheHas(long gridId)
        {
            return ScanCache.GetMethod("TryGet", Any).Invoke(null, new object[] { gridId, 0 }) != null;
        }

        [Fact]
        public void Damage_EndsTheEmptySkipOfThatGridOnly()
        {
            DamageHandler.OnAfterDamage(BlockOn(1), new MyDamageInformation(false, 50f, MyDamageType.Bullet, 0));

            Assert.True(GridChangeHandler.ChangedSince(1, Now - S(5)));
            Assert.False(GridChangeHandler.ChangedSince(2, Now - S(5)));
        }

        [Fact]
        public void ZeroDamage_DoesNotStamp()
        {
            DamageHandler.OnAfterDamage(BlockOn(1), new MyDamageInformation(false, 0f, MyDamageType.Bullet, 0));

            Assert.Equal(0, GridChangeHandler.Count);
        }

        [Fact]
        public void EmptyMarkTakenWellAfterTheDamage_IsTrusted()
        {
            GridChangeHandler.MarkChanged(1, Now);

            Assert.True(GridChangeHandler.ChangedSince(1, Now + GridChangeHandler.Grace - S(0.1)));
            Assert.False(GridChangeHandler.ChangedSince(1, Now + GridChangeHandler.Grace));
        }

        [Fact]
        public void RepeatedHits_StampAtMostOncePerSecond()
        {
            GridChangeHandler.MarkChanged(1, Now);
            GridChangeHandler.MarkChanged(1, Now + S(0.5));
            Assert.False(GridChangeHandler.ChangedSince(1, Now + GridChangeHandler.Grace)); // still the first stamp

            GridChangeHandler.MarkChanged(1, Now + S(1.5));
            Assert.True(GridChangeHandler.ChangedSince(1, Now + GridChangeHandler.Grace));
        }

        [Fact]
        public void SkipDisabled_NoStamps()
        {
            Mod.Settings.EmptyGridRescanDelaySeconds = 0;
            GridChangeHandler.MarkChanged(1, Now);

            Assert.Equal(0, GridChangeHandler.Count);
        }

        [Fact]
        public void Damage_DropsAnEmptyScanCacheEntry_KeepsANonEmptyOne()
        {
            CacheSet(1, null);
            CacheSet(2, new List<ClusterTargetCandidate> { new ClusterTargetCandidate() });

            GridChangeHandler.MarkChanged(1, Now);
            GridChangeHandler.MarkChanged(2, Now);

            Assert.False(CacheHas(1));
            Assert.True(CacheHas(2));
        }

        [Fact]
        public void Cleanup_DropsOnlyStampsNoLiveEmptyMarkCanPredate()
        {
            Mod.Settings.EmptyGridRescanDelaySeconds = 20; // max age = 20 s + grace
            GridChangeHandler.MarkChanged(1, Now);
            GridChangeHandler.MarkChanged(2, Now + S(15));

            GridChangeHandler.Cleanup(Now + S(23));

            Assert.False(GridChangeHandler.ChangedSince(1, Now - S(1)));
            Assert.True(GridChangeHandler.ChangedSince(2, Now + S(14)));
        }

        [Fact]
        public void IdleBar_SeesDamageOnAGridItFoundEmpty()
        {
            var system = new NanobotSystem();
            var empty = (ConcurrentDictionary<long, TimeSpan>)typeof(NanobotSystem).GetField("_EmptyGridCache", Any).GetValue(system);
            var hasDamage = typeof(NanobotSystem).GetMethod("HasDamageOnEmptyGrid", Any);
            empty[5] = Now - S(10);

            GridChangeHandler.MarkChanged(6, Now); // a grid it does not know
            Assert.False((bool)hasDamage.Invoke(system, null));

            GridChangeHandler.MarkChanged(5, Now);
            Assert.True((bool)hasDamage.Invoke(system, null));
        }
    }
}

using NSubstitute;
using SKONanobotBuildAndRepairSystem.Cluster;
using SKONanobotBuildAndRepairSystem.Models;
using System.Reflection;
using VRage.Game.ModAPI;
using Xunit;
using IMyShipWelder = Sandbox.ModAPI.IMyShipWelder;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-CLUSTER-001 — cluster forming (ScanClusterCoordinator): BaRs on the same grid with the same scan-relevant
    // settings share one cluster and one coordinator (lowest EntityId); different grid / settings / owner split them;
    // disabled BaRs leave; the coordinator is re-elected; the key hash changes exactly when the key does.
    public class ClusterTests : ModTestBase
    {
        private const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;

        private static object Coord(string method, params object[] args)
        {
            var type = typeof(NanobotSystem).Assembly.GetType("SKONanobotBuildAndRepairSystem.Cluster.ScanClusterCoordinator", true);
            return type.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
        }

        private static int ClusterCount
        {
            get
            {
                var type = typeof(NanobotSystem).Assembly.GetType("SKONanobotBuildAndRepairSystem.Cluster.ScanClusterCoordinator", true);
                return (int)type.GetProperty("ClusterCount", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).GetValue(null, null);
            }
        }

        private static void Rebuild() { Coord("RebuildClusters"); }
        private static string Key(NanobotSystem s) { return (string)Coord("ComputeClusterKey", s); }
        private static int Hash(NanobotSystem s) { return (int)Coord("ComputeClusterKeyHash", s); }

        private static SyncBlockSettings SettingsOf(NanobotSystem s)
        {
            return (SyncBlockSettings)typeof(NanobotSystem).GetField("_Settings", Inst).GetValue(s);
        }

        public ClusterTests()
        {
            Mod.NanobotSystems.Clear();
            Coord("Clear");
        }

        public override void Dispose()
        {
            Mod.NanobotSystems.Clear();
            Coord("Clear");
            base.Dispose();
        }

        private static NanobotSystem Bar(long id, long gridId = 500, long owner = 77)
        {
            var system = new NanobotSystem();
            typeof(NanobotSystem).GetField("_Settings", Inst).SetValue(system, new SyncBlockSettings(system));
            var grid = Substitute.For<IMyCubeGrid>();
            grid.EntityId.Returns(gridId);
            var welder = Substitute.For<IMyShipWelder>();
            welder.EntityId.Returns(id);
            welder.CubeGrid.Returns(grid);
            welder.OwnerId.Returns(owner);
            welder.IsFunctional.Returns(true);
            welder.Enabled.Returns(true);
            welder.UseConveyorSystem.Returns(true);
            typeof(NanobotSystem).GetField("_Welder", Inst).SetValue(system, welder);
            system.State.Ready = true;
            Mod.NanobotSystems[id] = system;
            return system;
        }

        private static ScanCluster ClusterOf(NanobotSystem s)
        {
            return (ScanCluster)typeof(NanobotSystem).GetField("AssignedCluster", Inst | BindingFlags.Public).GetValue(s);
        }

        [Fact]
        public void SameGridSameSettings_OneCluster_LowestIdCoordinates()
        {
            var a = Bar(30); var b = Bar(10); var c = Bar(20);
            Rebuild();
            Assert.Equal(1, ClusterCount);
            Assert.Same(ClusterOf(a), ClusterOf(b));
            Assert.Same(ClusterOf(a), ClusterOf(c));
            Assert.Same(b, ClusterOf(a).Coordinator);
        }

        [Fact]
        public void DifferentGridOwnerOrWorkMode_SplitTheCluster()
        {
            var a = Bar(1);
            var otherGrid = Bar(2, gridId: 501);
            var otherOwner = Bar(3, owner: 78);
            var otherMode = Bar(4);
            SettingsOf(otherMode).WorkMode = WorkModes.GrindOnly;
            Rebuild();
            Assert.Equal(4, ClusterCount);
            Assert.NotSame(ClusterOf(a), ClusterOf(otherGrid));
            Assert.NotSame(ClusterOf(a), ClusterOf(otherOwner));
            Assert.NotSame(ClusterOf(a), ClusterOf(otherMode));
        }

        [Fact]
        public void DisabledBar_LeavesTheCluster_AndTheCoordinatorIsReElected()
        {
            var a = Bar(10); var b = Bar(20);
            Rebuild();
            Assert.Same(a, ClusterOf(b).Coordinator);

            a.Welder.Enabled.Returns(false); // the coordinator is switched off
            Rebuild();
            Assert.Null(ClusterOf(a));
            Assert.Same(b, ClusterOf(b).Coordinator);
            Assert.Single(ClusterOf(b).Members);
        }

        [Fact]
        public void SettingsChange_RebuildsMembership()
        {
            var a = Bar(10); var b = Bar(20);
            Rebuild();
            Assert.Same(ClusterOf(a), ClusterOf(b));

            SettingsOf(b).SearchMode = SearchModes.BoundingBox; // same BaR count, different key
            Rebuild();
            Assert.NotSame(ClusterOf(a), ClusterOf(b));
        }

        [Fact]
        public void KeyHash_FollowsTheKey()
        {
            var a = Bar(10); var b = Bar(20);
            Assert.Equal(Key(a), Key(b));
            Assert.Equal(Hash(a), Hash(b));

            SettingsOf(b).WeldOptions = AutoWeldOptions.WeldSkeleton;
            Assert.NotEqual(Key(a), Key(b));
            Assert.NotEqual(Hash(a), Hash(b));

            // Flags outside the scan-relevant mask (e.g. ShowArea) keep the cluster together.
            SettingsOf(b).WeldOptions = SettingsOf(a).WeldOptions;
            SettingsOf(b).Flags |= SyncBlockSettings.Settings.ShowArea;
            Assert.Equal(Hash(a), Hash(b));
        }

        [Fact]
        public void UnchangedBars_RebuildKeepsTheSameClusterObjects()
        {
            var a = Bar(10); Bar(20);
            Rebuild();
            var before = ClusterOf(a);
            Rebuild(); // fast path: nothing changed
            Assert.Same(before, ClusterOf(a));
        }
    }
}

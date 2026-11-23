using NSubstitute;
using SKONanobotBuildAndRepairSystem.Cluster;
using SKONanobotBuildAndRepairSystem.Models;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using VRage.Game.ModAPI;
using VRageMath;
using Xunit;
using Xunit.Abstractions;
using IMyShipWelder = Sandbox.ModAPI.IMyShipWelder;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-CLUSTER-003 / SCN-PRIO-001 — target order and fair share: PreSortClusterCandidates (weld: priority, then
    // nearest; grind: autogrind first, priority, then farthest unless GrindNearFirst) and TruncateGridAware (every grid
    // keeps a share). Perf: growth from 1,000 to 10,000 candidates stays ~n log n (fakes make absolute ms meaningless;
    // game-side cost is measured in game). Limits 40x: well above n log n (~13x) plus fake / GC noise, far below quadratic (100x).
    public class TargetSortingTests : ModTestBase
    {
        private const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;
        private readonly ITestOutputHelper _out;
        private readonly Dictionary<long, IMyCubeGrid> _grids = new Dictionary<long, IMyCubeGrid>();
        private long _nextEntity = 9000;

        public TargetSortingTests(ITestOutputHelper output)
        {
            _out = output;
        }

        private static NanobotSystem System()
        {
            var system = new NanobotSystem();
            typeof(NanobotSystem).GetField("_Settings", Inst).SetValue(system, new SyncBlockSettings(system));
            var welder = Substitute.For<IMyShipWelder>();
            welder.EntityId.Returns(1L);
            typeof(NanobotSystem).GetField("_Welder", Inst).SetValue(system, welder);
            return system;
        }

        private static SyncBlockSettings SettingsOf(NanobotSystem s)
        {
            return (SyncBlockSettings)typeof(NanobotSystem).GetField("_Settings", Inst).GetValue(s);
        }

        private IMyCubeGrid Grid(long id)
        {
            IMyCubeGrid grid;
            if (_grids.TryGetValue(id, out grid)) return grid;
            grid = Substitute.For<IMyCubeGrid>();
            grid.EntityId.Returns(id);
            grid.GridIntegerToWorld(Arg.Any<Vector3I>()).Returns(ci => (Vector3D)ci.Arg<Vector3I>() * 2.5);
            _grids[id] = grid;
            return grid;
        }

        /// <summary>A block at grid cell x (2.5 m per cell from the origin); cargo = a cargo container (higher weld priority than armor).</summary>
        private IMySlimBlock Block(int x, long gridId = 1, bool cargo = false)
        {
            var block = Substitute.For<IMySlimBlock>();
            var grid = Grid(gridId);
            block.CubeGrid.Returns(grid);
            block.Position.Returns(new Vector3I(x, 0, 0));
            if (cargo)
            {
                var fat = Substitute.For<Sandbox.ModAPI.IMyCargoContainer>();
                fat.EntityId.Returns(_nextEntity++);
                block.FatBlock.Returns(fat);
            }
            return block;
        }

        private static void PreSort(NanobotSystem s, List<ClusterTargetCandidate> grind, List<ClusterTargetCandidate> weld)
        {
            typeof(NanobotSystem).GetMethod("PreSortClusterCandidates", Inst).Invoke(s, new object[] { Vector3D.Zero, grind, weld });
        }

        private static void Truncate(NanobotSystem s, List<TargetBlockData> list, int max)
        {
            typeof(NanobotSystem).GetMethod("TruncateGridAware", Inst).Invoke(s, new object[] { list, max });
        }

        private static List<ClusterTargetCandidate> C(params IMySlimBlock[] blocks)
        {
            return blocks.Select(b => new ClusterTargetCandidate(b, 0)).ToList();
        }

        private static int[] Xs(List<ClusterTargetCandidate> list)
        {
            return list.Select(c => c.Block.Position.X).ToArray();
        }

        [Fact]
        public void Weld_SamePriority_NearestFirst()
        {
            var weld = C(Block(30), Block(10), Block(20));
            PreSort(System(), null, weld);
            Assert.Equal(new[] { 10, 20, 30 }, Xs(weld));
        }

        [Fact]
        public void Weld_HigherPriorityClassFirst_EvenWhenFarther()
        {
            var weld = C(Block(10), Block(50, cargo: true), Block(20));
            PreSort(System(), null, weld);
            Assert.Equal(new[] { 50, 10, 20 }, Xs(weld)); // cargo container class ranks above armor by default
        }

        [Fact]
        public void Grind_DefaultFarthestFirst_NearFirstFlagReverses()
        {
            var s = System();
            var grind = C(Block(10), Block(30), Block(20));
            PreSort(s, grind, null);
            Assert.Equal(new[] { 30, 20, 10 }, Xs(grind));

            SettingsOf(s).Flags |= SyncBlockSettings.Settings.GrindNearFirst;
            PreSort(s, grind, null);
            Assert.Equal(new[] { 10, 20, 30 }, Xs(grind));
        }

        [Fact]
        public void Grind_AutogrindTargetsFirst()
        {
            var auto = new ClusterTargetCandidate(Block(5), TargetBlockData.AttributeFlags.Autogrind);
            var grind = C(Block(10), Block(30));
            grind.Add(auto);
            PreSort(System(), grind, null);
            Assert.Equal(5, grind[0].Block.Position.X);
        }

        [Fact]
        public void Truncate_EveryGridKeepsAShare()
        {
            var list = new List<TargetBlockData>();
            for (int i = 0; i < 100; i++) list.Add(new TargetBlockData(Block(i, gridId: 1), i, 0)); // dominant grid, sorted first
            for (int i = 0; i < 5; i++) list.Add(new TargetBlockData(Block(i, gridId: 2), 200 + i, 0));
            for (int i = 0; i < 5; i++) list.Add(new TargetBlockData(Block(i, gridId: 3), 300 + i, 0));
            Truncate(System(), list, 20);
            Assert.Equal(20, list.Count);
            Assert.Equal(5, list.Count(t => t.Block.CubeGrid.EntityId == 2));
            Assert.Equal(5, list.Count(t => t.Block.CubeGrid.EntityId == 3));
            Assert.Equal(10, list.Count(t => t.Block.CubeGrid.EntityId == 1));
            // Grid 1 keeps its best (first) entries.
            Assert.Equal(Enumerable.Range(0, 10), list.Where(t => t.Block.CubeGrid.EntityId == 1).Select(t => t.Block.Position.X).OrderBy(x => x));
        }

        [Fact]
        public void Truncate_SingleGrid_KeepsTheFirstEntries()
        {
            var list = new List<TargetBlockData>();
            for (int i = 0; i < 50; i++) list.Add(new TargetBlockData(Block(i), i, 0));
            Truncate(System(), list, 10);
            Assert.Equal(Enumerable.Range(0, 10), list.Select(t => t.Block.Position.X));
        }

        // ---- growth checks (1,000 / 2,000 / 5,000 / 10,000) ----

        private List<IMySlimBlock> Blocks(int n, int grids)
        {
            var list = new List<IMySlimBlock>(n);
            for (int i = 0; i < n; i++) list.Add(Block((i * 7919) % n, gridId: 1 + i % grids, cargo: i % 50 == 0));
            return list;
        }

        private static double BestMs(System.Action action)
        {
            var best = double.MaxValue;
            for (int r = 0; r < 3; r++)
            {
                var sw = Stopwatch.StartNew();
                action();
                sw.Stop();
                if (sw.Elapsed.TotalMilliseconds < best) best = sw.Elapsed.TotalMilliseconds;
            }
            return best;
        }

        [Fact]
        public void PreSort_GrowsLikeNLogN_From1000To10000()
        {
            var all = Blocks(10000, 5);
            var s = System();
            PreSort(s, null, C(all.Take(500).ToArray())); // warm-up (JIT, priority cache)
            var ms = new Dictionary<int, double>();
            foreach (var n in new[] { 1000, 2000, 5000, 10000 })
            {
                var source = all.Take(n).ToArray();
                ms[n] = BestMs(() => PreSort(s, C(source), C(source)));
                _out.WriteLine("PreSort {0,6} candidates (weld + grind): {1,8:F1} ms (fakes)", n, ms[n]);
            }
            var ratio = ms[10000] / ms[1000];
            _out.WriteLine("10k / 1k = {0:F1} (n log n ~ 13, quadratic ~ 100)", ratio);
            Assert.True(ratio < 40, "PreSort grows faster than n log n: 10k / 1k = " + ratio.ToString("F1"));
        }

        [Fact]
        public void Truncate_GrowsLinearly_From1000To10000()
        {
            var all = Blocks(10000, 10);
            var s = System();
            var ms = new Dictionary<int, double>();
            foreach (var n in new[] { 1000, 2000, 5000, 10000 })
            {
                var source = all.Take(n).Select((blk, i) => new TargetBlockData(blk, i, 0)).ToList();
                var best = double.MaxValue;
                Truncate(s, new List<TargetBlockData>(source), 512); // warm-up, not timed
                for (int r = 0; r < 7; r++)
                {
                    var copy = new List<TargetBlockData>(source); // not timed
                    var sw = Stopwatch.StartNew();
                    Truncate(s, copy, 512);
                    sw.Stop();
                    if (sw.Elapsed.TotalMilliseconds < best) best = sw.Elapsed.TotalMilliseconds;
                }
                ms[n] = best;
                _out.WriteLine("TruncateGridAware {0,6} targets -> 512: {1,8:F2} ms (fakes)", n, ms[n]);
            }
            // Quadratic growth would be ~100x for 10x the targets. The 1k time is tiny (sub-millisecond), so a scheduler
            // hiccup there made the ratio flaky under a loaded test run: floor it at 0.2 ms, best of 7 after a warm-up.
            var ratio = ms[10000] / global::System.Math.Max(ms[1000], 0.2);
            Assert.True(ratio < 40, "TruncateGridAware grows faster than linear: 10k / 1k = " + ratio.ToString("F1"));
        }
    }
}

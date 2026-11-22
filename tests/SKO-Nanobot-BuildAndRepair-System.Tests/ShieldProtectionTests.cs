using DefenseShields;
using NSubstitute;
using System;
using System.Collections.Generic;
using System.Reflection;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using Xunit;
using IMyShipWelder = Sandbox.ModAPI.IMyShipWelder;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-ZONE-001 — BUG-261122.12: a block counts as shield-protected only while its grid's shield is up.
    // DefenseShields' IsBlockProtected answers "inside the ellipsoid" even when the shield is off (harness S59).
    public class ShieldProtectionTests : ModTestBase
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private bool _inside = true;
        private bool _online = true;
        private int _blockCalls;
        private int _gridCalls;

        public ShieldProtectionTests()
        {
            var api = new ShieldApi();
            var delegates = new Dictionary<string, Delegate>
            {
                { "IsBlockProtected", new Func<IMySlimBlock, bool>(b => { _blockCalls++; return _inside; }) },
                { "GridShieldOnline", new Func<IMyCubeGrid, bool>(g => { _gridCalls++; return _online; }) },
                { "ProtectedByShield", new Func<IMyEntity, bool>(e => _online) },
            };
            api.ApiLoad(delegates);
            typeof(ShieldApi).GetField("_isReady", Any).SetValue(api, true);
            Mod.Shield = api;
        }

        public override void Dispose()
        {
            Mod.Shield = null;
            base.Dispose();
        }

        private static IMyCubeGrid Grid(long id)
        {
            var g = Substitute.For<IMyCubeGrid>();
            g.EntityId.Returns(id);
            return g;
        }

        private static bool Protected(long barGrid, long blockGrid)
        {
            var system = new NanobotSystem();
            var welder = Substitute.For<IMyShipWelder>();
            var g = Grid(barGrid);
            welder.CubeGrid.Returns(g);
            typeof(NanobotSystem).GetField("_Welder", Any).SetValue(system, welder);
            var block = Substitute.For<IMySlimBlock>();
            var bg = Grid(blockGrid);
            block.CubeGrid.Returns(bg);
            return system.IsShieldProtected(block);
        }

        [Fact]
        public void ShieldUp_BlockInside_IsProtected()
        {
            Assert.True(Protected(1, 2));
        }

        [Fact]
        public void ShieldDown_BlockInsideTheEllipsoid_IsNotProtected()
        {
            _online = false; // controller off / lowered / drained: DS still reports "inside"
            Assert.False(Protected(1, 2));
        }

        [Fact]
        public void BlockOutsideTheShield_IsNotProtected()
        {
            _inside = false;
            Assert.False(Protected(1, 2));
        }

        [Fact]
        public void OwnGrid_IsNeverProtected()
        {
            Assert.False(Protected(1, 1));
        }

        /// <summary>One system scanning many blocks of a few grids (scan order: grid by grid).</summary>
        private bool[] Scan(params long[] blockGrids)
        {
            var system = new NanobotSystem();
            var welder = Substitute.For<IMyShipWelder>();
            var own = Grid(1);
            welder.CubeGrid.Returns(own);
            typeof(NanobotSystem).GetField("_Welder", Any).SetValue(system, welder);
            var grids = new Dictionary<long, IMyCubeGrid>();
            var result = new bool[blockGrids.Length];
            for (int i = 0; i < blockGrids.Length; i++)
            {
                IMyCubeGrid g;
                if (!grids.TryGetValue(blockGrids[i], out g)) grids[blockGrids[i]] = g = Grid(blockGrids[i]);
                var block = Substitute.For<IMySlimBlock>();
                block.CubeGrid.Returns(g);
                result[i] = system.IsShieldProtected(block);
            }
            return result;
        }

        // Performance: the grid's shield state is asked once per grid, not once per block.
        [Fact]
        public void ShieldUp_GridStateAskedOncePerGrid()
        {
            var r = Scan(2, 2, 2, 2, 2, 3, 3, 3);
            Assert.All(r, Assert.True);
            Assert.Equal(2, _gridCalls);
            Assert.Equal(8, _blockCalls); // per block: is it inside the ellipsoid
        }

        // Performance: a grid with its shield down (or none) costs no per-block DS call.
        [Fact]
        public void ShieldDown_NoPerBlockCalls()
        {
            _online = false;
            var r = Scan(2, 2, 2, 2, 2, 2);
            Assert.All(r, Assert.False);
            Assert.Equal(1, _gridCalls);
            Assert.Equal(0, _blockCalls);
        }

        // The memo expires: a shield raised later is seen within a second.
        [Fact]
        public void GridState_IsRefreshedAfterOneSecond()
        {
            var system = new NanobotSystem();
            var welder = Substitute.For<IMyShipWelder>();
            var own = Grid(1);
            welder.CubeGrid.Returns(own);
            typeof(NanobotSystem).GetField("_Welder", Any).SetValue(system, welder);
            var g = Grid(2);
            var block = Substitute.For<IMySlimBlock>();
            block.CubeGrid.Returns(g);

            _online = false;
            Assert.False(system.IsShieldProtected(block));
            _online = true;
            Assert.False(system.IsShieldProtected(block)); // still memoised
            Now = Now + TimeSpan.FromSeconds(1.1);
            Assert.True(system.IsShieldProtected(block));
        }

        [Fact]
        public void ShieldCheckDisabled_NothingIsProtected()
        {
            Mod.Settings.ShieldCheckEnabled = false;
            Assert.False(Protected(1, 2));
        }
    }
}

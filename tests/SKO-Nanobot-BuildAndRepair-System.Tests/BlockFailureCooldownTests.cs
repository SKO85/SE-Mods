using NSubstitute;
using SKONanobotBuildAndRepairSystem.Handlers;
using System;
using VRage.Game.ModAPI;
using VRageMath;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-WELD-008 — BUG-261123.1: a "no components" mark is shared only with BaRs on the same supply (logical grid
    // group). A BaR on another ship ignores it; BaRs sharing the supply still skip the block until it expires.
    public class BlockFailureCooldownTests : ModTestBase
    {
        private readonly object _shipA = new object();
        private readonly object _shipB = new object();

        private static IMySlimBlock Block()
        {
            var grid = Substitute.For<IMyCubeGrid>();
            grid.EntityId.Returns(4242L);
            var block = Substitute.For<IMySlimBlock>();
            block.CubeGrid.Returns(grid);
            block.Position.Returns(new Vector3I(1, 2, 3));
            return block;
        }

        [Fact]
        public void Mark_AppliesToTheSameSupply()
        {
            var block = Block();
            BlockFailureCooldownHandler.MarkFailed(block, _shipA);
            Assert.True(BlockFailureCooldownHandler.IsOnCooldown(block, _shipA));
        }

        [Fact]
        public void Mark_IsIgnoredByABarOnAnotherSupply()
        {
            var block = Block();
            BlockFailureCooldownHandler.MarkFailed(block, _shipA);
            Assert.False(BlockFailureCooldownHandler.IsOnCooldown(block, _shipB));
        }

        [Fact]
        public void Mark_Expires()
        {
            var block = Block();
            BlockFailureCooldownHandler.MarkFailed(block, _shipA);
            Now += TimeSpan.FromSeconds(Mod.Settings.BlockFailureCooldownSeconds + 1);
            Assert.False(BlockFailureCooldownHandler.IsOnCooldown(block, _shipA));
        }
    }
}

using NSubstitute;
using SKONanobotBuildAndRepairSystem.Handlers;
using System;
using VRage.Game.ModAPI;
using VRageMath;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-BUDGET-001 — block claims (BlockSystemAssigningHandler): one BaR per block, refreshed while it works, refused
    // to others, expired after AssignmentTtlSeconds, released only by the owner, released all at once when a BaR closes.
    public class BlockAssignmentTests : ModTestBase
    {
        private const long BarA = 1001, BarB = 1002;

        private static IMySlimBlock Block(long gridId, int x)
        {
            var grid = Substitute.For<IMyCubeGrid>();
            grid.EntityId.Returns(gridId);
            var block = Substitute.For<IMySlimBlock>();
            block.CubeGrid.Returns(grid);
            block.Position.Returns(new Vector3I(x, 0, 0));
            return block;
        }

        [Fact]
        public void FreeBlock_IsClaimed_AndRefusedToAnotherBar()
        {
            var block = Block(10, 1);
            Assert.True(block.AssignToSystem(BarA));
            Assert.False(block.AssignToSystem(BarB));
            Assert.True(block.IsAssignedToOtherSystem(BarB));
            Assert.False(block.IsAssignedToOtherSystem(BarA));
        }

        [Fact]
        public void SameBlock_IsMatchedByGridAndPosition_NotByReference()
        {
            Assert.True(Block(10, 1).AssignToSystem(BarA));
            Assert.True(Block(10, 1).IsAssignedToOtherSystem(BarB)); // new reference, same block (scan rebuilds)
            Assert.False(Block(10, 2).IsAssignedToOtherSystem(BarB)); // other cell
            Assert.False(Block(11, 1).IsAssignedToOtherSystem(BarB)); // other grid
        }

        [Fact]
        public void Claim_ExpiresAfterTheTtl_UnlessTheOwnerRefreshesIt()
        {
            var ttl = TimeSpan.FromSeconds(Mod.Settings.AssignmentTtlSeconds);
            var block = Block(10, 1);
            block.AssignToSystem(BarA);

            Now += ttl - TimeSpan.FromSeconds(1);
            Assert.True(block.AssignToSystem(BarA)); // refresh while welding
            Now += ttl - TimeSpan.FromSeconds(1);
            Assert.True(block.IsAssignedToOtherSystem(BarB)); // still A's thanks to the refresh

            Now += TimeSpan.FromSeconds(2);
            Assert.False(block.IsAssignedToOtherSystem(BarB)); // A stopped refreshing: expired
            Assert.True(block.AssignToSystem(BarB));
        }

        [Fact]
        public void Release_OnlyByTheOwner()
        {
            var block = Block(10, 1);
            block.AssignToSystem(BarA);
            block.ReleaseFromSystem(BarB); // stale lock-on of another BaR must not free A's claim
            Assert.True(block.IsAssignedToOtherSystem(BarB));
            block.ReleaseFromSystem(BarA);
            Assert.False(block.IsAssignedToOtherSystem(BarB));
        }

        [Fact]
        public void ReleaseAllForSystem_FreesOnlyThatBarsClaims()
        {
            var a1 = Block(10, 1);
            var a2 = Block(10, 2);
            var b1 = Block(10, 3);
            a1.AssignToSystem(BarA);
            a2.AssignToSystem(BarA);
            b1.AssignToSystem(BarB);

            Assert.Equal(2, BlockSystemAssigningHandler.ReleaseAllForSystem(BarA));
            Assert.False(a1.IsAssignedToOtherSystem(BarB));
            Assert.False(a2.IsAssignedToOtherSystem(BarB));
            Assert.True(b1.IsAssignedToOtherSystem(BarA));
        }
    }
}

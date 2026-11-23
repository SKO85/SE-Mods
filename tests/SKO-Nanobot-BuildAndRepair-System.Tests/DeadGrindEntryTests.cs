using NSubstitute;
using SKONanobotBuildAndRepairSystem.Handlers;
using SKONanobotBuildAndRepairSystem.Models;
using System.Reflection;
using VRage.Game.ModAPI;
using VRageMath;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-GRIND-007 — BUG-261123.3: ground / removed grind entries are dropped from the list once (fully dismounted ones
    // queued for raze) instead of being re-walked every tick; live and partly ground entries stay.
    public class DeadGrindEntryTests : ModTestBase
    {
        private static bool IsDead(TargetBlockData t)
        {
            return (bool)typeof(NanobotSystem).GetMethod("IsDeadGrindEntry", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { t });
        }

        private static IMySlimBlock Block(int x, bool dismounted, bool closed = false, bool fat = false)
        {
            var grid = Substitute.For<IMyCubeGrid>();
            grid.EntityId.Returns(77L);
            var block = Substitute.For<IMySlimBlock>();
            block.CubeGrid.Returns(grid);
            block.Position.Returns(new Vector3I(x, 0, 0));
            block.IsFullyDismounted.Returns(dismounted);
            if (fat)
            {
                var fb = Substitute.For<IMyCubeBlock>();
                fb.Closed.Returns(closed);
                block.FatBlock.Returns(fb);
            }
            return block;
        }

        [Fact]
        public void LiveAndPartlyGround_Stay()
        {
            Assert.False(IsDead(new TargetBlockData(Block(1, false), 10, 0)));
            Assert.False(IsDead(new TargetBlockData(Block(2, false, false, true), 10, 0)));
        }

        [Fact]
        public void Gone_IsDropped()
        {
            Assert.True(IsDead(null));
            Assert.True(IsDead(new TargetBlockData(null, 10, 0)));
            Assert.True(IsDead(new TargetBlockData(Block(3, false, true, true), 10, 0)));
        }

        [Fact]
        public void FullyDismounted_IsDropped_AndQueuedForRazeOnce()
        {
            RazeQueueHandler.Clear();
            var block = Block(4, true);
            Assert.True(IsDead(new TargetBlockData(block, 10, 0)));
            Assert.True(IsDead(new TargetBlockData(block, 10, 0)));
            Assert.Equal(1, RazeQueueHandler.GetQueueDepth());
            RazeQueueHandler.Clear();
        }

        [Fact]
        public void RemoveDeadGrindEntries_KeepsLiveEntriesInOrder()
        {
            RazeQueueHandler.Clear();
            var list = new System.Collections.Generic.List<TargetBlockData>
            {
                new TargetBlockData(Block(1, false), 1, 0),
                new TargetBlockData(Block(2, true), 2, 0),
                new TargetBlockData(Block(3, false, true, true), 3, 0),
                new TargetBlockData(Block(4, false), 4, 0),
            };
            var removed = (int)typeof(NanobotSystem).GetMethod("RemoveDeadGrindEntries", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { list });
            Assert.Equal(2, removed);
            Assert.Equal(2, list.Count);
            Assert.Equal(new Vector3I(1, 0, 0), list[0].Block.Position);
            Assert.Equal(new Vector3I(4, 0, 0), list[1].Block.Position);
            RazeQueueHandler.Clear();
        }
    }
}

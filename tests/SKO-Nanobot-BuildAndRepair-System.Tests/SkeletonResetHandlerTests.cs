using NSubstitute;
using SKONanobotBuildAndRepairSystem.Handlers;
using System;
using System.Collections;
using System.Reflection;
using VRage.Game.ModAPI;
using VRageMath;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-WELD-006 — BUG-261023.3 skeleton resets only for really dented blocks, bounded per tick.
    public class SkeletonResetHandlerTests : ModTestBase
    {
        private static int _pos;

        public SkeletonResetHandlerTests()
        {
            SkeletonResetHandler.Clear();
        }

        public override void Dispose()
        {
            SkeletonResetHandler.Clear();
            base.Dispose();
        }

        private static IMySlimBlock Block(bool deformed)
        {
            var grid = Substitute.For<IMyCubeGrid>();
            grid.EntityId.Returns(1L);
            var block = Substitute.For<IMySlimBlock>();
            block.CubeGrid.Returns(grid);
            block.Position.Returns(new Vector3I(_pos++, 0, 0));
            block.HasDeformation.Returns(deformed);
            return block;
        }

        private static int QueueCount()
        {
            var q = (ICollection)typeof(SkeletonResetHandler).GetField("_queue", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            return q.Count;
        }

        private static int Resets(IMySlimBlock[] blocks)
        {
            var n = 0;
            foreach (var b in blocks)
                foreach (var call in b.ReceivedCalls())
                    if (call.GetMethodInfo().Name == "FixBones") n++;
            return n;
        }

        [Fact]
        public void NotDeformedBlocks_AreSkipped_AndGetTheCooldown()
        {
            var blocks = new IMySlimBlock[20];
            for (var i = 0; i < blocks.Length; i++) { blocks[i] = Block(false); SkeletonResetHandler.Enqueue(blocks[i]); }

            SkeletonResetHandler.Process();
            Assert.Equal(0, Resets(blocks));
            Assert.Equal(4, QueueCount()); // 16 dequeues per tick

            // Skipped block is on cooldown: re-enqueue is ignored.
            SkeletonResetHandler.Enqueue(blocks[0]);
            Assert.Equal(4, QueueCount());

            // ...until the cooldown expires.
            Now += TimeSpan.FromSeconds(61);
            SkeletonResetHandler.Enqueue(blocks[0]);
            Assert.Equal(5, QueueCount());
        }

        [Fact]
        public void DeformedBlocks_AreReset_TwoPerTick()
        {
            var blocks = new IMySlimBlock[5];
            for (var i = 0; i < blocks.Length; i++) { blocks[i] = Block(true); SkeletonResetHandler.Enqueue(blocks[i]); }

            SkeletonResetHandler.Process();
            Assert.Equal(2, Resets(blocks));
            Assert.Equal(3, QueueCount());

            Frame++;
            SkeletonResetHandler.Process();
            Assert.Equal(4, Resets(blocks));
        }

        [Fact]
        public void MixedQueue_SkipsDoNotCountAgainstTheResetCap()
        {
            var blocks = new IMySlimBlock[6];
            for (var i = 0; i < 4; i++) { blocks[i] = Block(false); SkeletonResetHandler.Enqueue(blocks[i]); }
            for (var i = 4; i < 6; i++) { blocks[i] = Block(true); SkeletonResetHandler.Enqueue(blocks[i]); }

            SkeletonResetHandler.Process();
            Assert.Equal(2, Resets(blocks));
            Assert.Equal(0, QueueCount());
        }
    }
}

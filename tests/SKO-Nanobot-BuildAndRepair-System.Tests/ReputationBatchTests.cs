using SKONanobotBuildAndRepairSystem.Utils;
using System;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-GRIND-002 — FEAT-261123.5: grind reputation hits are queued per (BaR owner, target owner) and applied in one
    // total every 2 s; nothing lost, nothing applied twice, a world unload drops the queue.
    public class ReputationBatchTests : ModTestBase
    {
        [Fact]
        public void Hits_AreCountedPerOwnerPair()
        {
            UtilsFaction.Reset();
            UtilsFaction.QueueReputationDamage(1, 10);
            UtilsFaction.QueueReputationDamage(1, 10);
            UtilsFaction.QueueReputationDamage(1, 11);
            UtilsFaction.QueueReputationDamage(2, 10);
            Assert.Equal(2, UtilsFaction.PendingHits(1, 10));
            Assert.Equal(1, UtilsFaction.PendingHits(1, 11));
            Assert.Equal(1, UtilsFaction.PendingHits(2, 10));
            UtilsFaction.Reset();
        }

        [Fact]
        public void Flush_EmptiesTheQueue_OnlyEveryTwoSeconds()
        {
            UtilsFaction.Reset();
            UtilsFaction.QueueReputationDamage(1, 10);
            UtilsFaction.Flush(TimeSpan.FromSeconds(100)); // applies (no factions in the fake session: no-op) and clears
            Assert.Equal(0, UtilsFaction.PendingHits(1, 10));
            UtilsFaction.QueueReputationDamage(1, 10);
            UtilsFaction.Flush(TimeSpan.FromSeconds(101)); // within 2 s: kept
            Assert.Equal(1, UtilsFaction.PendingHits(1, 10));
            UtilsFaction.Flush(TimeSpan.FromSeconds(102));
            Assert.Equal(0, UtilsFaction.PendingHits(1, 10));
            UtilsFaction.Reset();
        }

        [Fact]
        public void Reset_DropsQueuedHits()
        {
            UtilsFaction.QueueReputationDamage(3, 30);
            UtilsFaction.Reset();
            Assert.Equal(0, UtilsFaction.PendingHits(3, 30));
        }
    }
}

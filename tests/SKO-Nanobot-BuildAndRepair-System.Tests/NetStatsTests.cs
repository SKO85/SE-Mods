using SKONanobotBuildAndRepairSystem.Handlers;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-BUDGET-010 — FEAT-261123.7: network counters per message type (sent / wire / largest / received).
    [Collection("NetStats")]
    public class NetStatsTests
    {
        [Fact]
        public void Index_RoundTrips_AllModMessageIds()
        {
            foreach (ushort id in new ushort[] { 40000, 40001, 40002, 40003, 40004, 40005, 40100, 40102, 40103, 40104 })
            {
                var i = NetStats.Index(id);
                Assert.InRange(i, 0, NetStats.Size - 1);
                Assert.Equal(id, NetStats.IdOf(i));
            }
            Assert.Equal(-1, NetStats.Index(12345));
        }

        [Fact]
        public void Sent_CountsPayloadWireAndLargest()
        {
            NetStats.ResetForTests();
            NetStats.Sent(40104, 1000, 3);
            NetStats.Sent(40104, 4000, 3);
            NetStats.Sent(40102, 200, 0);
            NetStats.Received(40103, 50);
            var s = NetStats.Take(false);
            var st = NetStats.Index(40104);
            Assert.Equal(2, s.SentMsgs[st]);
            Assert.Equal(5000, s.SentBytes[st]);
            Assert.Equal(15000, s.WireBytes[st]);
            Assert.Equal(4000, s.MaxSent[st]);
            Assert.Equal(0, s.WireBytes[NetStats.Index(40102)]);
            Assert.Equal(1, s.RecvMsgs[NetStats.Index(40103)]);
            Assert.Equal(50, s.RecvBytes[NetStats.Index(40103)]);
        }

        [Fact]
        public void Take_ResetMax_StartsANewLargestWindow()
        {
            NetStats.ResetForTests();
            NetStats.Sent(40104, 9000, 1);
            Assert.Equal(9000, NetStats.Take(true).MaxSent[NetStats.Index(40104)]);
            NetStats.Sent(40104, 100, 1);
            var s = NetStats.Take(false);
            Assert.Equal(100, s.MaxSent[NetStats.Index(40104)]);
            Assert.Equal(2, s.SentMsgs[NetStats.Index(40104)]); // counters are cumulative
        }

        [Fact]
        public void UnknownIds_AreIgnored()
        {
            NetStats.ResetForTests();
            NetStats.Sent(1, 100, 1);
            NetStats.Received(60000, 100);
            var s = NetStats.Take(false);
            long total = 0;
            for (int i = 0; i < NetStats.Size; i++) total += s.SentMsgs[i] + s.RecvMsgs[i];
            Assert.Equal(0, total);
        }
    }
}

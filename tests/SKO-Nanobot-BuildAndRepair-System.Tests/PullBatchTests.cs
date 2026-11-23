using System.Reflection;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-INV-001 — FEAT-261123.3: a conveyor transfer fetches for up to 3 blocks, only from a well-stocked source,
    // held to 10 % of the BaR's inventory while it is below half full.
    public class PullBatchTests
    {
        private static int Extra(int needed, int forBlock, int available, int maxByVolume)
        {
            return (int)typeof(NanobotSystem).GetMethod("PullBatchExtra", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { needed, forBlock, available, maxByVolume });
        }

        private static int ByVolume(float max, float current, float item)
        {
            return (int)typeof(NanobotSystem).GetMethod("BatchExtraByVolume", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(float), typeof(float), typeof(float) }, null).Invoke(null, new object[] { max, current, item });
        }

        [Fact]
        public void WellStockedSource_FetchesForTwoMoreBlocks()
        {
            // 20 plates per armor block, 500 in the container.
            Assert.Equal(40, Extra(20, 20, 500, 1000));
        }

        [Fact]
        public void ThinSource_KeepsTwiceTheExtraBehind()
        {
            Assert.Equal(10, Extra(20, 20, 50, 1000)); // spare 30 -> extra 10, 20 left
            Assert.Equal(0, Extra(20, 20, 22, 1000));  // spare 2 -> nothing extra
            Assert.Equal(0, Extra(20, 16, 16, 1000));  // source can't even cover the block
        }

        [Fact]
        public void Extra_IsCappedByVolume()
        {
            Assert.Equal(7, Extra(20, 20, 500, 7));
            Assert.Equal(0, Extra(20, 20, 500, 0));
        }

        [Fact]
        public void VolumeCap_TenPercent_OnlyBelowHalfFull()
        {
            // Large BaR 15.625 m3, steel plate 0.003 m3: 10 % = 1.5625 m3 = 520 plates.
            Assert.Equal(520, ByVolume(15.625f, 1f, 0.003f));
            Assert.Equal(0, ByVolume(15.625f, 8f, 0.003f));   // half full or more: no extra stock
            Assert.Equal(0, ByVolume(15.625f, 1f, 0f));
        }
    }
}

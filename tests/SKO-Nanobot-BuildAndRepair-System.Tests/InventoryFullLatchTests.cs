using System.Reflection;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-INV-002 — BUG-261123.2: the "Inventory full" latch is set from 98% of the welder volume (ore rarely fills the
    // last bit exactly) and cleared below 90%.
    public class InventoryFullLatchTests
    {
        private static bool Next(bool wasFull, float current, float max)
        {
            return (bool)typeof(NanobotSystem).GetMethod("NextInventoryFull", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { wasFull, current, max });
        }

        [Fact]
        public void SetsFrom98Percent()
        {
            Assert.False(Next(false, 15.2f, 15.625f)); // 97.3%
            Assert.True(Next(false, 15.47f, 15.625f)); // 99.0%: S89 stuck point
            Assert.True(Next(false, 15.625f, 15.625f));
        }

        [Fact]
        public void ClearsBelow90Percent_WithHysteresis()
        {
            Assert.True(Next(true, 14.5f, 15.625f));  // 92.8%: stays full
            Assert.False(Next(true, 14.0f, 15.625f)); // 89.6%: clears
        }

        [Fact]
        public void UnknownCapacity_KeepsTheState()
        {
            Assert.True(Next(true, 0f, 0f));
            Assert.False(Next(false, 0f, 0f));
        }
    }
}

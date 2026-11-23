using System;
using System.Reflection;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-CFG-001 — BUG-261123.9: the nanobot transport speed scales with the mod Range as a float (Range 20 m -> 0.2,
    // was int / int = 0 -> speed 0 -> "TimeSpan overflowed" on every delivery); the delivery time is safe for a zero speed.
    public class TransportTimeTests
    {
        private static float Scale(int range)
        {
            return (float)typeof(NanobotSystem).GetMethod("TransportRangeScale", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { range });
        }

        private static TimeSpan Time(double distance, float speed)
        {
            return (TimeSpan)typeof(NanobotSystem).GetMethod("TransportTime", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { distance, speed });
        }

        [Theory]
        [InlineData(2, 0.02f)]
        [InlineData(20, 0.2f)]
        [InlineData(100, 1f)]
        [InlineData(150, 1.5f)]
        [InlineData(400, 4f)]
        [InlineData(2000, 4f)]
        public void RangeScale_IsProportional_CappedAt4(int range, float expected)
        {
            Assert.Equal(expected, Scale(range), 3);
        }

        [Fact]
        public void TransportTime_TwiceTheDistanceOverSpeed()
        {
            Assert.Equal(0.4, Time(10, 50).TotalSeconds, 3);
        }

        [Fact]
        public void TransportTime_ZeroOrBadInput_DoesNotThrow()
        {
            Assert.Equal(60.0, Time(10, 0f).TotalSeconds, 3);       // was Infinity -> OverflowException
            Assert.Equal(TimeSpan.Zero, Time(double.NaN, 50f));
            Assert.Equal(TimeSpan.Zero, Time(-5, 50f));
            Assert.Equal(60.0, Time(1e12, 50f).TotalSeconds, 3);    // capped
        }
    }
}

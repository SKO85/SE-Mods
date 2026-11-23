using System.Reflection;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-WELD-009 — BUG-261123.6: one pull looks at 16 sources at most; a weld miss parks the block for the supply
    // group only after a full round of empty sources since the BaR's last pick.
    public class PullRoundTests
    {
        private static MethodInfo Method(string name)
        {
            return typeof(NanobotSystem).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        }

        private static int Next(int current, bool pulled, int visited)
        {
            return (int)Method("NextEmptyPullVisits").Invoke(null, new object[] { current, pulled, visited });
        }

        private static bool Complete(int emptyVisits, int available)
        {
            return (bool)Method("IsPullRoundComplete").Invoke(null, new object[] { emptyVisits, available });
        }

        [Fact]
        public void PartialMiss_DoesNotCompleteTheRound()
        {
            // S105: 351 sources, a pull looks at 16.
            var visits = Next(0, false, 16);
            Assert.Equal(16, visits);
            Assert.False(Complete(visits, 351));
        }

        [Fact]
        public void FullEmptyRound_Completes()
        {
            var visits = 0;
            for (int i = 0; i < 22; i++) visits = Next(visits, false, 16);
            Assert.True(Complete(visits, 351)); // 352 >= 351
        }

        [Fact]
        public void Pick_ResetsTheCount()
        {
            var visits = Next(Next(0, false, 16), false, 16);
            Assert.Equal(0, Next(visits, true, 3));
        }

        [Fact]
        public void NoSources_IsAFullRound()
        {
            Assert.True(Complete(0, 0));
            Assert.True(Complete(Next(0, false, 0), 0));
        }

        [Fact]
        public void Count_DoesNotOverflow()
        {
            Assert.Equal(int.MaxValue, Next(int.MaxValue - 5, false, 16));
        }
    }
}

using System.Reflection;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-WELD-002 — BUG-261122.6: the welder's world-limit pre-check tests its builder; it only applies when the
    // builder is the owner we build as. Otherwise (transferred BaR, unknown builder) the game's own check decides.
    public class LimitsPreCheckTests
    {
        private static bool Applies(long builtBy, long owner)
        {
            return (bool)typeof(NanobotSystem).GetMethod("LimitsPreCheckApplies", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { builtBy, owner });
        }

        [Fact]
        public void BuilderIsOwner_PreCheckApplies()
        {
            Assert.True(Applies(144115188075855895L, 144115188075855895L));
        }

        [Fact]
        public void TransferredBar_GameDecides()
        {
            Assert.False(Applies(144115188075855895L, 144115188075855900L));
        }

        [Fact]
        public void UnknownBuilder_GameDecides()
        {
            Assert.False(Applies(0L, 144115188075855895L));
        }
    }
}

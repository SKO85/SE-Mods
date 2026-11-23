using SKONanobotBuildAndRepairSystem.Handlers;
using System.Reflection;
using VRageMath;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-BUDGET-010 — FEAT-261123.8: BaR state goes only to players within the sync distance (+ 500 m margin).
    public class StateRangeTests
    {
        private static MethodInfo M(string name)
        {
            return typeof(NetworkMessagingHandler).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        }

        private static double Range(int sync) { return (double)M("StateRange").Invoke(null, new object[] { sync }); }
        private static bool InRange(Vector3D p, Vector3D b, double r) { return (bool)M("InStateRange").Invoke(null, new object[] { p, b, r }); }

        [Fact]
        public void Range_IsSyncDistancePlusMargin_DefaultingTo3Km()
        {
            Assert.Equal(3500.0, Range(3000));
            Assert.Equal(10500.0, Range(10000));
            Assert.Equal(3500.0, Range(0));
        }

        [Fact]
        public void PlayersInsideTheRangeGetState_FarOnesDoNot()
        {
            var bar = new Vector3D(1000, 0, 0);
            Assert.True(InRange(new Vector3D(1000, 0, 3400), bar, 3500));
            Assert.True(InRange(new Vector3D(1000, 0, 3500), bar, 3500));
            Assert.False(InRange(new Vector3D(1000, 0, 3600), bar, 3500));
            Assert.False(InRange(new Vector3D(1000, 0, -10000), bar, 3500));
        }
    }
}

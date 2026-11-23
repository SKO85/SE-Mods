using NSubstitute;
using SKONanobotBuildAndRepairSystem.Extensions;
using System.Reflection;
using VRage.Game.ModAPI;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-CLUSTER-004 — FEAT-261123.1: the scan reads a block's repair state once; results match NeedRepair(allowMutate:
    // false) plus the former dented-only condition for every state.
    public class WeldScanClassifyTests
    {
        private static void Classify(IMySlimBlock b, AutoWeldOptions o, out bool need, out bool dented)
        {
            var args = new object[] { b, o, false, false };
            typeof(NanobotSystem).GetMethod("ClassifyForWeldScan", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static).Invoke(null, args);
            need = (bool)args[2];
            dented = (bool)args[3];
        }

        private static IMySlimBlock Block(float integrity, float max, float maxDeformation, bool hasDeformation, bool destroyed = false)
        {
            var b = Substitute.For<IMySlimBlock>();
            b.Integrity.Returns(integrity);
            b.MaxIntegrity.Returns(max);
            b.MaxDeformation.Returns(maxDeformation);
            b.HasDeformation.Returns(hasDeformation);
            b.IsDestroyed.Returns(destroyed);
            return b;
        }

        private static bool OldDentedOnly(IMySlimBlock b, AutoWeldOptions o)
        {
            return o != AutoWeldOptions.WeldSkeleton && !b.IsDestroyed && b.Integrity >= b.GetRequiredIntegrity(o)
                && b.MaxDeformation > SlimBlockExtensions.MinDeformation;
        }

        [Theory]
        [InlineData(50f, 100f, 0f, false, false)]   // damaged
        [InlineData(100f, 100f, 0f, false, false)]  // intact
        [InlineData(100f, 100f, 0f, true, false)]   // full, dented (bone check)
        [InlineData(100f, 100f, 0.5f, true, false)] // full, latched MaxDeformation
        [InlineData(50f, 100f, 0.5f, true, false)]  // damaged and latched
        [InlineData(50f, 100f, 0f, false, true)]    // destroyed
        public void MatchesNeedRepairAndTheOldDentedCheck(float integrity, float max, float maxDef, bool hasDef, bool destroyed)
        {
            foreach (var o in new[] { AutoWeldOptions.WeldFull, AutoWeldOptions.WeldSkeleton })
            {
                var b = Block(integrity, max, maxDef, hasDef, destroyed);
                bool need, dented;
                Classify(b, o, out need, out dented);
                Assert.Equal(b.NeedRepair(o, false), need);
                Assert.Equal(!need && OldDentedOnly(b, o), dented);
            }
        }

        [Fact]
        public void IntactBlock_ReadsDeformationOnce()
        {
            var b = Block(100f, 100f, 0f, false);
            bool need, dented;
            Classify(b, AutoWeldOptions.WeldFull, out need, out dented);
            var calls = 0;
            foreach (var c in b.ReceivedCalls()) if (c.GetMethodInfo().Name == "get_HasDeformation") calls++;
            Assert.Equal(1, calls);
            Assert.False(need);
            Assert.False(dented);
        }
    }
}

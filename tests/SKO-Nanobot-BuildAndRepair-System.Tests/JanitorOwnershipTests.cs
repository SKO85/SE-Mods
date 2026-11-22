using NSubstitute;
using System.Reflection;
using VRage.Game.ModAPI;
using Xunit;
using IMyShipWelder = Sandbox.ModAPI.IMyShipWelder;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-GRIND-001 — BUG-261122.7 unowned blocks the BaR owner built are not janitor targets.
    public class JanitorOwnershipTests : ModTestBase
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private const long Owner = 7;

        private static bool BuiltByOwnerOrFaction(long builtBy)
        {
            var system = new NanobotSystem();
            var welder = Substitute.For<IMyShipWelder>();
            welder.OwnerId.Returns(Owner);
            typeof(NanobotSystem).GetField("_Welder", Any).SetValue(system, welder);
            var block = Substitute.For<IMySlimBlock>();
            block.BuiltBy.Returns(builtBy);
            return (bool)typeof(NanobotSystem).GetMethod("IsBuiltByOwnerOrFaction", Any).Invoke(system, new object[] { block });
        }

        [Fact]
        public void BlockBuiltByTheOwner_IsTheOwners()
        {
            Assert.True(BuiltByOwnerOrFaction(Owner));
        }

        [Fact]
        public void BlockWithoutBuilder_IsDebris()
        {
            Assert.False(BuiltByOwnerOrFaction(0));
        }
    }
}

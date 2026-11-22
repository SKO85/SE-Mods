using NSubstitute;
using System.Reflection;
using Xunit;
using IMyShipWelder = Sandbox.ModAPI.IMyShipWelder;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-BUDGET-002 / SCN-BUDGET-004 — auto budgets and auto stagger.
    public class ModResolverTests : ModTestBase
    {
        private static long _nextId = 1;

        private static void AddNullSystems(int count)
        {
            for (var i = 0; i < count; i++) Mod.NanobotSystems.TryAdd(_nextId++, null);
        }

        /// <summary>A NanobotSystem whose welder reports Enabled = <paramref name="enabled"/> (no game entity).</summary>
        private static NanobotSystem SystemWithWelder(bool enabled)
        {
            var system = new NanobotSystem();
            var welder = Substitute.For<IMyShipWelder>();
            welder.Enabled.Returns(enabled);
            typeof(NanobotSystem).GetField("_Welder", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(system, welder);
            return system;
        }

        private void AddSystems(int enabled, int disabled)
        {
            for (var i = 0; i < enabled; i++) Mod.NanobotSystems.TryAdd(_nextId++, SystemWithWelder(true));
            for (var i = 0; i < disabled; i++) Mod.NanobotSystems.TryAdd(_nextId++, SystemWithWelder(false));
        }

        [Theory]
        [InlineData(0, 5)]
        [InlineData(3, 5)]
        [InlineData(7, 7)]
        [InlineData(25, 10)]
        public void GetEffectiveMaxGrindsPerTick_AutoClampsBetween5And10(int systems, int expected)
        {
            AddNullSystems(systems);
            Assert.Equal(expected, Mod.GetEffectiveMaxGrindsPerTick());
            Assert.Equal(expected, Mod.GetEffectiveMaxWeldsPerTick());
        }

        [Fact]
        public void GetEffectiveMaxGrindsPerTick_ExplicitWins()
        {
            AddNullSystems(25);
            Mod.Settings.MaxGrindsPerTick = 42;
            Mod.Settings.MaxWeldsPerTick = 3;
            Assert.Equal(42, Mod.GetEffectiveMaxGrindsPerTick());
            Assert.Equal(3, Mod.GetEffectiveMaxWeldsPerTick());
        }

        [Fact]
        public void GetEffectiveMaxMsPerTick_DefaultsAndExplicit()
        {
            Mod.Settings.MaxGrindMsPerTick = 0;
            Assert.Equal(Mod.MaxGrindMsPerTickDefault, Mod.GetEffectiveMaxGrindMsPerTick());
            Mod.Settings.MaxWeldMsPerTick = 25;
            Assert.Equal(25.0, Mod.GetEffectiveMaxWeldMsPerTick());
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(5, 1)]
        [InlineData(6, 2)]
        [InlineData(10, 2)]
        [InlineData(11, 3)]
        public void AutoStaggerGroups_Thresholds(int enabledSystems, int expectedGroups)
        {
            AddSystems(enabledSystems, 4); // disabled BaRs never count (BUG-031)
            Assert.Equal(expectedGroups, Mod.GetEffectiveStaggerGroupCount());
        }

        [Fact]
        public void AutoStaggerGroups_ExplicitOverrideWins()
        {
            AddSystems(20, 0);
            Mod.Settings.StaggerGroupCount = 4;
            Assert.Equal(4, Mod.GetEffectiveStaggerGroupCount());
        }
    }
}

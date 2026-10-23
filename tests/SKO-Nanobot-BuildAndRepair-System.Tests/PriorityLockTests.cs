using SKONanobotBuildAndRepairSystem.Models;
using System.Reflection;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-PRIO-001 / SCN-API-001 — BUG-261023.6 admin priority locks vs PB scripts and client messages.
    public class PriorityLockTests : ModTestBase
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static object Prop(object target, string name)
        {
            return target.GetType().GetProperty(name, Any).GetValue(target);
        }

        private static object Call(object target, string method, params object[] args)
        {
            return target.GetType().GetMethod(method, Any).Invoke(target, args);
        }

        private static SyncBlockSettings Settings(NanobotSystem system) { return (SyncBlockSettings)Prop(system, "Settings"); }

        private static string Entries(NanobotSystem system, string handler) { return (string)Call(Prop(system, handler), "GetEntries"); }

        private static void ResetChanged(SyncBlockSettings settings)
        {
            typeof(SyncBlockSettings).GetProperty("Changed", Any).SetValue(settings, 0u);
        }

        private static NanobotSystem NewSystem()
        {
            var system = new NanobotSystem();
            var settings = new SyncBlockSettings(system);
            typeof(NanobotSystem).GetField("_Settings", Any).SetValue(system, settings);
            settings.WeldPriority = Entries(system, "BlockWeldPriority");
            settings.GrindPriority = Entries(system, "BlockGrindPriority");
            settings.ComponentCollectPriority = Entries(system, "ComponentCollectPriority");
            ResetChanged(settings);
            return system;
        }

        [Fact]
        public void ScriptSetters_WriteTheListThroughToSettings()
        {
            var system = NewSystem();
            var before = Settings(system).WeldPriority;

            Call(system, "ScriptSetWeldEnabled", 1, false);
            Assert.NotEqual(before, Settings(system).WeldPriority);
            Assert.Equal(Entries(system, "BlockWeldPriority"), Settings(system).WeldPriority);
            Assert.NotEqual(0u, Settings(system).Changed); // saved + transmitted + SettingsChanged

            Call(system, "ScriptSetGrindPriority", 1, 3);
            Assert.Equal(Entries(system, "BlockGrindPriority"), Settings(system).GrindPriority);
            Call(system, "ScriptSetCollectEnabled", 1, false);
            Assert.Equal(Entries(system, "ComponentCollectPriority"), Settings(system).ComponentCollectPriority);
        }

        [Fact]
        public void ScriptSetters_AreIgnored_WhileLocked()
        {
            Mod.Settings.Welder.PriorityFixed = true;
            Mod.Settings.Welder.CollectPriorityFixed = true;
            var system = NewSystem();
            var weld = Entries(system, "BlockWeldPriority");
            var grind = Entries(system, "BlockGrindPriority");
            var collect = Entries(system, "ComponentCollectPriority");

            Call(system, "ScriptSetWeldPriority", 1, 3);
            Call(system, "ScriptSetWeldEnabled", 1, false);
            Call(system, "ScriptSetGrindPriority", 1, 3);
            Call(system, "ScriptSetGrindEnabled", 1, false);
            Call(system, "ScriptSetCollectPriority", 1, 2);
            Call(system, "ScriptSetCollectEnabled", 1, false);

            Assert.Equal(weld, Entries(system, "BlockWeldPriority"));
            Assert.Equal(grind, Entries(system, "BlockGrindPriority"));
            Assert.Equal(collect, Entries(system, "ComponentCollectPriority"));
            Assert.Equal(0u, Settings(system).Changed);
        }

        [Fact]
        public void CollectLock_IsIndependentOfThePriorityLock()
        {
            Mod.Settings.Welder.CollectPriorityFixed = true;
            var system = NewSystem();
            var collect = Entries(system, "ComponentCollectPriority");

            Call(system, "ScriptSetCollectEnabled", 1, false);
            Call(system, "ScriptSetWeldEnabled", 1, false);

            Assert.Equal(collect, Entries(system, "ComponentCollectPriority"));
            Assert.Equal(Entries(system, "BlockWeldPriority"), Settings(system).WeldPriority);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void ClientMessage_CannotChangeLockedLists(bool priorityFixed, bool collectFixed)
        {
            Mod.Settings.Welder.PriorityFixed = priorityFixed;
            Mod.Settings.Welder.CollectPriorityFixed = collectFixed;
            var current = new SyncBlockSettings { WeldPriority = "w-current", GrindPriority = "g-current", ComponentCollectPriority = "c-current" };
            var received = new SyncBlockSettings { WeldPriority = "w-client", GrindPriority = "g-client", ComponentCollectPriority = "c-client" };

            typeof(SyncBlockSettings).GetMethod("KeepLockedPriorities", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { received, current });

            Assert.Equal(priorityFixed ? "w-current" : "w-client", received.WeldPriority);
            Assert.Equal(priorityFixed ? "g-current" : "g-client", received.GrindPriority);
            Assert.Equal(collectFixed ? "c-current" : "c-client", received.ComponentCollectPriority);
        }
    }
}

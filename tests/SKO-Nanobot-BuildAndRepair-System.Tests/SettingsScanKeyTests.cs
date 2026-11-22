using NSubstitute;
using SKONanobotBuildAndRepairSystem.Models;
using System.Reflection;
using VRage.Game.ModAPI;
using VRageMath;
using Xunit;
using IMyShipWelder = Sandbox.ModAPI.IMyShipWelder;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-TERM-001 / SCN-API-001 — BUG-261023.9 a settings change only forces a rescan when the scan can change.
    public class SettingsScanKeyTests : ModTestBase
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static NanobotSystem NewSystem()
        {
            var system = new NanobotSystem();
            typeof(NanobotSystem).GetField("_Settings", Any).SetValue(system, new SyncBlockSettings(system));
            var grid = Substitute.For<IMyCubeGrid>();
            grid.EntityId.Returns(42L);
            var welder = Substitute.For<IMyShipWelder>();
            welder.CubeGrid.Returns(grid);
            welder.OwnerId.Returns(7L);
            typeof(NanobotSystem).GetField("_Welder", Any).SetValue(system, welder);
            return system;
        }

        private static SyncBlockSettings Settings(NanobotSystem system)
        {
            return (SyncBlockSettings)typeof(NanobotSystem).GetProperty("Settings", Any).GetValue(system);
        }

        private static int Key(NanobotSystem system)
        {
            var clusterHash = (int)InvokeStatic("SKONanobotBuildAndRepairSystem.Cluster.ScanClusterCoordinator", "ComputeClusterKeyHash", system);
            return (int)typeof(NanobotSystem).GetMethod("ComputeSettingsScanKey", Any).Invoke(system, new object[] { clusterHash });
        }

        [Fact]
        public void PickedTargetChange_DoesNotChangeTheKey()
        {
            var system = NewSystem();
            var before = Key(system);
            Settings(system).CurrentPickedGrindingBlock = Substitute.For<VRage.Game.ModAPI.Ingame.IMySlimBlock>();
            Settings(system).CurrentPickedWeldingBlock = Substitute.For<VRage.Game.ModAPI.Ingame.IMySlimBlock>();
            Settings(system).SoundVolume = Settings(system).SoundVolume * 0.5f;
            Assert.Equal(before, Key(system));
        }

        [Fact]
        public void ScanRelevantChanges_ChangeTheKey()
        {
            var system = NewSystem();
            var s = Settings(system);

            var k0 = Key(system);
            s.AreaSize = s.AreaSize + new Vector3(2, 0, 0);
            var k1 = Key(system);
            Assert.NotEqual(k0, k1);

            s.AreaOffset = s.AreaOffset + new Vector3(0, 1, 0);
            var k2 = Key(system);
            Assert.NotEqual(k1, k2);

            s.WorkMode = s.WorkMode == WorkModes.WeldOnly ? WorkModes.GrindOnly : WorkModes.WeldOnly;
            var k3 = Key(system);
            Assert.NotEqual(k2, k3);

            s.Flags = s.Flags ^ SyncBlockSettings.Settings.ComponentCollectIfIdle;
            Assert.NotEqual(k3, Key(system));
        }
    }
}

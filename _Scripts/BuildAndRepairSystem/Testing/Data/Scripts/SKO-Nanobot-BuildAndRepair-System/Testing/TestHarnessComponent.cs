using VRage.Game.Components;

namespace SKONanobotBuildAndRepairSystem.Testing
{
    /// <summary>
    /// FEAT-260910.2: wires the scenario harness into the mod's seam (Mod.TestTick / Mod.TestCommand).
    /// Only present in the -Testing overlay; the release variant never sees this file.
    /// </summary>
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class TestHarnessComponent : MySessionComponentBase
    {
        public override void LoadData()
        {
            Mod.TestTick = TestRunner.Tick;
            Mod.TestCommand = TestCommand.Execute;
        }

        protected override void UnloadData()
        {
            try { TestRunner.Abort(); } catch { }
            Mod.TestTick = null;
            Mod.TestCommand = null;
        }
    }
}

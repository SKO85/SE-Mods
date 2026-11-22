using Sandbox.ModAPI;
using VRage.Game;
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
        /// <summary>This mod (the -Testing variant), for reading its data files.</summary>
        internal static MyObjectBuilder_Checkpoint.ModItem ModItem;

        /// <summary>Text of a file shipped in the -Testing variant (path relative to the mod root), or null.</summary>
        internal static string ReadModFile(string path)
        {
            try
            {
                if (!MyAPIGateway.Utilities.FileExistsInModLocation(path, ModItem)) return null;
                using (var reader = MyAPIGateway.Utilities.ReadFileInModLocation(path, ModItem)) return reader.ReadToEnd();
            }
            catch { return null; }
        }

        public override void LoadData()
        {
            ModItem = ModContext != null ? ModContext.ModItem : default(MyObjectBuilder_Checkpoint.ModItem);
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

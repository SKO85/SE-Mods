using Sandbox.Definitions;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>Small helpers shared by scenarios.</summary>
    internal static class ScenarioUtil
    {
        /// <summary>Components of every vanilla block the harness spawns (armor, conveyor, battery, stator).</summary>
        public static readonly string[] Components =
        {
            "SteelPlate", "InteriorPlate", "Construction", "SmallTube", "LargeTube", "Motor", "MetalGrid", "Computer", "PowerCell"
        };

        /// <summary>A paint that no other harness block uses (ignore / grind colour tests).</summary>
        public static readonly Vector3 Paint = new Vector3(0.5f, 0.5f, 0.2f);

        public static void StockEverything(TestContext ctx, TestRig rig, int perContainer)
        {
            for (int i = 0; i < Components.Length; i++) ctx.StockAllCargo(rig, Components[i], perContainer);
        }

        /// <summary>How many of one component a fully built block of this definition holds.</summary>
        public static int ComponentsPerBlock(string blockTypeId, string blockSubtype, string componentSubtype)
        {
            MyDefinitionId id;
            if (!MyDefinitionId.TryParse(blockTypeId, blockSubtype, out id)) return 0;
            var def = MyDefinitionManager.Static.GetCubeBlockDefinition(id);
            if (def == null || def.Components == null) return 0;
            var n = 0;
            for (int i = 0; i < def.Components.Length; i++)
                if (def.Components[i].Definition.Id.SubtypeName == componentSubtype) n += def.Components[i].Count;
            return n;
        }

        /// <summary>Grid is gone, or none of the listed blocks is left.</summary>
        public static bool AllGone(TestContext ctx, TestRig rig, List<IMySlimBlock> blocks)
        {
            var grid = rig.Grid;
            if (grid == null || grid.Closed || grid.MarkedForClose) return true;
            return ctx.CountExisting(blocks) == 0;
        }

        /// <summary>Extra blocks in a row along grid X: (x0 + i, y, z), i = 0..count-1.</summary>
        public static void AddRow(RigSpec spec, RigBlockKind kind, int count, int x0, int y, int z, float buildPercent, string tag)
        {
            for (int i = 0; i < count; i++)
                spec.Extra.Add(new RigBlock { Kind = kind, Position = new Vector3I(x0 + i, y, z), BuildPercent = buildPercent, Tag = tag });
        }
    }
}

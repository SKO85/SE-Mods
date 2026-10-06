using Sandbox.Common.ObjectBuilders;
using System.Collections.Generic;
using VRage;
using VRage.Game;
using VRage.ObjectBuilders;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing
{
    /// <summary>
    /// Builds a static large-grid test rig as an object builder.
    /// Layout (grid units, y up): base layer y=0 holds the BaRs (1x1x2, at x=2i, z=0..1) wrapped by
    /// small containers on their side and back faces so the conveyor network is reachable whichever
    /// face carries the port, plus a battery. Armor targets sit directly on top at y=1 in rows of 10.
    /// Target-only grids (Bars == 0) are just the armor rows at y=0.
    /// </summary>
    internal static class RigBuilder
    {
        private const string BarSubtype = "SELtdLargeNanobotBuildAndRepairSystem";
        private const string CargoSubtype = "LargeBlockSmallContainer";
        private const string BatterySubtype = "LargeBlockBatteryBlock";
        private const string ArmorSubtype = "LargeBlockArmorBlock";
        private const int RowWidth = 10;

        public static MyObjectBuilder_CubeGrid Build(RigSpec spec, Vector3D position, long ownerId, TestRig rig)
        {
            var grid = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_CubeGrid>();
            grid.GridSizeEnum = MyCubeSize.Large;
            grid.IsStatic = true;
            grid.CreatePhysics = true;
            grid.DestructibleBlocks = true;
            grid.PersistentFlags = MyPersistentEntityFlags2.InScene | MyPersistentEntityFlags2.CastShadows;
            grid.PositionAndOrientation = new MyPositionAndOrientation(position, Vector3.Forward, Vector3.Up);
            grid.DisplayName = "NanobotTest-" + spec.Name;
            grid.CubeBlocks = new List<MyObjectBuilder_CubeBlock>();

            var used = new HashSet<Vector3I>();
            var armorY = 0;

            if (spec.Bars > 0)
            {
                armorY = 1;
                var width = spec.Bars * 2;
                for (int i = 0; i < spec.Bars; i++)
                {
                    var x = i * 2;
                    var bar = Create<MyObjectBuilder_ShipWelder>(BarSubtype, new Vector3I(x, 0, 0), ownerId);
                    bar.Enabled = true;
                    Add(grid, used, bar, 1, 1, 2);
                    rig.BarPositions.Add(new Vector3I(x, 0, 0));

                    // side face fillers between/next to BaRs (x odd)
                    AddCargo(grid, used, rig, new Vector3I(x + 1, 0, 0), ownerId);
                    AddCargo(grid, used, rig, new Vector3I(x + 1, 0, 1), ownerId);
                }
                // back row behind every BaR
                for (int x = 0; x < width; x++) AddCargo(grid, used, rig, new Vector3I(x, 0, 2), ownerId);
                // left side of the first BaR
                AddCargo(grid, used, rig, new Vector3I(-1, 0, 0), ownerId);
                AddCargo(grid, used, rig, new Vector3I(-1, 0, 1), ownerId);

                var battery = Create<MyObjectBuilder_BatteryBlock>(BatterySubtype, new Vector3I(-1, 0, 2), ownerId);
                battery.Enabled = true;
                battery.CurrentStoredPower = 3f;
                battery.MaxStoredPower = 3f;
                Add(grid, used, battery, 1, 1, 1);
            }

            var n = 0;
            for (int i = 0; i < spec.DamagedArmor; i++, n++)
            {
                var pos = new Vector3I(n % RowWidth, armorY, n / RowWidth);
                var armor = Create<MyObjectBuilder_CubeBlock>(ArmorSubtype, pos, ownerId);
                armor.BuildPercent = spec.DamagedBuildPercent;
                armor.IntegrityPercent = spec.DamagedBuildPercent;
                Add(grid, used, armor, 1, 1, 1);
                rig.TargetPositions.Add(pos);
            }
            for (int i = 0; i < spec.Armor; i++, n++)
            {
                var pos = new Vector3I(n % RowWidth, armorY, n / RowWidth);
                var armor = Create<MyObjectBuilder_CubeBlock>(ArmorSubtype, pos, ownerId);
                Add(grid, used, armor, 1, 1, 1);
            }

            return grid;
        }

        private static void AddCargo(MyObjectBuilder_CubeGrid grid, HashSet<Vector3I> used, TestRig rig, Vector3I pos, long ownerId)
        {
            if (used.Contains(pos)) return;
            var cargo = Create<MyObjectBuilder_CargoContainer>(CargoSubtype, pos, ownerId);
            Add(grid, used, cargo, 1, 1, 1);
            rig.CargoPositions.Add(pos);
        }

        private static T Create<T>(string subtype, Vector3I min, long ownerId) where T : MyObjectBuilder_CubeBlock, new()
        {
            var block = MyObjectBuilderSerializer.CreateNewObject<T>(subtype);
            block.Min = min;
            block.BlockOrientation = new SerializableBlockOrientation(Base6Directions.Direction.Forward, Base6Directions.Direction.Up);
            block.Owner = ownerId;
            block.BuiltBy = ownerId;
            block.ShareMode = MyOwnershipShareModeEnum.Faction;
            block.ColorMaskHSV = new SerializableVector3(0f, -1f, 0f);
            return block;
        }

        private static void Add(MyObjectBuilder_CubeGrid grid, HashSet<Vector3I> used, MyObjectBuilder_CubeBlock block, int sx, int sy, int sz)
        {
            var min = (Vector3I)block.Min;
            for (int x = 0; x < sx; x++)
                for (int y = 0; y < sy; y++)
                    for (int z = 0; z < sz; z++)
                        used.Add(min + new Vector3I(x, y, z));
            grid.CubeBlocks.Add(block);
        }
    }
}

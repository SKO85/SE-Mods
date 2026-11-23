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
    /// face carries the port, plus a battery. Armor targets sit directly on top at y=1 in rows of RigSpec.RowWidth.
    /// Target-only grids (Bars == 0) are just the armor rows at y=0.
    /// </summary>
    internal static class RigBuilder
    {
        private const string BarSubtype = "SELtdLargeNanobotBuildAndRepairSystem";
        private const string CargoSubtype = "LargeBlockSmallContainer";
        private const string BatterySubtype = "LargeBlockBatteryBlock";
        private const string ArmorSubtype = "LargeBlockArmorBlock";
        private const string ProjectorSubtype = "LargeProjector";
        private const string ConveyorSubtype = "LargeBlockConveyor";
        private const string StatorSubtype = "LargeStator";

        public static readonly Vector3I SmallProjectorPos = new Vector3I(6, 0, 0);

        /// <summary>Static small-grid ship: small BaR (3x3x1) at the origin, small battery, small projector.</summary>
        private static MyObjectBuilder_CubeGrid BuildSmall(RigSpec spec, Vector3D position, long ownerId, TestRig rig)
        {
            var grid = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_CubeGrid>();
            grid.GridSizeEnum = MyCubeSize.Small;
            grid.IsStatic = true;
            grid.CreatePhysics = true;
            grid.DestructibleBlocks = true;
            grid.PersistentFlags = MyPersistentEntityFlags2.InScene | MyPersistentEntityFlags2.CastShadows;
            grid.PositionAndOrientation = new MyPositionAndOrientation(position, spec.Forward, Vector3.Up);
            grid.DisplayName = "NanobotTest-" + spec.Name;
            grid.CubeBlocks = new List<MyObjectBuilder_CubeBlock>();
            var used = new HashSet<Vector3I>();
            if (spec.Bars > 0)
            {
                var bar = Create<MyObjectBuilder_ShipWelder>("SELtdSmallNanobotBuildAndRepairSystem", Vector3I.Zero, ownerId);
                bar.Enabled = false; // switched on by the runner when the measured run starts
                Add(grid, used, bar, 3, 3, 1);
                rig.BarPositions.Add(Vector3I.Zero);
            }
            var battery = Create<MyObjectBuilder_BatteryBlock>("SmallBlockSmallBatteryBlock", new Vector3I(4, 0, 0), ownerId);
            battery.Enabled = spec.BatteryEnabled;
            battery.CurrentStoredPower = 0.05f;
            battery.MaxStoredPower = 0.05f;
            Add(grid, used, battery, 1, 1, 1);
            rig.BatteryPosition = new Vector3I(4, 0, 0);
            // Armor bridge so the ship is one piece (BaR 0..2, battery 4, projector 6).
            Add(grid, used, Create<MyObjectBuilder_CubeBlock>("SmallBlockArmorBlock", new Vector3I(3, 0, 0), ownerId), 1, 1, 1);
            Add(grid, used, Create<MyObjectBuilder_CubeBlock>("SmallBlockArmorBlock", new Vector3I(5, 0, 0), ownerId), 1, 1, 1);
            if (spec.Projector)
            {
                var projector = Create<MyObjectBuilder_Projector>("SmallProjector", SmallProjectorPos, ownerId);
                projector.Enabled = true;
                Add(grid, used, projector, 1, 1, 1);
                rig.ProjectorPosition = SmallProjectorPos;
            }
            return grid;
        }

        public static MyObjectBuilder_CubeGrid Build(RigSpec spec, Vector3D position, long ownerId, TestRig rig)
        {
            if (spec.SmallGrid) return BuildSmall(spec, position, ownerId, rig);
            var grid = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_CubeGrid>();
            grid.GridSizeEnum = MyCubeSize.Large;
            grid.IsStatic = !spec.Dynamic;
            if (spec.Dynamic)
            {
                grid.LinearVelocity = spec.LinearVelocity;
                grid.AngularVelocity = spec.AngularVelocity;
            }
            grid.CreatePhysics = true;
            grid.DestructibleBlocks = !spec.Indestructible;
            grid.PersistentFlags = MyPersistentEntityFlags2.InScene | MyPersistentEntityFlags2.CastShadows;
            grid.PositionAndOrientation = new MyPositionAndOrientation(position, spec.Forward, Vector3.Up);
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
                    bar.Enabled = false; // switched on by the runner when the measured run starts
                    Add(grid, used, bar, 1, 1, 2);
                    var barGroup = spec.BarGroups != null && i < spec.BarGroups.Length ? spec.BarGroups[i] : spec.BarGroup;
                    if (barGroup != null) AddToGroup(grid, barGroup, new Vector3I(x, 0, 0));
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
                battery.Enabled = spec.BatteryEnabled;
                battery.CurrentStoredPower = 3f;
                battery.MaxStoredPower = 3f;
                Add(grid, used, battery, 1, 1, 1);
                rig.BatteryPosition = new Vector3I(-1, 0, 2);

                if (spec.Projector)
                {
                    // Next to the last side filler (x = width - 1), on the base layer.
                    var projectorPos = new Vector3I(width, 0, 1);
                    var projector = Create<MyObjectBuilder_Projector>(ProjectorSubtype, projectorPos, ownerId);
                    projector.Enabled = true;
                    Add(grid, used, projector, 1, 1, 1);
                    rig.ProjectorPosition = projectorPos;
                }
            }

            var rowWidth = spec.RowWidth > 0 ? spec.RowWidth : 10;
            var n = 0;
            for (int i = 0; i < spec.DamagedArmor; i++, n++)
            {
                var pos = new Vector3I(n % rowWidth, armorY, n / rowWidth);
                var armor = Create<MyObjectBuilder_CubeBlock>(ArmorSubtype, pos, ownerId);
                armor.BuildPercent = spec.DamagedBuildPercent;
                armor.IntegrityPercent = spec.DamagedBuildPercent;
                Add(grid, used, armor, 1, 1, 1);
                rig.TargetPositions.Add(pos);
            }
            for (int i = 0; i < spec.Armor; i++, n++)
            {
                var pos = new Vector3I(n % rowWidth, armorY, n / rowWidth);
                var armor = Create<MyObjectBuilder_CubeBlock>(ArmorSubtype, pos, ownerId);
                Add(grid, used, armor, 1, 1, 1);
            }

            for (int i = 0; i < spec.Extra.Count; i++)
            {
                var extra = spec.Extra[i];
                var size = ExtraSize(extra.Kind);
                for (int x = 0; x < size.X; x++)
                    for (int y = 0; y < size.Y; y++)
                        for (int z = 0; z < size.Z; z++)
                            if (used.Contains(extra.Position + new Vector3I(x, y, z)))
                                throw new System.Exception("RigBuilder(" + spec.Name + "): extra block at " + extra.Position + " overlaps another block");
                var owner = extra.OwnerId == long.MinValue ? ownerId : extra.OwnerId;
                var block = CreateExtra(extra.Kind, extra.Position, owner);
                block.BuiltBy = extra.BuiltBy == long.MinValue ? owner : extra.BuiltBy;
                if (extra.BuildPercent < 1f)
                {
                    block.BuildPercent = extra.BuildPercent;
                    block.IntegrityPercent = extra.BuildPercent;
                }
                if (extra.Color.HasValue) block.ColorMaskHSV = extra.Color.Value;
                var terminal = block as MyObjectBuilder_TerminalBlock;
                if (terminal != null && extra.Name != null) terminal.CustomName = extra.Name;
                var functional = block as MyObjectBuilder_FunctionalBlock;
                if (functional != null && !extra.Enabled) functional.Enabled = false;
                var pb = block as MyObjectBuilder_MyProgrammableBlock;
                if (pb != null && extra.Program != null) pb.Program = extra.Program;
                if (extra.Groups != null) foreach (var g in extra.Groups) AddToGroup(grid, g, extra.Position);
                Add(grid, used, block, size.X, size.Y, size.Z);
                rig.ExtraSpecs.Add(extra);
            }

            return grid;
        }

        /// <summary>Grid cells of an extra (default orientation): mechanical bases 1x2x1, assembler 1x1x2.</summary>
        private static Vector3I ExtraSize(RigBlockKind kind)
        {
            switch (kind)
            {
                case RigBlockKind.AdvancedStator:
                case RigBlockKind.Piston:
                case RigBlockKind.Suspension:
                    return new Vector3I(1, 2, 1);
                case RigBlockKind.Assembler:
                    return new Vector3I(1, 1, 2);
                default:
                    return Vector3I.One;
            }
        }

        private static void AddToGroup(MyObjectBuilder_CubeGrid grid, string name, Vector3I blockMin)
        {
            if (grid.BlockGroups == null) grid.BlockGroups = new List<MyObjectBuilder_BlockGroup>();
            MyObjectBuilder_BlockGroup group = null;
            foreach (var g in grid.BlockGroups) if (g.Name == name) { group = g; break; }
            if (group == null)
            {
                group = new MyObjectBuilder_BlockGroup { Name = name, Blocks = new List<Vector3I>() };
                grid.BlockGroups.Add(group);
            }
            group.Blocks.Add(blockMin);
        }

        private static MyObjectBuilder_CubeBlock CreateExtra(RigBlockKind kind, Vector3I pos, long ownerId)
        {
            switch (kind)
            {
                case RigBlockKind.Conveyor: return Create<MyObjectBuilder_Conveyor>(ConveyorSubtype, pos, ownerId);
                case RigBlockKind.Battery:
                    var battery = Create<MyObjectBuilder_BatteryBlock>(BatterySubtype, pos, ownerId);
                    battery.Enabled = true;
                    battery.CurrentStoredPower = 3f;
                    battery.MaxStoredPower = 3f;
                    return battery;
                case RigBlockKind.Stator:
                    var stator = Create<MyObjectBuilder_MotorStator>(StatorSubtype, pos, ownerId);
                    stator.Enabled = true;
                    return stator;
                case RigBlockKind.AdvancedStator:
                    var advanced = Create<MyObjectBuilder_MotorAdvancedStator>("LargeAdvancedStator", pos, ownerId);
                    advanced.Enabled = true;
                    return advanced;
                case RigBlockKind.Hinge:
                    var hinge = Create<MyObjectBuilder_MotorAdvancedStator>("LargeHinge", pos, ownerId);
                    hinge.Enabled = true;
                    return hinge;
                case RigBlockKind.Piston:
                    var piston = Create<MyObjectBuilder_ExtendedPistonBase>("LargePistonBase", pos, ownerId);
                    piston.Enabled = true;
                    return piston;
                case RigBlockKind.Suspension:
                    var suspension = Create<MyObjectBuilder_MotorSuspension>("Suspension1x1", pos, ownerId);
                    suspension.Enabled = true;
                    return suspension;
                case RigBlockKind.Connector:
                    var connector = Create<MyObjectBuilder_ShipConnector>("Connector", pos, ownerId);
                    connector.Enabled = true;
                    return connector;
                case RigBlockKind.ShieldController:
                    var controller = Create<MyObjectBuilder_UpgradeModule>("DSControlLarge", pos, ownerId);
                    controller.Enabled = true;
                    return controller;
                case RigBlockKind.Assembler:
                    var assembler = Create<MyObjectBuilder_Assembler>("LargeAssembler", pos, ownerId);
                    assembler.Enabled = true;
                    return assembler;
                case RigBlockKind.ProgrammableBlock:
                    var program = Create<MyObjectBuilder_MyProgrammableBlock>("LargeProgrammableBlock", pos, ownerId);
                    program.Enabled = true;
                    return program;
                case RigBlockKind.Lcd:
                    var lcd = Create<MyObjectBuilder_TextPanel>("LargeLCDPanel", pos, ownerId);
                    lcd.Enabled = true;
                    return lcd;
                case RigBlockKind.Cargo:
                    return Create<MyObjectBuilder_CargoContainer>(CargoSubtype, pos, ownerId);
                case RigBlockKind.Sorter:
                    // Default orientation: conveyor ports front/back (along Z).
                    var sorter = Create<MyObjectBuilder_ConveyorSorter>("LargeBlockConveyorSorter", pos, ownerId);
                    sorter.Enabled = true;
                    return sorter;
                case RigBlockKind.CockpitSeat:
                    return Create<MyObjectBuilder_Cockpit>("LargeBlockCockpitSeat", pos, ownerId);
                case RigBlockKind.ShieldEmitter:
                    var emitter = Create<MyObjectBuilder_UpgradeModule>("EmitterL", pos, ownerId);
                    emitter.Enabled = true;
                    return emitter;
                default: return Create<MyObjectBuilder_CubeBlock>(ArmorSubtype, pos, ownerId);
            }
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

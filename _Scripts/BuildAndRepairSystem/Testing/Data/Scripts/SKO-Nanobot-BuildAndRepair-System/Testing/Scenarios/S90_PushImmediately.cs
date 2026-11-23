using SKONanobotBuildAndRepairSystem.Models;
using VRage;
using VRage.Game;
using IMyInventory = VRage.Game.ModAPI.IMyInventory;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    internal enum PushFlag { None, IngotOre, Components, Items }

    /// <summary>
    /// SCN-INV-002: the "push immediately" options. The idle BaR holds 200 kg iron ore, 50 SteelPlate and 1 hand welder;
    /// the rig cargo is empty. With exactly one option on (or none), after 30 s only that item class has left the BaR
    /// (into the rig cargo), the rest stays, and nothing is lost.
    /// </summary>
    internal sealed class S90_PushImmediately : TestScenarioBase
    {
        // An idle BaR refreshes its push targets with its (slower) idle scan and backs off 15 s after a push that found
        // no target, so a fresh idle BaR can take ~45 s to push: allow 75 s, finish as soon as the class has left.
        private const double Window = 75.0;
        private const double NoneWindow = 45.0;
        private const int Ore = 200, Plates = 50, Tools = 1;
        // Created on use: MyItemType's type lookup needs the game's object builder registry (not loaded in unit tests,
        // which construct every registered scenario).
        private static MyItemType IronOre { get { return MyItemType.MakeOre("Iron"); } }
        private static MyItemType SteelPlate { get { return MyItemType.MakeComponent("SteelPlate"); } }
        private static MyItemType Welder { get { return new MyItemType("MyObjectBuilder_PhysicalGunObject", "WelderItem"); } }

        private readonly string _id;
        private readonly PushFlag _flag;
        private TestRig _rig;
        private bool _stocked;
        private double _pushedAt = -1;

        public S90_PushImmediately(string id, PushFlag flag)
        {
            _id = id;
            _flag = flag;
        }

        public override string Id { get { return _id; } }
        public override string Description
        {
            get
            {
                switch (_flag)
                {
                    case PushFlag.None: return "Push immediately off: nothing leaves the BaR's inventory";
                    case PushFlag.IngotOre: return "Push ingots/ore immediately: only ore leaves the BaR";
                    case PushFlag.Components: return "Push components immediately: only components leave the BaR";
                    default: return "Push items immediately: only other items (tools) leave the BaR";
                }
            }
        }

        public override void Setup(TestContext ctx)
        {
            _stocked = false;
            _pushedAt = -1;
            _rig = ctx.SpawnRig(new RigSpec { Name = _id, Bars = 1 });
        }

        protected override void Configure(TestContext ctx)
        {
            var all = SyncBlockSettings.Settings.PushIngotOreImmediately | SyncBlockSettings.Settings.PushComponentImmediately | SyncBlockSettings.Settings.PushItemsImmediately;
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.Grids;
                s.WorkMode = WorkModes.WeldOnly;
                s.Flags &= ~all;
                if (_flag == PushFlag.IngotOre) s.Flags |= SyncBlockSettings.Settings.PushIngotOreImmediately;
                if (_flag == PushFlag.Components) s.Flags |= SyncBlockSettings.Settings.PushComponentImmediately;
                if (_flag == PushFlag.Items) s.Flags |= SyncBlockSettings.Settings.PushItemsImmediately;
            });
        }

        private IMyInventory BarInventory()
        {
            var bar = _rig.Bars.Count > 0 ? _rig.Bars[0] : null;
            return bar != null && bar.Welder != null ? bar.Welder.GetInventory(0) : null;
        }

        private double InCargo(MyItemType type)
        {
            double total = 0;
            for (int i = 0; i < _rig.Cargos.Count; i++)
            {
                var inv = _rig.Cargos[i].GetInventory(0);
                if (inv != null) total += (double)inv.GetItemAmount(type);
            }
            return total;
        }

        public override bool IsReady(TestContext ctx)
        {
            if (!base.IsReady(ctx)) return false;
            if (_stocked) return true;
            var inv = BarInventory();
            if (inv == null) return false;
            inv.AddItems((MyFixedPoint)Ore, new MyObjectBuilder_Ore { SubtypeName = "Iron" });
            inv.AddItems((MyFixedPoint)Plates, new MyObjectBuilder_Component { SubtypeName = "SteelPlate" });
            inv.AddItems((MyFixedPoint)Tools, new MyObjectBuilder_PhysicalGunObject { SubtypeName = "WelderItem" });
            _stocked = true;
            return true;
        }

        private MyItemType Expected
        {
            get { return _flag == PushFlag.IngotOre ? IronOre : _flag == PushFlag.Components ? SteelPlate : Welder; }
        }

        public override void Sample(TestContext ctx)
        {
            if (_flag == PushFlag.None || _pushedAt >= 0 || (TestContext.Frame % 30) != 0) return;
            var bar = BarInventory();
            if (bar != null && (double)bar.GetItemAmount(Expected) < 0.01) _pushedAt = ctx.RunSeconds;
        }

        public override bool IsComplete(TestContext ctx)
        {
            if (_flag == PushFlag.None) return ctx.RunSeconds >= NoneWindow;
            return _pushedAt >= 0 || ctx.RunSeconds >= Window;
        }

        private void CheckClass(TestContext ctx, IMyInventory bar, string name, MyItemType type, int start, bool shouldLeave)
        {
            var inBar = (double)bar.GetItemAmount(type);
            var inCargo = InCargo(type);
            ctx.Metric(name + "InBar", inBar);
            ctx.Metric(name + "InCargo", inCargo);
            ctx.Check(System.Math.Abs(inBar + inCargo - start) < 0.01, name + " not conserved: " + inBar + " in the BaR + " + inCargo + " in cargo of " + start);
            if (shouldLeave) ctx.Check(inBar < 0.01, name + ": " + inBar + " still in the BaR although its push option is on");
            else ctx.Check(inCargo < 0.01, name + ": " + inCargo + " pushed out although its push option is off");
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var bar = BarInventory();
            ctx.Check(bar != null, "layout: BaR inventory missing");
            if (bar == null) return;
            ctx.Metric("flag", _flag.ToString());
            ctx.Metric("pushedAtS", _pushedAt);
            var barSystem = _rig.Bars[0];
            ctx.Metric("barFlags", ((int)barSystem.Settings.Flags).ToString("X"));
            // Engine answers for BaR -> first rig container (diagnostics for refused pushes).
            var cargo = _rig.Cargos.Count > 0 ? _rig.Cargos[0].GetInventory(0) : null;
            if (cargo != null)
            {
                ctx.Metric("oreCanTransfer", bar.CanTransferItemTo(cargo, IronOre));
                ctx.Metric("oreCanAdd", cargo.CanItemsBeAdded((MyFixedPoint)Ore, IronOre));
                ctx.Metric("platesCanTransfer", bar.CanTransferItemTo(cargo, SteelPlate));
                ctx.Metric("platesCanAdd", cargo.CanItemsBeAdded((MyFixedPoint)Plates, SteelPlate));
                ctx.Metric("toolsCanTransfer", bar.CanTransferItemTo(cargo, Welder));
                ctx.Metric("toolsCanAdd", cargo.CanItemsBeAdded((MyFixedPoint)Tools, Welder));
                ctx.Metric("cargoConnected", bar.IsConnectedTo(cargo));
            }
            CheckClass(ctx, bar, "ore", IronOre, Ore, _flag == PushFlag.IngotOre);
            CheckClass(ctx, bar, "plates", SteelPlate, Plates, _flag == PushFlag.Components);
            CheckClass(ctx, bar, "tools", Welder, Tools, _flag == PushFlag.Items);
        }
    }
}

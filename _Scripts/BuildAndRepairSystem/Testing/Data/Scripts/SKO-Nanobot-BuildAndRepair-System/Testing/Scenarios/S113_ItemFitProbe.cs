using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Utils;
using System.Collections.Generic;
using VRage;
using VRage.Game;
using IMyInventory = VRage.Game.ModAPI.IMyInventory;
using MyInventoryItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// SCN-INV-002 / BUG-261123.5 probe: does MaxItemsAddable (CanItemsBeAdded search) refuse ore / ingots? Per item
    /// class (ore, ingot, component) it compares the item type read from a real inventory item with one built by
    /// MyItemType.Make*, on an empty cargo and the BaR's own inventory, and moves 100 units from the BaR's transport into
    /// the BaR exactly like the mod does (MaxItemsAddable + TransferItemFrom). Runs in one tick, no welding.
    /// </summary>
    internal sealed class S113_ItemFitProbe : TestScenarioBase
    {
        private const double Amount = 100;
        private TestRig _rig;
        private bool _done;
        private readonly List<string> _failures = new List<string>();
        private readonly List<KeyValuePair<string, object>> _metrics = new List<KeyValuePair<string, object>>();
        private readonly List<MyInventoryItem> _items = new List<MyInventoryItem>();

        public override string Id { get { return "S113"; } }
        public override string Description { get { return "Probe: can ore / ingots / components be fitted into the BaR and a cargo (MaxItemsAddable)"; } }
        public override int TimeoutSeconds { get { return 30; } }

        public override void Setup(TestContext ctx)
        {
            _done = false;
            _failures.Clear();
            _metrics.Clear();
            _rig = ctx.SpawnRig(new RigSpec { Name = "S113", Bars = 1 });
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.ConfigureBars(_rig, s => { s.WorkMode = WorkModes.WeldOnly; });
        }

        public override void Sample(TestContext ctx)
        {
            if (_done) return;
            var bar = _rig.Bars.Count > 0 ? _rig.Bars[0] : null;
            var welderInv = bar != null && bar.Welder != null ? bar.Welder.GetInventory(0) : null;
            var cargoInv = _rig.Cargos.Count > 0 ? _rig.Cargos[0].GetInventory(0) : null;
            if (welderInv == null || cargoInv == null || bar._TransportInventory == null) return;
            ctx.ClearInventory(_rig.Cargos[0]);

            Probe("ore", new MyObjectBuilder_Ore { SubtypeName = "Iron" }, MyItemType.MakeOre("Iron"), welderInv, cargoInv, bar._TransportInventory);
            Probe("ingot", new MyObjectBuilder_Ingot { SubtypeName = "Iron" }, MyItemType.MakeIngot("Iron"), welderInv, cargoInv, bar._TransportInventory);
            Probe("component", new MyObjectBuilder_Component { SubtypeName = "SteelPlate" }, MyItemType.MakeComponent("SteelPlate"), welderInv, cargoInv, bar._TransportInventory);
            NearlyFull("oreNearlyFull", new MyObjectBuilder_Ore { SubtypeName = "Iron" }, MyItemType.MakeOre("Iron"), welderInv, bar._TransportInventory);
            NearlyFull("componentNearlyFull", new MyObjectBuilder_Component { SubtypeName = "SteelPlate" }, MyItemType.MakeComponent("SteelPlate"), welderInv, bar._TransportInventory);
            _done = true;
        }

        private void Metric(string name, object value) { _metrics.Add(new KeyValuePair<string, object>(name, value)); }

        private void Probe(string kind, MyObjectBuilder_PhysicalObject ob, MyItemType made, IMyInventory welderInv, IMyInventory cargoInv, IMyInventory transport)
        {
            // A real item type: put the item into the cargo and read it back.
            cargoInv.AddItems((MyFixedPoint)Amount, ob);
            _items.Clear();
            cargoInv.GetItems(_items);
            var found = false;
            var real = made;
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Type.SubtypeId == made.SubtypeId && _items[i].Type.TypeId == made.TypeId) { real = _items[i].Type; found = true; break; }
            }
            Metric(kind + ".realTypeFound", found);
            Metric(kind + ".typeId", real.TypeId.Replace(';', ' '));
            Metric(kind + ".madeTypeId", made.TypeId.Replace(';', ' '));
            cargoInv.RemoveItemsOfType((MyFixedPoint)Amount, ob);

            Metric(kind + ".cargo.canAddReal", cargoInv.CanItemsBeAdded((MyFixedPoint)Amount, real));
            Metric(kind + ".cargo.canAddMade", cargoInv.CanItemsBeAdded((MyFixedPoint)Amount, made));
            Metric(kind + ".bar.canAddReal", welderInv.CanItemsBeAdded((MyFixedPoint)Amount, real));
            Metric(kind + ".bar.canAddMade", welderInv.CanItemsBeAdded((MyFixedPoint)Amount, made));
            var fitReal = (double)welderInv.MaxItemsAddable((MyFixedPoint)Amount, real);
            Metric(kind + ".bar.maxAddableReal", fitReal);
            Metric(kind + ".bar.maxAddableMade", (double)welderInv.MaxItemsAddable((MyFixedPoint)Amount, made));
            Metric(kind + ".bar.canAddOne", welderInv.CanItemsBeAdded((MyFixedPoint)1, real));

            // The mod's transport -> BaR move (NanobotSystem.Inventory.cs): MaxItemsAddable + TransferItemFrom.
            var barBefore = (double)welderInv.GetItemAmount(real);
            transport.AddItems((MyFixedPoint)Amount, ob);
            _items.Clear();
            transport.GetItems(_items);
            double moved = 0;
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var item = _items[i];
                if (item.Type.SubtypeId != made.SubtypeId || item.Type.TypeId != made.TypeId) continue;
                var fit = welderInv.MaxItemsAddable(item.Amount, item.Type);
                Metric(kind + ".transport.fit", (double)fit);
                if (fit > 0 && welderInv.TransferItemFrom(transport, i, null, true, fit, false)) moved = (double)welderInv.GetItemAmount(real) - barBefore;
            }
            var leftInTransport = (double)transport.GetItemAmount(real);
            Metric(kind + ".transport.moved", moved);
            Metric(kind + ".transport.left", leftInTransport);
            if (fitReal < Amount) _failures.Add(kind + ": MaxItemsAddable into the empty BaR returned " + fitReal + " of " + Amount);
            if (moved < Amount) _failures.Add(kind + ": moved " + moved + " of " + Amount + " from the transport into the empty BaR (" + leftInTransport + " left)");

            // Clean up so the next class starts empty.
            welderInv.RemoveItemsOfType((MyFixedPoint)welderInv.GetItemAmount(real), ob);
            transport.RemoveItemsOfType((MyFixedPoint)transport.GetItemAmount(real), ob);
        }

        /// <summary>
        /// The S89 situation: the BaR is nearly full (stone) and the transport holds more than fits. MaxItemsAddable must
        /// return what fits (free volume / item volume, not 0) and the move must take exactly that.
        /// </summary>
        private void NearlyFull(string kind, MyObjectBuilder_PhysicalObject ob, MyItemType type, IMyInventory welderInv, IMyInventory transport)
        {
            var stone = new MyObjectBuilder_Ore { SubtypeName = "Stone" };
            var stoneDef = Sandbox.Definitions.MyDefinitionManager.Static.GetPhysicalItemDefinition(new MyDefinitionId(typeof(MyObjectBuilder_Ore), "Stone"));
            var def = Sandbox.Definitions.MyDefinitionManager.Static.GetPhysicalItemDefinition(ob.GetId());
            if (stoneDef == null || def == null) { _failures.Add(kind + ": no item definition"); return; }
            // Leave ~1 % free (about 150 kg of iron ore in a large BaR).
            var leaveFree = (double)welderInv.MaxVolume * 0.01;
            var stoneAmount = System.Math.Floor(((double)welderInv.MaxVolume - (double)welderInv.CurrentVolume - leaveFree) / stoneDef.Volume);
            welderInv.AddItems((MyFixedPoint)stoneAmount, stone);
            var free = (double)welderInv.MaxVolume - (double)welderInv.CurrentVolume;
            var expected = System.Math.Floor(free / def.Volume);
            Metric(kind + ".freeVolume", free);
            Metric(kind + ".expectedFit", expected);

            var offered = expected * 3 + 10;
            transport.AddItems((MyFixedPoint)offered, ob);
            _items.Clear();
            transport.GetItems(_items);
            double fit = -1, moved = 0;
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var item = _items[i];
                if (item.Type.SubtypeId != type.SubtypeId || item.Type.TypeId != type.TypeId) continue;
                fit = (double)welderInv.MaxItemsAddable(item.Amount, item.Type);
                if (fit > 0 && welderInv.TransferItemFrom(transport, i, null, true, (MyFixedPoint)fit, false)) moved = (double)welderInv.GetItemAmount(type);
            }
            Metric(kind + ".fit", fit);
            Metric(kind + ".moved", moved);
            Metric(kind + ".fillAfter", (double)welderInv.CurrentVolume / (double)welderInv.MaxVolume);
            if (fit < expected * 0.95) _failures.Add(kind + ": MaxItemsAddable " + fit + " but about " + expected + " fit (" + free.ToString("F4") + " m3 free)");
            if (moved < fit) _failures.Add(kind + ": moved " + moved + " of the " + fit + " that fit");

            welderInv.RemoveItemsOfType((MyFixedPoint)welderInv.GetItemAmount(type), ob);
            welderInv.RemoveItemsOfType(welderInv.GetItemAmount(MyItemType.MakeOre("Stone")), stone);
            transport.RemoveItemsOfType((MyFixedPoint)transport.GetItemAmount(type), ob);
        }

        public override bool IsComplete(TestContext ctx)
        {
            return _done;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            for (int i = 0; i < _metrics.Count; i++)
            {
                var v = _metrics[i].Value;
                if (v is bool) ctx.Metric(_metrics[i].Key, (bool)v);
                else if (v is double) ctx.Metric(_metrics[i].Key, (double)v);
                else ctx.Metric(_metrics[i].Key, v != null ? v.ToString() : "null");
            }
            ctx.Check(_done, "probe did not run (no BaR / cargo / transport inventory)");
            for (int i = 0; i < _failures.Count; i++) ctx.Check(false, _failures[i]);
        }
    }
}

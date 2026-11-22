using Sandbox.ModAPI;
using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using VRage;
using VRage.Game;
using VRageMath;
using IMyConveyorSorter = Sandbox.ModAPI.IMyConveyorSorter;
using IMyInventory = VRage.Game.ModAPI.IMyInventory;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// SCN-WELD-005 (BUG-261122.17): stock the BaR cannot pull (a sorter blacklists it) must stay in the missing list,
    /// or the companion script never queues it and the build stalls. Chain behind the BaR: rig cargo (0,0,2, empty)
    /// -> sorter (0,0,3, blacklist SteelPlate) -> cargo (0,0,4, 500 SteelPlate). The rig's own cargo stays empty.
    /// </summary>
    internal sealed class S84_SorterBlockedStock : TestScenarioBase
    {
        private const double ObserveSeconds = 20.0;
        private const int Stock = 500;
        private static readonly MyItemType SteelPlate = MyItemType.MakeComponent("SteelPlate");
        private TestRig _rig;

        public override string Id { get { return "S84"; } }
        public override string Description { get { return "Sorter-blocked stock stays in the missing list (companion script keeps queuing)"; } }
        public override int TimeoutSeconds { get { return 60; } }

        public override void Setup(TestContext ctx)
        {
            var spec = new RigSpec { Name = "S84", Bars = 1, DamagedArmor = 10 };
            spec.Extra.Add(new RigBlock { Kind = RigBlockKind.Sorter, Position = new Vector3I(0, 0, 3), Name = "S84 Sorter", Tag = "sorter" });
            spec.Extra.Add(new RigBlock { Kind = RigBlockKind.Cargo, Position = new Vector3I(0, 0, 4), Name = "S84 Blocked Cargo", Tag = "far" });
            _rig = ctx.SpawnRig(spec);
            ctx.SetModSettings(s => { s.Welder.WorkSpeed = 10; });
        }

        private IMyInventory FarInventory()
        {
            var t = _rig.Tag("far");
            return t.Count > 0 && t[0] != null && t[0].FatBlock != null ? t[0].FatBlock.GetInventory(0) : null;
        }

        protected override void Configure(TestContext ctx)
        {
            var t = _rig.Tag("sorter");
            var sorter = t.Count > 0 && t[0] != null ? t[0].FatBlock as IMyConveyorSorter : null;
            if (sorter != null)
            {
                sorter.DrainAll = false;
                sorter.SetFilter(MyConveyorSorterMode.Blacklist, new List<MyInventoryItemFilter>
                {
                    new MyInventoryItemFilter(new MyDefinitionId(typeof(MyObjectBuilder_Component), "SteelPlate"))
                });
            }
            var far = FarInventory();
            if (far != null) far.AddItems((MyFixedPoint)Stock, new MyObjectBuilder_Component { SubtypeName = "SteelPlate" });
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.Grids;
                s.WorkMode = WorkModes.WeldBeforeGrind;
                s.WeldOptions = AutoWeldOptions.WeldFull;
            });
        }

        public override bool IsComplete(TestContext ctx)
        {
            return ctx.RunSeconds >= ObserveSeconds;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var bar = _rig.Bars[0];
            var barInventory = bar.Welder != null ? bar.Welder.GetInventory(0) : null;
            var far = FarInventory();
            ctx.Check(barInventory != null && far != null, "layout: BaR or blocked cargo inventory missing");
            if (barInventory == null || far == null) return;

            // Premise: the conveyor graph connects the cargo (so it is a source), the sorter refuses SteelPlate.
            var connected = far.IsConnectedTo(barInventory);
            var transferable = far.CanTransferItemTo(barInventory, SteelPlate);
            var left = (double)far.GetItemAmount(SteelPlate);
            ctx.Metric("connected", connected ? 1L : 0L);
            ctx.Metric("transferable", transferable ? 1L : 0L);
            ctx.Metric("blockedStockLeft", left);
            ctx.Check(connected, "layout: blocked cargo not conveyor-connected to the BaR (scenario proves nothing)");
            ctx.Check(!transferable, "layout: the sorter does not block SteelPlate");

            int steelPlates;
            var missing = bar.State.MissingComponents;
            lock (missing)
            {
                missing.TryGetValue(new MyDefinitionId(typeof(MyObjectBuilder_Component), "SteelPlate"), out steelPlates);
            }
            ctx.Metric("missingSteelPlate", (long)steelPlates);
            ctx.Check(left >= Stock, "the BaR pulled " + (Stock - left) + " SteelPlate through the blacklisting sorter");
            ctx.Check(steelPlates > 0, "SteelPlate not reported missing although the only stock is behind the sorter");
            ctx.CheckMethodMaxMax("RebuildMissingComponentsAggregate", 2.0);
        }
    }
}

using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Models;
using System.Collections.Generic;
using VRage;
using VRage.Game;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>SCN-COLLECT-001 / BUG-261023.5: one BaR collects 5 floating SteelPlate stacks into cargo; no item lost or duplicated.</summary>
    internal sealed class S10_CollectFloating : TestScenarioBase
    {
        private const string Subtype = "SteelPlate";
        private const int Stacks = 5;
        private const int StackAmount = 100;

        private static readonly string[] CollectPerfMethods =
        {
            "ServerTryCollectingFloatingTargets",
            "ServerTryWeldingGrindingCollecting",
            "UpdateBeforeSimulation10_100",
            "AsyncClusterScan",
            "ApplyClusterResultToSelf"
        };

        private static readonly MyDefinitionId SteelId = new MyDefinitionId(typeof(MyObjectBuilder_Component), Subtype);

        private TestRig _rig;
        private readonly List<IMyEntity> _floating = new List<IMyEntity>();

        public override string Id { get { return "S10"; } }
        public override string Description { get { return "One BaR collects 5 floating SteelPlate stacks into cargo (no loss)"; } }
        public override IList<string> PerfMethods { get { return CollectPerfMethods; } }

        public override void Setup(TestContext ctx)
        {
            _floating.Clear();
            _rig = ctx.SpawnRig(new RigSpec { Name = "S10", Bars = 1 });
            ctx.SetModSettings(s => { s.Welder.WorkSpeed = 10; });
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.BoundingBox;
                s.WorkMode = WorkModes.WeldBeforeGrind;
                s.Flags &= ~SyncBlockSettings.Settings.ComponentCollectIfIdle;
            });

            // Stacks 6-8 m above the BaR, well inside the default area.
            var grid = _rig.Grid;
            var center = grid.GridIntegerToWorld(_rig.BarPositions[0]);
            var up = grid.WorldMatrix.Up;
            var right = grid.WorldMatrix.Right;
            var fwd = grid.WorldMatrix.Forward;
            for (int i = 0; i < Stacks; i++)
            {
                var pos = center + up * (6 + (i % 3)) + right * (2 * (i - 2)) + fwd * (i % 2 == 0 ? 2 : -2);
                var ob = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_FloatingObject>();
                ob.Item = new MyObjectBuilder_InventoryItem
                {
                    Amount = (MyFixedPoint)StackAmount,
                    PhysicalContent = new MyObjectBuilder_Component { SubtypeName = Subtype }
                };
                ob.PersistentFlags = MyPersistentEntityFlags2.InScene;
                ob.PositionAndOrientation = new MyPositionAndOrientation(pos, Vector3.Forward, Vector3.Up);
                var entity = MyAPIGateway.Entities.CreateFromObjectBuilderAndAdd(ob);
                if (entity != null) _floating.Add(entity);
            }
        }

        public override bool IsComplete(TestContext ctx)
        {
            return FloatingLeft() == 0 && SteelOnRig() >= Stacks * StackAmount;
        }

        private int FloatingLeft()
        {
            var n = 0;
            for (int i = 0; i < _floating.Count; i++)
            {
                var e = _floating[i];
                if (e != null && !e.Closed && !e.MarkedForClose) n++;
            }
            return n;
        }

        /// <summary>SteelPlate in every rig inventory plus the BaR transport (items in flight).</summary>
        private double SteelOnRig()
        {
            var total = 0.0;
            for (int i = 0; i < _rig.Cargos.Count; i++)
            {
                var inv = _rig.Cargos[i].GetInventory(0);
                if (inv != null) total += (double)inv.GetItemAmount(SteelId);
            }
            for (int b = 0; b < _rig.Bars.Count; b++)
            {
                var bar = _rig.Bars[b];
                var welderInv = bar.Welder != null ? bar.Welder.GetInventory(0) : null;
                if (welderInv != null) total += (double)welderInv.GetItemAmount(SteelId);
                if (bar._TransportInventory != null) total += (double)bar._TransportInventory.GetItemAmount(SteelId);
            }
            return total;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var spawned = Stacks * StackAmount;
            var left = FloatingLeft();
            var onRig = SteelOnRig();
            ctx.Metric("floatingSpawned", (long)_floating.Count);
            ctx.Metric("floatingLeft", (long)left);
            ctx.Metric("steelOnRig", onRig);
            ctx.Check(_floating.Count == Stacks, "floatingSpawned=" + _floating.Count + "/" + Stacks);
            ctx.Check(left == 0, "floatingLeft=" + left);
            // BUG-261023.5: every collected item must arrive — none destroyed, none duplicated.
            ctx.Check(onRig == spawned, "steelOnRig=" + onRig + " expected " + spawned);

            ctx.CheckMethodMaxMax("ServerTryCollectingFloatingTargets", 10.0);
            // Removing floating objects is physics work; first run dipped to 0.82 with mod ticks <= 1.2 ms.
            ctx.Check(ctx.SimMin >= 0.75, "simMin<0.75");
        }
    }
}

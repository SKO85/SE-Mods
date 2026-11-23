using Sandbox.Definitions;
using System.Collections.Generic;
using VRage.Game.ModAPI;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// SCN-GRIND-002 (janitor options, coverage follow-up): an unowned derelict with 4 armor blocks, a battery and 2 small
    /// containers; one BaR (GrindOnly, BoundingBox, janitor NoOwnership). S133 DisableOnly: every functional block is
    /// ground to at or below its critical integrity (no longer functional) and then left alone — not removed; armor
    /// untouched. S134 HackOnly: the same down to the ownership (hack) integrity. Done once nothing changed for 5 s.
    /// </summary>
    internal sealed class S133_JanitorOptions : TestScenarioBase
    {
        private const double Window = 90.0;
        private const double Settle = 5.0;
        private readonly string _id;
        private readonly AutoGrindOptions _option;
        private TestRig _rig, _derelict;
        private readonly List<IMySlimBlock> _functional = new List<IMySlimBlock>();
        private readonly List<IMySlimBlock> _armor = new List<IMySlimBlock>();
        private double _lastSum = -1, _lastChangeAt;

        public S133_JanitorOptions(string id, AutoGrindOptions option)
        {
            _id = id;
            _option = option;
        }

        public override string Id { get { return _id; } }
        public override string Description
        {
            get
            {
                return _option == AutoGrindOptions.DisableOnly
                    ? "Janitor DisableOnly: functional blocks ground until disabled, not removed; armor untouched"
                    : "Janitor HackOnly: functional blocks ground until hackable, not removed; armor untouched";
            }
        }
        public override int TimeoutSeconds { get { return 120; } }

        public override void Setup(TestContext ctx)
        {
            _functional.Clear();
            _armor.Clear();
            _lastSum = -1;
            ctx.SetModSettings(s =>
            {
                s.Welder.WorkSpeed = 3;
                s.Welder.AllowedGrindJanitorRelations |= AutoGrindRelation.NoOwnership;
            });
            _rig = ctx.SpawnRig(new RigSpec { Name = _id, Bars = 1 });
            var derelict = new RigSpec { Name = _id + "-derelict", Bars = 0, Armor = 4, OwnerId = 0, Offset = new Vector3D(0, 0, -25) };
            derelict.Extra.Add(new RigBlock { Kind = RigBlockKind.Battery, Position = new Vector3I(0, 1, 0), Tag = "fn" });
            derelict.Extra.Add(new RigBlock { Kind = RigBlockKind.Cargo, Position = new Vector3I(1, 1, 0), Tag = "fn" });
            derelict.Extra.Add(new RigBlock { Kind = RigBlockKind.Cargo, Position = new Vector3I(2, 1, 0), Tag = "fn" });
            _derelict = ctx.SpawnRig(derelict);
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.BoundingBox;
                s.WorkMode = WorkModes.GrindOnly;
                s.UseGrindJanitorOn = AutoGrindRelation.NoOwnership;
                s.GrindJanitorOptions = _option;
            });
        }

        public override bool IsReady(TestContext ctx)
        {
            if (!base.IsReady(ctx)) return false;
            if (_functional.Count == 0)
            {
                _functional.AddRange(_derelict.Tag("fn"));
                var blocks = new List<IMySlimBlock>();
                _derelict.Grid.GetBlocks(blocks);
                for (int i = 0; i < blocks.Count; i++) if (blocks[i].FatBlock == null) _armor.Add(blocks[i]);
            }
            return _functional.Count == 3;
        }

        private double Threshold(IMySlimBlock b)
        {
            var def = (MyCubeBlockDefinition)b.BlockDefinition;
            if (_option == AutoGrindOptions.DisableOnly) return def.CriticalIntegrityRatio;
            return def.OwnershipIntegrityRatio > 0 ? def.OwnershipIntegrityRatio : def.CriticalIntegrityRatio;
        }

        private static bool Exists(IMySlimBlock b)
        {
            return b != null && !b.IsDestroyed && b.CubeGrid != null && !b.CubeGrid.Closed && b.CubeGrid.GetCubeBlock(b.Position) == b;
        }

        public override void Sample(TestContext ctx)
        {
            if ((TestContext.Frame % 10) != 0) return;
            var sum = ctx.IntegritySum(_functional) + ctx.IntegritySum(_armor);
            if (System.Math.Abs(sum - _lastSum) > 0.01) { _lastSum = sum; _lastChangeAt = ctx.RunSeconds; }
        }

        public override bool IsComplete(TestContext ctx)
        {
            return (ctx.RunSeconds > 5.0 && ctx.RunSeconds - _lastChangeAt >= Settle) || ctx.RunSeconds >= Window;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var removed = 0;
            var aboveThreshold = 0;
            var ratios = "";
            for (int i = 0; i < _functional.Count; i++)
            {
                var b = _functional[i];
                if (!Exists(b)) { removed++; continue; }
                var ratio = b.Integrity / b.MaxIntegrity;
                ratios += (ratios.Length > 0 ? " " : "") + ratio.ToString("F2") + "/" + Threshold(b).ToString("F2");
                if (ratio > Threshold(b) + 0.01) aboveThreshold++;
            }
            var armorDamaged = 0;
            for (int i = 0; i < _armor.Count; i++) if (!Exists(_armor[i]) || _armor[i].Integrity < _armor[i].MaxIntegrity - 0.01) armorDamaged++;
            ctx.Metric("functionalRemoved", (long)removed);
            ctx.Metric("functionalAboveThreshold", (long)aboveThreshold);
            ctx.Metric("ratiosVsThreshold", ratios);
            ctx.Metric("armorDamaged", (long)armorDamaged);
            ctx.Check(_functional.Count == 3 && _armor.Count >= 4, "layout: " + _functional.Count + " functional / " + _armor.Count + " armor");
            ctx.Check(removed == 0, removed + " functional block(s) removed (" + _option + " must stop at the threshold)");
            ctx.Check(aboveThreshold == 0, aboveThreshold + " functional block(s) still above the " + _option + " threshold (" + ratios + ")");
            ctx.Check(armorDamaged == 0, armorDamaged + " armor block(s) ground (" + _option + " takes functional blocks only)");
        }
    }
}

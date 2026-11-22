using SKONanobotBuildAndRepairSystem.Handlers;
using System.Collections.Generic;
using VRage.Game.ModAPI;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// SCN-BUDGET-001: block claims (AssignToSystem). Welding is slowed (WeldingMultiplier 0.1, several weld
    /// calls per block) so claims can be observed. S55 — 2 BaRs weld 10 blocks: blocks are claimed while being
    /// welded, and none is still claimed after the work. S56 — one block; BaR A starts it alone, is switched off
    /// mid-weld while BaR B is switched on: A's claim is released / expires within the TTL and B finishes it.
    /// </summary>
    internal sealed class S55_Claims : TestScenarioBase
    {
        private readonly string _id;
        private readonly bool _takeover;
        private TestRig _rig;

        // S55
        private double _doneAt = -1;
        private double _releasedAt = -1;
        private int _claimsAtDone = -1;
        private int _claimsPeak;

        // S56
        private int _phase;
        private IMySlimBlock _x;
        private double _xStart;
        private bool _xClaimedByA;
        private double _switchAt;
        private double _xReleasedAt = -1;
        private double _xTakenAt = -1;
        private double _xDoneAt = -1;

        public S55_Claims(string id, bool takeover)
        {
            _id = id;
            _takeover = takeover;
        }

        public override string Id { get { return _id; } }
        public override string Description
        {
            get
            {
                return _takeover
                    ? "Claims: BaR switched off mid-weld, its claim expires and the other BaR finishes the block"
                    : "Claims: no block is left claimed after the work is done";
            }
        }
        public override int TimeoutSeconds { get { return _takeover ? 120 : 90; } }

        private static int Ttl { get { return Mod.Settings.AssignmentTtlSeconds > 0 ? Mod.Settings.AssignmentTtlSeconds : 8; } }

        public override void Setup(TestContext ctx)
        {
            _doneAt = _releasedAt = -1;
            _claimsAtDone = -1;
            _claimsPeak = 0;
            _phase = 0;
            _x = null;
            _xClaimedByA = false;
            _xReleasedAt = _xTakenAt = _xDoneAt = -1;
            ctx.SetModSettings(s =>
            {
                s.Welder.WorkSpeed = _takeover ? 1 : 10;
                s.Welder.WeldingMultiplier = 0.1f; // a block takes many weld calls
                s.AssignToSystemEnabled = true;
            });
            _rig = ctx.SpawnRig(new RigSpec { Name = _id, Bars = 2, DamagedArmor = _takeover ? 1 : 10 });
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.StockAllCargo(_rig, "SteelPlate", 300);
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.Grids;
                s.WorkMode = WorkModes.WeldBeforeGrind;
                s.WeldOptions = AutoWeldOptions.WeldFull;
            });
        }

        private int ClaimedTargets()
        {
            var n = 0;
            long owner;
            for (int i = 0; i < _rig.Targets.Count; i++)
                if (_rig.Targets[i] != null && _rig.Targets[i].TryGetAssignedSystem(out owner)) n++;
            return n;
        }

        public override void Sample(TestContext ctx)
        {
            if (!_takeover)
            {
                if (_doneAt < 0)
                {
                    var claimed = ClaimedTargets();
                    if (claimed > _claimsPeak) _claimsPeak = claimed;
                    if (ctx.CountFullIntegrity(_rig.Targets) == _rig.Targets.Count) { _doneAt = ctx.RunSeconds; _claimsAtDone = ClaimedTargets(); }
                    return;
                }
                if (_releasedAt < 0 && ClaimedTargets() == 0) _releasedAt = ctx.RunSeconds;
                return;
            }

            var a = _rig.Bars[0];
            var b = _rig.Bars[1];
            switch (_phase)
            {
                case 0:
                    b.Welder.Enabled = false; // A works alone first
                    _phase = 1;
                    break;
                case 1:
                    if (b.Welder.Enabled) b.Welder.Enabled = false;
                    var current = a.State.CurrentWeldingBlock;
                    if (current == null || current.IsFullIntegrity) break;
                    if (_x == null || _x.CubeGrid != current.CubeGrid || _x.Position != current.Position) { _x = current; _xStart = current.Integrity; break; }
                    if (_x.Integrity <= _xStart + 0.01) break;
                    // A is welding X: claim check, then swap.
                    long owner;
                    _xClaimedByA = _x.TryGetAssignedSystem(out owner) && owner == a.Welder.EntityId;
                    a.Welder.Enabled = false;
                    b.Welder.Enabled = true;
                    _switchAt = ctx.RunSeconds;
                    _phase = 2;
                    break;
                case 2:
                    long claimer;
                    var claimed = _x.TryGetAssignedSystem(out claimer);
                    if (_xReleasedAt < 0 && (!claimed || claimer != a.Welder.EntityId)) _xReleasedAt = ctx.RunSeconds;
                    var bBlock = b.State.CurrentWeldingBlock;
                    if (_xTakenAt < 0 && bBlock != null && bBlock.CubeGrid == _x.CubeGrid && bBlock.Position == _x.Position) _xTakenAt = ctx.RunSeconds;
                    if (_xDoneAt < 0 && _x.IsFullIntegrity) _xDoneAt = ctx.RunSeconds;
                    break;
            }
        }

        public override bool IsComplete(TestContext ctx)
        {
            if (!_takeover)
                return _doneAt >= 0 && (_releasedAt >= 0 || ctx.RunSeconds >= _doneAt + Ttl + 5);
            return _phase == 2 && _xDoneAt >= 0;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            ctx.Metric("ttlSeconds", (long)Ttl);
            if (!_takeover)
            {
                ctx.Metric("claimsPeak", (long)_claimsPeak);
                ctx.Metric("claimsAtDone", (long)_claimsAtDone);
                ctx.Metric("releaseSeconds", _releasedAt >= 0 ? _releasedAt - _doneAt : -1);
                ctx.Check(_doneAt >= 0, "work not finished");
                ctx.Check(_claimsPeak > 0, "no block was ever claimed while being welded");
                ctx.Check(_releasedAt >= 0, ClaimedTargets() + " block(s) still claimed " + Ttl + "+5 s after the work was done");
                return;
            }
            ctx.Metric("claimedByA", _xClaimedByA);
            ctx.Metric("xIntegrityFirstSeen", _x != null ? (double)_xStart : -1);
            ctx.Metric("releaseSeconds", _xReleasedAt >= 0 ? _xReleasedAt - _switchAt : -1);
            ctx.Metric("takeoverSeconds", _xTakenAt >= 0 ? _xTakenAt - _switchAt : -1);
            ctx.Metric("finishSeconds", _xDoneAt >= 0 ? _xDoneAt - _switchAt : -1);
            ctx.Check(_phase == 2, "BaR A never started welding a block");
            ctx.Check(_xClaimedByA, "the block A was welding was not claimed by A");
            ctx.Check(_xReleasedAt >= 0 && _xReleasedAt - _switchAt <= Ttl + 3, "A's claim not released within the TTL (" + Ttl + " s)");
            ctx.Check(_xDoneAt >= 0, "BaR B did not finish A's block");
        }
    }
}

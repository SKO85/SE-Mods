using VRage.Game.ObjectBuilders.Components;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    internal enum SafeZoneMode { WeldForbidden, AllAllowed, GrindForbiddenAtTarget, CheckDisabled }

    /// <summary>
    /// SCN-ZONE-001: safe zones spawned by the harness. S39 zone over the rig without Welding — nothing
    /// welded. S40 zone allowing everything — welded as usual. S41 zone only around a derelict target,
    /// without Grinding — the janitor leaves it alone. S53 (SCN-CFG-003) mod SafeZoneCheckEnabled=false with a
    /// no-welding zone — the BaR ignores the zone and tries to weld. Whether the weld lands is the game's call: in single
    /// player (admin / creative rights) it does; on a dedicated server the game itself refuses welding inside the zone
    /// (Torch, 261123.42: 745 weld calls, 0 welded). The check is that the mod no longer filters the zone.
    /// </summary>
    internal sealed class S39_SafeZone : TestScenarioBase
    {
        private const double Window = 20.0;
        private const float RigZoneRadius = 40f;
        private const float TargetZoneRadius = 15f;
        private const double TargetDistance = 60.0;

        private readonly string _id;
        private readonly SafeZoneMode _mode;
        private TestRig _rig;
        private TestRig _derelict;
        private double _start = -1;

        public S39_SafeZone(string id, SafeZoneMode mode)
        {
            _id = id;
            _mode = mode;
        }

        public override string Id { get { return _id; } }
        public override string Description
        {
            get
            {
                switch (_mode)
                {
                    case SafeZoneMode.WeldForbidden: return "Safe zone without Welding over the BaR: nothing is welded";
                    case SafeZoneMode.AllAllowed: return "Safe zone allowing everything: welded as usual";
                    case SafeZoneMode.GrindForbiddenAtTarget: return "Safe zone without Grinding around a derelict: the janitor leaves it";
                    default: return "SafeZoneCheckEnabled=false: the BaR ignores a no-welding zone and welds";
                }
            }
        }

        public override void Setup(TestContext ctx)
        {
            _start = -1;
            _derelict = null;
            ctx.SetModSettings(s =>
            {
                s.Welder.WorkSpeed = 10;
                s.Welder.AllowedGrindJanitorRelations |= AutoGrindRelation.NoOwnership;
                s.SafeZoneCheckEnabled = _mode != SafeZoneMode.CheckDisabled;
            });

            if (_mode == SafeZoneMode.GrindForbiddenAtTarget)
            {
                _rig = ctx.SpawnRig(new RigSpec { Name = _id, Bars = 1 });
                var offset = new Vector3D(0, 0, -TargetDistance);
                _derelict = ctx.SpawnRig(new RigSpec { Name = _id + "-derelict", Bars = 0, DamagedArmor = 3, DamagedBuildPercent = 1f, OwnerId = 0, Offset = offset });
                ctx.SpawnSafeZone(ctx.Origin + offset, TargetZoneRadius, MySafeZoneAction.All & ~MySafeZoneAction.Grinding);
                return;
            }

            _rig = ctx.SpawnRig(new RigSpec { Name = _id, Bars = 1, DamagedArmor = 5 });
            var allowed = _mode == SafeZoneMode.AllAllowed ? MySafeZoneAction.All : MySafeZoneAction.All & ~MySafeZoneAction.Welding;
            ctx.SpawnSafeZone(ctx.Origin, RigZoneRadius, allowed);
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.StockCargo(_rig, "SteelPlate", 300);
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.BoundingBox;
                s.WeldOptions = AutoWeldOptions.WeldFull;
                if (_mode == SafeZoneMode.GrindForbiddenAtTarget)
                {
                    s.WorkMode = WorkModes.GrindOnly;
                    s.UseGrindJanitorOn = AutoGrindRelation.NoOwnership;
                    s.GrindJanitorOptions = 0;
                }
                else
                {
                    s.WorkMode = WorkModes.WeldBeforeGrind;
                }
            });
        }

        public override void Sample(TestContext ctx)
        {
            if (_rig != null && _rig.Bars.Count > 0 && _rig.Bars[0] != null && _rig.Bars[0].State.WeldBlockedGrids > _maxWeldBlocked)
                _maxWeldBlocked = _rig.Bars[0].State.WeldBlockedGrids;
            if (_start < 0) _start = ctx.IntegritySum(_derelict != null ? _derelict.Targets : _rig.Targets);
        }

        private int _maxWeldBlocked;

        public override bool IsComplete(TestContext ctx)
        {
            if (_mode == SafeZoneMode.AllAllowed)
                return ctx.CountFullIntegrity(_rig.Targets) == _rig.Targets.Count;
            if (_mode == SafeZoneMode.CheckDisabled)
                return ctx.CountFullIntegrity(_rig.Targets) == _rig.Targets.Count || ctx.RunSeconds >= Window;
            return ctx.RunSeconds >= Window;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var bar = _rig.Bars.Count > 0 ? _rig.Bars[0] : null;
            if (bar != null)
            {
                ctx.Metric("zoneAllowsWelding", bar.State.SafeZoneAllowsWelding);
                ctx.Metric("zoneAllowsGrinding", bar.State.SafeZoneAllowsGrinding);
            }

            switch (_mode)
            {
                case SafeZoneMode.WeldForbidden:
                    ctx.Check(ctx.IntegritySum(_rig.Targets) <= _start + 0.01, "welded inside a no-welding safe zone");
                    ctx.Check(bar == null || !bar.State.SafeZoneAllowsWelding, "BaR state says welding is allowed");
                    break;
                case SafeZoneMode.GrindForbiddenAtTarget:
                    var left = ScenarioUtil.AllGone(ctx, _derelict, _derelict.Targets) ? 0 : ctx.CountExisting(_derelict.Targets);
                    ctx.Metric("derelictLeft", (long)left);
                    ctx.Check(left == _derelict.Targets.Count && ctx.IntegritySum(_derelict.Targets) >= _start - 0.01, "ground inside a no-grinding safe zone");
                    break;
                case SafeZoneMode.CheckDisabled:
                    var weldedCd = ctx.CountFullIntegrity(_rig.Targets);
                    long weldCalls = 0;
                    Profiling.MethodProfiler.MethodStatsSnapshot weldStats;
                    if (ctx.TryGetMethodStats("ServerDoWeld", out weldStats)) weldCalls = weldStats.Calls;
                    ctx.Metric("welded", (long)weldedCd);
                    ctx.Metric("weldCalls", weldCalls);
                    ctx.Metric("gameRefusedInZone", weldedCd == 0 && weldCalls > 0);
                    ctx.Metric("dedicated", Sandbox.ModAPI.MyAPIGateway.Utilities.IsDedicated);
                    ctx.Metric("weldBlockedShown", (long)_maxWeldBlocked);
                    // BUG-261123.8: refused welds are not retried every tick (one try per 30 s per grid, plus slack).
                    if (weldedCd == 0 && weldCalls > 0)
                    {
                        ctx.Check(weldCalls <= 5, "the BaR retried a refused weld " + weldCalls + " times in " + Window + " s (expected <= 5)");
                        ctx.Check(_maxWeldBlocked >= 1, "the BaR never reported 'Welding blocked by a safe zone / protection'");
                    }
                    ctx.Check(bar == null || bar.State.SafeZoneAllowsWelding, "BaR state says the zone forbids welding although the check is off");
                    ctx.Check(weldCalls > 0, "the BaR never tried to weld inside the zone (the mod still applies the zone)");
                    ctx.Check(weldedCd == _rig.Targets.Count || weldedCd == 0, "welded=" + weldedCd + "/" + _rig.Targets.Count + " (partly)");
                    break;
                default:
                    var welded = ctx.CountFullIntegrity(_rig.Targets);
                    ctx.Metric("welded", (long)welded);
                    ctx.Check(welded == _rig.Targets.Count, "welded=" + welded + "/" + _rig.Targets.Count);
                    break;
            }
        }
    }
}

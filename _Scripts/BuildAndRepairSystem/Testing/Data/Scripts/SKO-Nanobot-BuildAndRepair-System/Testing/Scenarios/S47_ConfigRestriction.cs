using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    internal enum RestrictionMode { JanitorRelation, WorkMode, SearchMode }

    /// <summary>
    /// SCN-CFG-003: admin restrictions in ModSettings win over the block settings (CheckLimits on every
    /// settings change, as for a client message). S47 AllowedGrindJanitorRelations = Enemies only — a BaR set to
    /// NoOwnership does not grind the derelict. S48 AllowedWorkModes = WeldOnly — a BaR set to GrindOnly welds
    /// its own blocks and grinds nothing. S49 AllowedSearchModes = Grids — a BaR set to BoundingBox does not
    /// reach a separate grid in range, but still welds its own.
    /// </summary>
    internal sealed class S47_ConfigRestriction : TestScenarioBase
    {
        private const double SettleSeconds = 10.0;
        private const double Window = 20.0;

        private readonly string _id;
        private readonly RestrictionMode _mode;
        private TestRig _rig;
        private TestRig _other;
        private double _otherStart = -1;
        private double _doneAt = -1;

        public S47_ConfigRestriction(string id, RestrictionMode mode)
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
                    case RestrictionMode.JanitorRelation: return "Admin janitor relations = Enemies: NoOwnership janitor is forced off, derelict kept";
                    case RestrictionMode.WorkMode: return "Admin work modes = WeldOnly: GrindOnly is forced to WeldOnly, nothing ground";
                    default: return "Admin search modes = Grids: BoundingBox is forced to Grids, separate grid untouched";
                }
            }
        }

        public override void Setup(TestContext ctx)
        {
            _otherStart = -1;
            _doneAt = -1;
            ctx.SetModSettings(s =>
            {
                s.Welder.WorkSpeed = 10;
                switch (_mode)
                {
                    case RestrictionMode.JanitorRelation:
                        s.Welder.AllowedGrindJanitorRelations = AutoGrindRelation.Enemies;
                        break;
                    case RestrictionMode.WorkMode:
                        s.Welder.AllowedGrindJanitorRelations |= AutoGrindRelation.NoOwnership;
                        s.Welder.AllowedWorkModes = WorkModes.WeldOnly;
                        s.Welder.WorkModeDefault = WorkModes.WeldOnly;
                        break;
                    default:
                        s.Welder.AllowedSearchModes = SearchModes.Grids;
                        s.Welder.SearchModeDefault = SearchModes.Grids;
                        break;
                }
            });

            _rig = ctx.SpawnRig(new RigSpec { Name = _id, Bars = 1, DamagedArmor = _mode == RestrictionMode.JanitorRelation ? 0 : 3 });
            if (_mode == RestrictionMode.SearchMode)
            {
                var spec = new RigSpec { Name = _id + "-separate", Bars = 0, DamagedArmor = 3, Offset = new Vector3D(0, 0, -20) };
                spec.Extra.Add(new RigBlock { Kind = RigBlockKind.Battery, Position = new Vector3I(0, 0, 1), Tag = "battery" });
                _other = ctx.SpawnRig(spec);
            }
            else
            {
                _other = ctx.SpawnRig(new RigSpec { Name = _id + "-derelict", Bars = 0, DamagedArmor = 3, DamagedBuildPercent = 1f, OwnerId = 0, Offset = new Vector3D(0, 0, -25) });
            }
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.StockCargo(_rig, "SteelPlate", 300);
            ctx.ConfigureBars(_rig, s =>
            {
                // What a player would try; CheckLimits in SettingsChanged must pull it back.
                s.SearchMode = SearchModes.BoundingBox;
                s.WorkMode = _mode == RestrictionMode.JanitorRelation || _mode == RestrictionMode.WorkMode ? WorkModes.GrindOnly : WorkModes.WeldBeforeGrind;
                s.WeldOptions = AutoWeldOptions.WeldFull;
                s.UseGrindJanitorOn = AutoGrindRelation.NoOwnership;
                s.GrindJanitorOptions = 0;
            });
        }

        public override void Sample(TestContext ctx)
        {
            if (_otherStart < 0) _otherStart = ctx.IntegritySum(_other.Targets);
            if (_doneAt < 0 && ctx.CountFullIntegrity(_rig.Targets) == _rig.Targets.Count) _doneAt = ctx.RunSeconds;
        }

        public override bool IsComplete(TestContext ctx)
        {
            if (_rig.Targets.Count == 0) return ctx.RunSeconds >= Window;
            return _doneAt >= 0 && ctx.RunSeconds >= _doneAt + SettleSeconds;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var s = _rig.Bars.Count > 0 ? _rig.Bars[0].Settings : null;
            if (s != null)
            {
                ctx.Metric("workMode", s.WorkMode.ToString());
                ctx.Metric("searchMode", s.SearchMode.ToString());
                ctx.Metric("janitor", s.UseGrindJanitorOn.ToString());
            }
            var otherLeft = ScenarioUtil.AllGone(ctx, _other, _other.Targets) ? 0 : ctx.CountExisting(_other.Targets);
            var otherGain = ctx.IntegritySum(_other.Targets) - _otherStart;
            ctx.Metric("otherLeft", (long)otherLeft);
            ctx.Metric("otherIntegrityChange", otherGain);
            ctx.Check(otherLeft == _other.Targets.Count && System.Math.Abs(otherGain) <= 0.01, "the restricted action still happened on " + _other.Name);

            switch (_mode)
            {
                case RestrictionMode.JanitorRelation:
                    ctx.Check(s == null || (s.UseGrindJanitorOn & AutoGrindRelation.NoOwnership) == 0, "janitor NoOwnership not cleared");
                    break;
                case RestrictionMode.WorkMode:
                    ctx.Check(s == null || s.WorkMode == WorkModes.WeldOnly, "work mode not forced to WeldOnly");
                    ctx.Check(ctx.CountFullIntegrity(_rig.Targets) == _rig.Targets.Count, "own blocks not welded");
                    break;
                default:
                    ctx.Check(s == null || s.SearchMode == SearchModes.Grids, "search mode not forced to Grids");
                    ctx.Check(ctx.CountFullIntegrity(_rig.Targets) == _rig.Targets.Count, "own blocks not welded");
                    break;
            }
        }
    }
}

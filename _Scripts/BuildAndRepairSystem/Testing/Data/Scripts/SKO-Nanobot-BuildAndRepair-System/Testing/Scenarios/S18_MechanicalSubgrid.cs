using Sandbox.ModAPI;
using System.Collections.Generic;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// SCN-WELD-007: a mechanical block on the rig (rotor, advanced rotor, hinge, piston, wheel suspension)
    /// gets its top part through the game's own "add top part" terminal action; 3 damaged armor blocks
    /// built by the caller go onto that subgrid (no owner there). Grids mode reaches the subgrid through
    /// the mechanical connection and welds it; the janitor (NoOwnership on) must not grind it (BUG-261122.7).
    /// </summary>
    internal sealed class S18_MechanicalSubgrid : TestScenarioBase
    {
        private static readonly Vector3I BasePos = new Vector3I(-1, 1, 0); // on top of the cargo at (-1,0,0)

        private readonly string _id;
        private readonly string _label;
        private readonly RigBlockKind _kind;
        private readonly string _addTopAction;
        private TestRig _rig;
        private IMyCubeGrid _top;
        private readonly List<IMySlimBlock> _subgridTargets = new List<IMySlimBlock>();
        private bool _topRequested;
        private double _topRequestedAt;
        private bool _reported;

        public S18_MechanicalSubgrid(string id, string label, RigBlockKind kind, string addTopAction)
        {
            _id = id;
            _label = label;
            _kind = kind;
            _addTopAction = addTopAction;
        }

        public override string Id { get { return _id; } }
        public override string Description { get { return _label + " subgrid: damaged armor on the top part is welded, not ground"; } }

        public override void Setup(TestContext ctx)
        {
            _top = null;
            _subgridTargets.Clear();
            _topRequested = false;
            _reported = false;
            ctx.SetModSettings(s =>
            {
                s.Welder.WorkSpeed = 10;
                s.Welder.AllowedGrindJanitorRelations |= AutoGrindRelation.NoOwnership;
            });
            var spec = new RigSpec { Name = _id, Bars = 1 };
            spec.Extra.Add(new RigBlock { Kind = _kind, Position = BasePos, Tag = "base" });
            _rig = ctx.SpawnRig(spec);
        }

        protected override void Configure(TestContext ctx)
        {
            ScenarioUtil.StockEverything(ctx, _rig, 50);
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.Grids;
                s.WorkMode = WorkModes.WeldBeforeGrind;
                s.WeldOptions = AutoWeldOptions.WeldFull;
                s.UseGrindJanitorOn = AutoGrindRelation.NoOwnership;
                s.GrindJanitorOptions = 0;
            });
        }

        public override bool IsReady(TestContext ctx)
        {
            if (!base.IsReady(ctx)) return false;
            if (_top != null) return true;

            var baseSlim = _rig.Tag("base").Count > 0 ? _rig.Tag("base")[0] : null;
            var mech = baseSlim != null ? baseSlim.FatBlock as IMyMechanicalConnectionBlock : null;
            if (mech == null) return false;

            if (mech.TopGrid == null || mech.Top == null)
            {
                if (!_topRequested)
                {
                    // Look the action up first: ApplyAction throws on an unknown id (pistons and suspensions
                    // use "Add Top Part"; AddPistonHead / AddWheel are only label keys).
                    var actions = new List<Sandbox.ModAPI.Interfaces.ITerminalAction>();
                    mech.GetActions(actions);
                    Sandbox.ModAPI.Interfaces.ITerminalAction action = null;
                    var ids = "";
                    for (int i = 0; i < actions.Count; i++)
                    {
                        ids += (i > 0 ? ", " : "") + actions[i].Id;
                        if (actions[i].Id == _addTopAction) action = actions[i];
                    }
                    if (action == null)
                    {
                        ctx.Check(false, "no action '" + _addTopAction + "' on " + _label + "; available: " + ids);
                        _topRequested = true;
                        _reported = true;
                        return false;
                    }
                    action.Apply(mech);
                    _topRequested = true;
                    _topRequestedAt = ctx.RunSecondsSinceSetup;
                }
                else if (!_reported && ctx.RunSecondsSinceSetup - _topRequestedAt > 10.0)
                {
                    ctx.Check(false, "no top part after '" + _addTopAction + "'");
                    _reported = true;
                }
                return false;
            }

            // Top part is there: 3 damaged armor blocks on it, along its up direction (away from the base).
            _top = mech.TopGrid;
            ctx.TrackSpawned(_top);
            var top = mech.Top;
            var up = Base6Directions.GetIntVector(top.Orientation.Up);
            for (int k = 1; k <= 3; k++)
            {
                var ob = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_CubeBlock>("LargeBlockArmorBlock");
                ob.Min = top.Position + up * k;
                ob.BlockOrientation = new SerializableBlockOrientation(Base6Directions.Direction.Forward, Base6Directions.Direction.Up);
                ob.BuildPercent = 0.2f;
                ob.IntegrityPercent = 0.2f;
                ob.Owner = ctx.OwnerId;
                ob.BuiltBy = ctx.OwnerId;
                ob.ColorMaskHSV = new SerializableVector3(0f, -1f, 0f);
                var added = _top.AddBlock(ob, false);
                if (added != null) _subgridTargets.Add(added);
            }
            return false; // next poll: ready
        }

        public override bool IsComplete(TestContext ctx)
        {
            return _subgridTargets.Count > 0 && ctx.CountFullIntegrity(_subgridTargets) == _subgridTargets.Count;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var welded = ctx.CountFullIntegrity(_subgridTargets);
            ctx.Metric("subgrid", _label);
            ctx.Metric("subgridBlocks", (long)_subgridTargets.Count);
            ctx.Metric("welded", (long)welded);
            ctx.Check(_subgridTargets.Count == 3, "subgrid armor added=" + _subgridTargets.Count + "/3");
            ctx.Check(ctx.CountExisting(_subgridTargets) == _subgridTargets.Count, "subgrid armor was ground");
            ctx.Check(welded == _subgridTargets.Count, "subgrid welded=" + welded + "/" + _subgridTargets.Count);
        }
    }
}

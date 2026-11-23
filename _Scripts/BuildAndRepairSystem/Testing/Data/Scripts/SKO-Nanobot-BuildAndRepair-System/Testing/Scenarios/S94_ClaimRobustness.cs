using SKONanobotBuildAndRepairSystem.Handlers;
using System.Collections.Generic;
using VRage.Game.ModAPI;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    internal enum ClaimCase
    {
        /// <summary>2 BaRs weld 10 blocks: never more half-welded blocks than BaRs, none left at the end.</summary>
        WeldFocus,
        /// <summary>2 BaRs grind a derelict: never more half-ground blocks than BaRs, all gone at the end.</summary>
        GrindFocus,
        /// <summary>BaR A is destroyed mid-weld: its claim is released at once, B finishes the block.</summary>
        WelderDestroyed,
        /// <summary>BaR A loses power mid-weld: its claim expires after the TTL, B (other ship) finishes the block.</summary>
        WelderUnpowered,
        /// <summary>BaR A is switched off mid-grind: its claim expires, B finishes grinding the block.</summary>
        GrinderStopped
    }

    /// <summary>
    /// SCN-BUDGET-001: claims under stress. Focus variants check that BaRs finish what they start (no trail of
    /// half-done blocks); takeover variants check that a block whose BaR stopped working is picked up by another BaR.
    /// </summary>
    internal sealed class S94_ClaimRobustness : TestScenarioBase
    {
        private const double Window = 120.0;

        private readonly string _id;
        private readonly ClaimCase _case;
        private TestRig _rig, _other;

        // focus
        private readonly List<IMySlimBlock> _blocks = new List<IMySlimBlock>();
        private readonly List<float> _start = new List<float>();
        private int _inProgressPeak;
        private double _doneAt = -1;

        // takeover
        private int _phase;
        private IMySlimBlock _x;
        private Vector3I _xPos;
        private IMyCubeGrid _xGrid;
        private float _xStart;
        private bool _xClaimedByA;
        private long _aId;
        private double _eventAt = -1, _xReleasedAt = -1, _xTakenAt = -1, _xDoneAt = -1;

        public S94_ClaimRobustness(string id, ClaimCase claimCase)
        {
            _id = id;
            _case = claimCase;
        }

        private bool Focus { get { return _case == ClaimCase.WeldFocus || _case == ClaimCase.GrindFocus; } }
        private bool Grinding { get { return _case == ClaimCase.GrindFocus || _case == ClaimCase.GrinderStopped; } }
        private static int Ttl { get { return Mod.Settings.AssignmentTtlSeconds > 0 ? Mod.Settings.AssignmentTtlSeconds : 8; } }

        public override string Id { get { return _id; } }
        public override string Description
        {
            get
            {
                switch (_case)
                {
                    case ClaimCase.WeldFocus: return "Claims: 2 BaRs finish each block they start (never more half-welded blocks than BaRs)";
                    case ClaimCase.GrindFocus: return "Claims: 2 BaRs finish each block they grind (never more half-ground blocks than BaRs)";
                    case ClaimCase.WelderDestroyed: return "Claims: BaR destroyed mid-weld, its claim is freed at once and the other BaR finishes";
                    case ClaimCase.WelderUnpowered: return "Claims: BaR loses power mid-weld, its claim expires and a BaR on another ship finishes";
                    default: return "Claims: BaR switched off mid-grind, its claim expires and the other BaR finishes grinding";
                }
            }
        }
        public override int TimeoutSeconds { get { return 150; } }

        public override void Setup(TestContext ctx)
        {
            _blocks.Clear();
            _start.Clear();
            _inProgressPeak = 0;
            _doneAt = -1;
            _phase = 0;
            _x = null;
            _xGrid = null;
            _xClaimedByA = false;
            _eventAt = _xReleasedAt = _xTakenAt = _xDoneAt = -1;
            _other = null;
            ctx.SetModSettings(s =>
            {
                s.AssignToSystemEnabled = true;
                s.Welder.WorkSpeed = Focus ? 10 : 1;
                s.Welder.WeldingMultiplier = 0.1f;
                s.Welder.GrindingMultiplier = 0.1f;
                if (Grinding) s.Welder.AllowedGrindJanitorRelations |= AutoGrindRelation.NoOwnership;
            });

            switch (_case)
            {
                case ClaimCase.WeldFocus:
                    _rig = ctx.SpawnRig(new RigSpec { Name = _id, Bars = 2, DamagedArmor = 10 });
                    break;
                case ClaimCase.WelderDestroyed:
                    _rig = ctx.SpawnRig(new RigSpec { Name = _id, Bars = 2, DamagedArmor = 1 });
                    break;
                case ClaimCase.WelderUnpowered:
                    // A on its own ship (own battery) with the block; B on another ship 25 m away.
                    _rig = ctx.SpawnRig(new RigSpec { Name = _id, Bars = 1, DamagedArmor = 1 });
                    _other = ctx.SpawnRig(new RigSpec { Name = _id + "-helper", Bars = 1, Offset = new Vector3D(0, 0, -25) });
                    break;
                default:
                    _rig = ctx.SpawnRig(new RigSpec { Name = _id, Bars = 2 });
                    _other = ctx.SpawnRig(new RigSpec { Name = _id + "-derelict", Bars = 0, Armor = _case == ClaimCase.GrindFocus ? 10 : 1, OwnerId = 0, Offset = new Vector3D(0, 0, -25) });
                    break;
            }
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.StockAllCargo(_rig, "SteelPlate", 300);
            if (_case == ClaimCase.WelderUnpowered) ctx.StockAllCargo(_other, "SteelPlate", 300);
            System.Action<Models.SyncBlockSettings> edit = s =>
            {
                s.SearchMode = SearchModes.BoundingBox;
                s.WeldOptions = AutoWeldOptions.WeldFull;
                if (Grinding)
                {
                    s.WorkMode = WorkModes.GrindOnly;
                    s.UseGrindJanitorOn = AutoGrindRelation.NoOwnership;
                    s.GrindJanitorOptions = 0;
                }
                else s.WorkMode = WorkModes.WeldOnly;
            };
            ctx.ConfigureBars(_rig, edit);
            if (_case == ClaimCase.WelderUnpowered) ctx.ConfigureBars(_other, edit);
        }

        private NanobotSystem BarA { get { return _rig.Bars[0]; } }
        private NanobotSystem BarB { get { return _case == ClaimCase.WelderUnpowered ? _other.Bars[0] : _rig.Bars[1]; } }

        private static bool Exists(IMyCubeGrid grid, Vector3I pos)
        {
            return grid != null && !grid.Closed && !grid.MarkedForClose && grid.GetCubeBlock(pos) != null;
        }

        private static IMySlimBlock Current(NanobotSystem bar, bool grinding)
        {
            if (bar == null || bar.Welder == null || bar.Welder.Closed) return null;
            return grinding ? bar.State.CurrentGrindingBlock : bar.State.CurrentWeldingBlock;
        }

        public override void Sample(TestContext ctx)
        {
            if ((TestContext.Frame % 30) != 0) return;
            if (Focus) SampleFocus(ctx);
            else SampleTakeover(ctx);
        }

        private void SampleFocus(TestContext ctx)
        {
            if (_blocks.Count == 0)
            {
                if (Grinding)
                {
                    var grid = _other.Grid;
                    if (grid == null) return;
                    grid.GetBlocks(_blocks);
                }
                else _blocks.AddRange(_rig.Targets);
                for (int i = 0; i < _blocks.Count; i++) _start.Add(_blocks[i].Integrity);
                return;
            }
            var inProgress = 0;
            var remaining = 0;
            for (int i = 0; i < _blocks.Count; i++)
            {
                var b = _blocks[i];
                if (Grinding)
                {
                    if (!Exists(b.CubeGrid, b.Position)) continue;
                    remaining++;
                    if (b.Integrity < _start[i] - 0.01f) inProgress++;
                }
                else
                {
                    if (b.IsFullIntegrity) continue;
                    remaining++;
                    if (b.Integrity > _start[i] + 0.01f) inProgress++;
                }
            }
            if (inProgress > _inProgressPeak) _inProgressPeak = inProgress;
            if (_doneAt < 0 && remaining == 0) _doneAt = ctx.RunSeconds;
        }

        private void SampleTakeover(TestContext ctx)
        {
            var a = BarA;
            var b = BarB;
            switch (_phase)
            {
                case 0:
                    if (b.Welder != null) b.Welder.Enabled = false; // A works alone first
                    _phase = 1;
                    break;
                case 1:
                    if (b.Welder.Enabled) b.Welder.Enabled = false;
                    var current = Current(a, Grinding);
                    if (current == null) break;
                    if (_x == null || _x.CubeGrid != current.CubeGrid || _x.Position != current.Position)
                    {
                        _x = current;
                        _xGrid = current.CubeGrid;
                        _xPos = current.Position;
                        _xStart = current.Integrity;
                        break;
                    }
                    var worked = Grinding ? _x.Integrity < _xStart - 0.01f : _x.Integrity > _xStart + 0.01f;
                    if (!worked) break;
                    long owner;
                    _aId = a.Welder.EntityId;
                    _xClaimedByA = _x.TryGetAssignedSystem(out owner) && owner == _aId;
                    // Something happens to A while it works on X.
                    if (_case == ClaimCase.WelderDestroyed) a.Welder.CubeGrid.RazeBlock(a.Welder.Position);
                    else if (_case == ClaimCase.WelderUnpowered) { if (_rig.Battery != null) _rig.Battery.Enabled = false; }
                    else a.Welder.Enabled = false;
                    b.Welder.Enabled = true;
                    _eventAt = ctx.RunSeconds;
                    _phase = 2;
                    break;
                case 2:
                    long claimer;
                    var stillA = Exists(_xGrid, _xPos) && _x.TryGetAssignedSystem(out claimer) && claimer == _aId;
                    if (_xReleasedAt < 0 && !stillA) _xReleasedAt = ctx.RunSeconds;
                    var bBlock = Current(b, Grinding);
                    if (_xTakenAt < 0 && bBlock != null && bBlock.CubeGrid == _xGrid && bBlock.Position == _xPos) _xTakenAt = ctx.RunSeconds;
                    var done = Grinding ? !Exists(_xGrid, _xPos) : _x.IsFullIntegrity;
                    if (_xDoneAt < 0 && done) _xDoneAt = ctx.RunSeconds;
                    break;
            }
        }

        public override bool IsComplete(TestContext ctx)
        {
            if (ctx.RunSeconds >= Window) return true;
            return Focus ? _doneAt >= 0 : _phase == 2 && _xDoneAt >= 0;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            ctx.Metric("case", _case.ToString());
            ctx.Metric("ttlSeconds", (long)Ttl);
            if (Focus)
            {
                ctx.Metric("blocks", (long)_blocks.Count);
                ctx.Metric("inProgressPeak", (long)_inProgressPeak);
                ctx.Metric("doneS", _doneAt);
                ctx.Check(_blocks.Count > 0, "layout: no target blocks found");
                ctx.Check(_doneAt >= 0, "work not finished within " + Window + " s");
                ctx.Check(_inProgressPeak <= 2, _inProgressPeak + " blocks were half-" + (Grinding ? "ground" : "welded") + " at once with 2 BaRs (BaRs left blocks unfinished)");
                return;
            }
            ctx.Metric("claimedByA", _xClaimedByA);
            ctx.Metric("releaseSeconds", _xReleasedAt >= 0 ? _xReleasedAt - _eventAt : -1);
            ctx.Metric("takeoverSeconds", _xTakenAt >= 0 ? _xTakenAt - _eventAt : -1);
            ctx.Metric("finishSeconds", _xDoneAt >= 0 ? _xDoneAt - _eventAt : -1);
            ctx.Check(_phase == 2, "BaR A never started working on a block");
            ctx.Check(_xClaimedByA, "the block A was working on was not claimed by A");
            var maxRelease = _case == ClaimCase.WelderDestroyed ? 3.0 : Ttl + 3.0;
            ctx.Check(_xReleasedAt >= 0 && _xReleasedAt - _eventAt <= maxRelease, "A's claim not released within " + maxRelease + " s");
            ctx.Check(_xDoneAt >= 0, "BaR B did not finish A's block");
        }
    }
}

using Sandbox.ModAPI;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    internal enum ShieldMode
    {
        /// <summary>Pirate ship, shield up: the janitor must not grind it.</summary>
        EnemyShieldUp,
        /// <summary>Pirate ship, controller switched off (functional, shield down): ground like any enemy.</summary>
        EnemyShieldOff,
        /// <summary>Pirate ship, shield up, ShieldCheckEnabled=false: ground through the shield.</summary>
        CheckDisabled,
        /// <summary>Grinding through the shield (check off), check switched on mid-grind: grinding stops.</summary>
        CheckOnMidGrind,
        /// <summary>BaR ship inside its own shield: own damaged blocks are welded.</summary>
        OwnShieldWeld,
        /// <summary>Friendly ship with its shield up: welded by a BaR outside the shield.</summary>
        FriendlyShieldWeld
    }

    /// <summary>
    /// SCN-ZONE-001 (DefenseShields): BaR behaviour around DS shields. Skipped when DefenseShields is not loaded
    /// (or its API is not ready), and the enemy modes when the world has no pirates (SPRT). Shield ships carry a
    /// DS controller (DSControlLarge) and a ship emitter (EmitterL) at the end of a single armor row, with two
    /// batteries; the measured run starts only once the shield is online (or, for EnemyShieldOff, down again).
    /// </summary>
    internal sealed class S58_Shields : TestScenarioBase
    {
        private const int GrindBlocks = 10;
        private const int WeldBlocks = 6;
        private const double ShieldUpWindow = 20.0;
        private const double MidGrindGrace = 3.0;
        private const double MidGrindWatch = 10.0;

        private readonly string _id;
        private readonly ShieldMode _mode;
        private TestRig _bar;
        private TestRig _shieldRig;
        private long _pirateId;

        private double _onlineAt = -1;
        private double _downAt = -1;
        private bool _reported;
        private bool _probed;
        private bool _probeBlockProtected;
        private bool _probeProtectedByShield;
        private bool _probeGridOnline;
        private string _probeStatus = "";
        private double _integrityStart = -1;
        private double _switchedAt = -1;
        private double _integrityAfterGrace = -1;

        public S58_Shields(string id, ShieldMode mode)
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
                    case ShieldMode.EnemyShieldUp: return "DefenseShields: pirate ship with its shield up is not ground";
                    case ShieldMode.EnemyShieldOff: return "DefenseShields: pirate ship with its shield switched off is ground";
                    case ShieldMode.CheckDisabled: return "DefenseShields: ShieldCheckEnabled=false grinds through a raised shield";
                    case ShieldMode.CheckOnMidGrind: return "DefenseShields: shield check switched on mid-grind stops the grinding";
                    case ShieldMode.OwnShieldWeld: return "DefenseShields: BaR ship inside its own shield welds its own blocks";
                    default: return "DefenseShields: friendly ship with its shield up is welded from outside";
                }
            }
        }
        public override int TimeoutSeconds { get { return 90; } }
        public override int ReadyTimeoutSeconds { get { return 120; } }

        private bool Enemy { get { return _mode != ShieldMode.OwnShieldWeld && _mode != ShieldMode.FriendlyShieldWeld; } }
        private bool Weld { get { return !Enemy; } }

        public override string SkipReason(TestContext ctx)
        {
            if (Mod.Shield == null || !Mod.Shield.IsReady)
                return "DefenseShields not loaded (or ShieldCheckEnabled off at load)";
            if (Enemy)
            {
                var pirates = MyAPIGateway.Session.Factions != null ? MyAPIGateway.Session.Factions.TryGetFactionByTag("SPRT") : null;
                if (pirates == null || pirates.FounderId == 0) return "no SPRT faction in this world";
                _pirateId = pirates.FounderId;
            }
            return null;
        }

        public override void Setup(TestContext ctx)
        {
            _onlineAt = _downAt = _integrityStart = _switchedAt = _integrityAfterGrace = -1;
            _reported = _probed = false;
            _probeStatus = "";
            var slow = _mode == ShieldMode.CheckOnMidGrind;
            ctx.SetModSettings(s =>
            {
                s.Welder.WorkSpeed = slow ? 1 : 10;
                if (slow) s.Welder.GrindingMultiplier = 0.1f;
                s.ShieldCheckEnabled = _mode != ShieldMode.CheckDisabled && _mode != ShieldMode.CheckOnMidGrind;
                s.Welder.AllowedGrindJanitorRelations |= AutoGrindRelation.Enemies;
            });

            if (_mode == ShieldMode.OwnShieldWeld)
            {
                // BaR ship with its own shield: emitter / controller / battery on the left of the rig's base layer.
                var own = new RigSpec { Name = _id, Bars = 1, Dynamic = true, DamagedArmor = WeldBlocks, RowWidth = WeldBlocks };
                own.Extra.Add(new RigBlock { Kind = RigBlockKind.ShieldEmitter, Position = new Vector3I(-2, 0, 0), Tag = "emitter" });
                own.Extra.Add(new RigBlock { Kind = RigBlockKind.ShieldController, Position = new Vector3I(-2, 0, 1), Tag = "controller" });
                own.Extra.Add(new RigBlock { Kind = RigBlockKind.Battery, Position = new Vector3I(-2, 0, 2), Tag = "battery" });
                _bar = ctx.SpawnRig(own);
                _shieldRig = _bar;
                return;
            }

            _bar = ctx.SpawnRig(new RigSpec { Name = _id, Bars = 1 });
            var n = Weld ? WeldBlocks : GrindBlocks;
            var ship = new RigSpec
            {
                Name = _id + (Enemy ? "-enemy" : "-friendly"),
                Bars = 0,
                Dynamic = true,
                DamagedArmor = n,
                DamagedBuildPercent = Weld ? 0.2f : 1f, // grind targets intact but tracked
                RowWidth = n,
                OwnerId = Enemy ? _pirateId : -1,
                Offset = new Vector3D(0, 0, 60)
            };
            // Single row along X; emitter at the free end (clear line of sight), controller + batteries at the other end.
            ship.Extra.Add(new RigBlock { Kind = RigBlockKind.ShieldEmitter, Position = new Vector3I(-1, 0, 0), Tag = "emitter" });
            ship.Extra.Add(new RigBlock { Kind = RigBlockKind.ShieldController, Position = new Vector3I(n, 0, 0), Tag = "controller" });
            ship.Extra.Add(new RigBlock { Kind = RigBlockKind.Battery, Position = new Vector3I(n + 1, 0, 0), Tag = "battery" });
            ship.Extra.Add(new RigBlock { Kind = RigBlockKind.Battery, Position = new Vector3I(n + 2, 0, 0), Tag = "battery" });
            _shieldRig = ctx.SpawnRig(ship);
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.StockAllCargo(_bar, "SteelPlate", 300);
            ctx.ConfigureBars(_bar, s =>
            {
                s.SearchMode = _mode == ShieldMode.OwnShieldWeld ? SearchModes.Grids : SearchModes.BoundingBox;
                s.WorkMode = Weld ? WorkModes.WeldBeforeGrind : WorkModes.GrindOnly;
                s.WeldOptions = AutoWeldOptions.WeldFull;
                s.UseGrindJanitorOn = Enemy ? AutoGrindRelation.Enemies : 0;
                s.GrindJanitorOptions = 0;
                s.AreaOffset = Vector3.Zero;
                s.AreaSize = new Vector3(200f, 200f, 200f);
            });
        }

        private IMyFunctionalBlock Controller()
        {
            var tagged = _shieldRig.Tag("controller");
            return tagged.Count > 0 && tagged[0] != null ? tagged[0].FatBlock as IMyFunctionalBlock : null;
        }

        private bool ShieldOnline(IMyFunctionalBlock controller)
        {
            return Mod.Shield.GridShieldOnline(_shieldRig.Grid) || Mod.Shield.IsShieldUp(controller);
        }

        private string Status(IMyFunctionalBlock controller)
        {
            var s = controller != null ? Mod.Shield.ShieldStatus(controller) : "no controller";
            return string.IsNullOrEmpty(s) ? "(empty)" : s.Replace(';', ',').Replace('=', ':').Replace('\n', ' ').Replace('\r', ' ');
        }

        public override bool IsReady(TestContext ctx)
        {
            if (!base.IsReady(ctx)) return false;
            var controller = Controller();
            if (controller == null || _shieldRig.Grid == null) return false;

            var online = ShieldOnline(controller);
            if (_onlineAt < 0)
            {
                if (online)
                {
                    _onlineAt = ctx.RunSecondsSinceSetup;
                    if (_mode != ShieldMode.EnemyShieldOff) return true;
                    controller.Enabled = false; // functional controller, shield down
                    return false;
                }
                if (!_reported && ctx.RunSecondsSinceSetup > ReadyTimeoutSeconds - 5)
                {
                    ctx.Check(false, "shield never came online (status " + Status(controller) + ")");
                    _reported = true;
                }
                return false;
            }
            if (!online)
            {
                _downAt = ctx.RunSecondsSinceSetup;
                return true;
            }
            if (!_reported && ctx.RunSecondsSinceSetup > ReadyTimeoutSeconds - 5)
            {
                ctx.Check(false, "shield stayed online after the controller was switched off (status " + Status(controller) + ")");
                _reported = true;
            }
            return false;
        }

        private void Probe(TestContext ctx)
        {
            _probed = true;
            var block = _shieldRig.Targets.Count > 0 ? _shieldRig.Targets[0] : null;
            _probeBlockProtected = block != null && Mod.Shield.IsBlockProtected(block);
            _probeProtectedByShield = Mod.Shield.ProtectedByShield(_shieldRig.Grid);
            _probeGridOnline = Mod.Shield.GridShieldOnline(_shieldRig.Grid);
            _probeStatus = Status(Controller());
            _integrityStart = ctx.IntegritySum(_shieldRig.Targets);
        }

        public override void Sample(TestContext ctx)
        {
            if (!_probed) Probe(ctx);
            if (_mode != ShieldMode.CheckOnMidGrind) return;

            var integrity = ctx.IntegritySum(_shieldRig.Targets);
            if (_switchedAt < 0)
            {
                if (integrity < _integrityStart - 0.01)
                {
                    ctx.SetModSettings(s => { s.ShieldCheckEnabled = true; });
                    _switchedAt = ctx.RunSeconds;
                }
                return;
            }
            if (_integrityAfterGrace < 0 && ctx.RunSeconds >= _switchedAt + MidGrindGrace) _integrityAfterGrace = integrity;
        }

        public override bool IsComplete(TestContext ctx)
        {
            switch (_mode)
            {
                case ShieldMode.EnemyShieldUp:
                    return ctx.RunSeconds >= ShieldUpWindow;
                case ShieldMode.EnemyShieldOff:
                case ShieldMode.CheckDisabled:
                    return ScenarioUtil.AllGone(ctx, _shieldRig, _shieldRig.Targets) || ctx.RunSeconds >= 30;
                case ShieldMode.CheckOnMidGrind:
                    if (ScenarioUtil.AllGone(ctx, _shieldRig, _shieldRig.Targets)) return true;
                    return _switchedAt >= 0 ? ctx.RunSeconds >= _switchedAt + MidGrindGrace + MidGrindWatch : ctx.RunSeconds >= 40;
                default:
                    return ctx.CountFullIntegrity(_shieldRig.Targets) == _shieldRig.Targets.Count || ctx.RunSeconds >= 30;
            }
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var targets = _shieldRig.Targets;
            var gone = ScenarioUtil.AllGone(ctx, _shieldRig, targets);
            var left = gone ? 0 : ctx.CountExisting(targets);
            var integrity = gone ? 0 : ctx.IntegritySum(targets);
            ctx.Metric("shieldOnlineS", _onlineAt);
            if (_mode == ShieldMode.EnemyShieldOff) ctx.Metric("shieldDownS", _downAt);
            ctx.Metric("apiIsBlockProtected", _probeBlockProtected);
            ctx.Metric("apiProtectedByShield", _probeProtectedByShield);
            ctx.Metric("apiGridShieldOnline", _probeGridOnline);
            ctx.Metric("shieldStatus", _probeStatus);
            ctx.Metric("targetBlocks", (long)targets.Count);
            ctx.Metric("left", (long)left);

            switch (_mode)
            {
                case ShieldMode.EnemyShieldUp:
                    ctx.Check(left == targets.Count && integrity >= _integrityStart - 0.01,
                        "shielded pirate ship was ground (" + (targets.Count - left) + " blocks gone)");
                    break;
                case ShieldMode.EnemyShieldOff:
                    ctx.Check(left == 0, "pirate ship with its shield down was not ground: " + left + "/" + targets.Count +
                        " left (DS IsBlockProtected=" + _probeBlockProtected + " with the shield down)");
                    break;
                case ShieldMode.CheckDisabled:
                    ctx.Check(left == 0, "ShieldCheckEnabled=false but the shielded pirate ship was not ground: " + left + "/" + targets.Count + " left");
                    break;
                case ShieldMode.CheckOnMidGrind:
                    var lostAfter = _integrityAfterGrace >= 0 ? _integrityAfterGrace - integrity : -1;
                    ctx.Metric("switchedAtS", _switchedAt);
                    ctx.Metric("integrityLostAfterGrace", lostAfter);
                    ctx.Check(_switchedAt >= 0, "grinding through the shield never started (check off)");
                    ctx.Check(!gone, "ship fully ground before the shield check took effect");
                    ctx.Check(lostAfter >= 0 && lostAfter < 0.01, "still grinding " + MidGrindGrace + " s after the shield check was switched on (lost " + lostAfter + ")");
                    break;
                default:
                    var welded = ctx.CountFullIntegrity(targets);
                    ctx.Metric("welded", (long)welded);
                    if (_mode == ShieldMode.OwnShieldWeld && _bar.Bars.Count > 0) ctx.Metric("barIsShielded", _bar.Bars[0].State.IsShielded);
                    ctx.Check(welded == targets.Count, "welded=" + welded + "/" + targets.Count + " inside the shield");
                    break;
            }
        }
    }
}

using SKONanobotBuildAndRepairSystem.Models;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    internal enum SyncLoadMode { SettingsChurn, BigTargetList, Idle }

    /// <summary>
    /// FEAT-261123.7, network load for dedicated servers (run on Torch with a client connected; in single player the
    /// traffic checks are skipped, the CPU checks still run).
    /// S116 SettingsChurn: 20 BaRs whose settings a script / timer toggles every 0.5 s for 20 s (ShowArea on all of
    /// them). Settings broadcasts must stay at <= 1 per BaR per TransmitSettingsIntervalSeconds (1 s), not one per change.
    /// S117 BigTargetList: one BaR (BoundingBox, area 60 m) facing 1,500 damaged armor blocks while welding: the state
    /// message with the target list must stay small (<= 64 KB) and sends <= 1 per BaR per s.
    /// S118 Idle: 50 BaRs with nothing to do for 30 s (after a 10 s settle): traffic should be close to zero
    /// (<= 0.1 state sends per BaR per s, no settings broadcasts).
    /// </summary>
    internal sealed class S116_SyncLoad : TestScenarioBase
    {
        private const double Window = 20.0;
        private const double ToggleEvery = 0.5;
        private readonly string _id;
        private readonly SyncLoadMode _mode;
        private TestRig _rig, _target;
        private double _nextToggle;
        private int _toggles;

        public S116_SyncLoad(string id, SyncLoadMode mode)
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
                    case SyncLoadMode.SettingsChurn: return "Sync: 20 BaRs with settings toggled every 0.5 s by a script (settings broadcast rate)";
                    case SyncLoadMode.BigTargetList: return "Sync: one BaR with a 1,500-block target list (state message size and rate)";
                    default: return "Sync: 50 idle BaRs, nothing to do (traffic should be close to zero)";
                }
            }
        }
        public override int TimeoutSeconds { get { return 90; } }

        public override void Setup(TestContext ctx)
        {
            _nextToggle = 0;
            _toggles = 0;
            _idleSnapshot = false;
            ctx.SetModSettings(s => { s.Welder.WorkSpeed = 1; });
            if (_mode == SyncLoadMode.SettingsChurn)
            {
                _rig = ctx.SpawnRig(new RigSpec { Name = _id, Bars = 20, DamagedArmor = 40, RowWidth = 40 });
            }
            else if (_mode == SyncLoadMode.Idle)
            {
                _rig = ctx.SpawnRig(new RigSpec { Name = _id, Bars = 50 });
            }
            else
            {
                _rig = ctx.SpawnRig(new RigSpec { Name = _id, Bars = 1 });
                _target = ctx.SpawnRig(new RigSpec { Name = _id + "-target", Bars = 0, DamagedArmor = 1500, RowWidth = 50, Offset = new Vector3D(-60, -6, -40) });
            }
        }

        protected override void Configure(TestContext ctx)
        {
            if (_mode != SyncLoadMode.Idle) ctx.StockCargo(_rig, "SteelPlate", 2000);
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = _mode == SyncLoadMode.SettingsChurn ? SearchModes.Grids : SearchModes.BoundingBox;
                s.WorkMode = WorkModes.WeldOnly;
                if (_mode == SyncLoadMode.Idle) s.WorkMode = WorkModes.WeldBeforeGrind;
                if (_mode == SyncLoadMode.BigTargetList) s.AreaSize = new Vector3(60f, 60f, 60f);
            });
        }

        private const double IdleSettle = 10.0;
        private bool _idleSnapshot;

        public override void Sample(TestContext ctx)
        {
            if (_mode == SyncLoadMode.Idle)
            {
                // Start counting once the first scans and state sends after switching on are done.
                if (!_idleSnapshot && ctx.RunSeconds >= IdleSettle) { ctx.NetStart = SKONanobotBuildAndRepairSystem.Handlers.NetStats.Take(true); _idleSnapshot = true; }
                return;
            }
            if (_mode != SyncLoadMode.SettingsChurn) return;
            var now = ctx.RunSeconds;
            if (now < _nextToggle || now >= Window) return;
            _nextToggle = now + ToggleEvery;
            _toggles++;
            for (int i = 0; i < _rig.Bars.Count; i++)
            {
                var bar = _rig.Bars[i];
                if (bar == null) continue;
                bar.Settings.Flags ^= SyncBlockSettings.Settings.ShowArea;
                bar.SettingsChanged();
            }
        }

        public override bool IsComplete(TestContext ctx)
        {
            return ctx.RunSeconds >= (_mode == SyncLoadMode.Idle ? IdleSettle + 30.0 : Window);
        }

        public override void Teardown(TestContext ctx)
        {
            if (_rig != null)
                for (int i = 0; i < _rig.Bars.Count; i++)
                    if (_rig.Bars[i] != null) _rig.Bars[i].Settings.Flags &= ~SyncBlockSettings.Settings.ShowArea;
            base.Teardown(ctx);
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var players = ctx.RemotePlayers();
            var bars = _rig.Bars.Count > 0 ? _rig.Bars.Count : 1;
            var secs = _mode == SyncLoadMode.Idle ? ctx.RunSeconds - IdleSettle : ctx.RunSeconds;
            if (secs < 1) secs = 1;
            long sMsgs, sBytes, sWire, stMsgs, stBytes, stWire;
            int sMax, stMax;
            ctx.NetSent(40102, out sMsgs, out sBytes, out sWire, out sMax);
            ctx.NetSent(40104, out stMsgs, out stBytes, out stWire, out stMax);
            ctx.Metric("toggles", (long)_toggles);
            ctx.Metric("settingsPerBarPerS", sMsgs / (double)bars / secs);
            ctx.Metric("statePerBarPerS", stMsgs / (double)bars / secs);
            ctx.Metric("stateMaxKB", stMax / 1024.0);
            var bar0 = _rig.Bars.Count > 0 ? _rig.Bars[0] : null;
            if (bar0 != null) ctx.Metric("barWeldTargets", (long)bar0.State.PossibleWeldTargets.CurrentCount);

            ctx.Check(ctx.TickAvgMs <= 1.0, "tick avg " + ctx.TickAvgMs.ToString("F2") + " ms > 1 ms");
            if (players == 0)
            {
                ctx.Metric("netChecks", "skipped (no remote player connected)");
                return;
            }
            if (_mode == SyncLoadMode.Idle)
            {
                ctx.Check(stMsgs / (double)bars / secs <= 0.1, "idle BaRs sent " + stMsgs + " state messages in " + secs.ToString("F0") + " s (" + (stMsgs / (double)bars / secs).ToString("F2") + " per BaR per s, expected <= 0.1)");
                ctx.Check(sMsgs == 0, "idle BaRs broadcast settings " + sMsgs + " times without any change");
            }
            else if (_mode == SyncLoadMode.SettingsChurn)
            {
                // 2 changes per second per BaR; broadcasts are held to one per second per BaR (+ slack for the edges).
                ctx.Check(sMsgs <= (long)(bars * (Window + 2)), "settings broadcasts " + sMsgs + " for " + bars + " BaRs in " + Window + " s (expected <= 1 per BaR per s)");
                ctx.Check(sMsgs > 0, "no settings broadcast at all while settings changed (clients never see the change)");
            }
            else
            {
                ctx.Check(stMax <= 64 * 1024, "largest state message " + stMax + " bytes > 64 KB (spike risk)");
                ctx.Check(stMsgs <= (long)(bars * (secs + 2)), "state sends " + stMsgs + " in " + secs.ToString("F0") + " s for " + bars + " BaR(s) (expected <= 1 per BaR per s)");
            }
        }
    }
}

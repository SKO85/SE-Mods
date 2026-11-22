using Sandbox.ModAPI;
using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using VRageMath;
using IMyProgrammableBlock = Sandbox.ModAPI.IMyProgrammableBlock;
using IMyTextSurfaceProvider = Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;
using IMyAssembler = Sandbox.ModAPI.Ingame.IMyAssembler;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// SCN-API-002: the companion script end to end, with its shipped configuration, in a real PB. Rig: BaR in
    /// group BuildAndRepairGroup1 with 5 damaged blocks and empty cargo, an assembler in AssemblerGroup1, an LCD
    /// "BuildAndRepairGroup1StatusPanel", a tagged LCD "Hangar LCD [BaR:1]" (Custom Data: Status page) and a
    /// cockpit seat "Cockpit" (default display Cockpit[0]). Checks: missing SteelPlate is queued; the displays are
    /// written; "info-only" stops queuing, survives a recompile; "queue" resumes. Skipped when in-game scripts
    /// are disabled. The script text ships in the -Testing variant as Data/CompanionScript.txt.
    /// </summary>
    internal sealed class S71_CompanionScript : TestScenarioBase
    {
        private const double QueueWait = 40.0;
        private const double QuietWindow = 8.0;
        private const string TaggedCustomData = "@BaR\nKinds=Status\n@/BaR";

        private TestRig _rig;
        private string _script;
        private bool _failed;
        private bool _pbEnabled;
        private int _phase;
        private double _phaseAt;
        private readonly List<MyProductionItem> _queue = new List<MyProductionItem>();

        private double _queuedAt = -1;
        private double _queuedAmount = -1;
        private bool _statusLcdWritten;
        private bool _taggedShowsEnabled;
        private bool _cockpitWritten;
        private double _queuedDuringInfoOnly = -1;
        private bool _taggedShowsInfoOnly;
        private string _storageAfterInfoOnly = "";
        private double _queuedAfterRecompile = -1;
        private double _requeuedAt = -1;

        public override string Id { get { return "S71"; } }
        public override string Description { get { return "Companion script in a real PB: assembler queuing, LCDs, info-only / queue arguments, recompile"; } }
        public override int TimeoutSeconds { get { return 120; } }

        public override string SkipReason(TestContext ctx)
        {
            var settings = MyAPIGateway.Session.SessionSettings;
            if (settings != null && !settings.EnableIngameScripts) return "in-game scripts are disabled in this world";
            if (Mod.Settings.Welder.ScriptControllFixed) return "script control is locked (ScriptControllFixed): EnsureQueued not published";
            return null;
        }

        public override void Setup(TestContext ctx)
        {
            _failed = _pbEnabled = false;
            _phase = 0;
            _queuedAt = _queuedAmount = _queuedDuringInfoOnly = _queuedAfterRecompile = _requeuedAt = -1;
            _statusLcdWritten = _taggedShowsEnabled = _cockpitWritten = _taggedShowsInfoOnly = false;
            _storageAfterInfoOnly = "";
            _script = TestHarnessComponent.ReadModFile("Data/CompanionScript.txt");
            ctx.SetModSettings(s => { s.Welder.WorkSpeed = 10; });

            var spec = new RigSpec { Name = "S71", Bars = 1, DamagedArmor = 5, BarGroup = "BuildAndRepairGroup1" };
            spec.Extra.Add(new RigBlock { Kind = RigBlockKind.Assembler, Position = new Vector3I(2, 0, 0), Name = "S71 Assembler", Groups = new[] { "AssemblerGroup1" }, Tag = "assembler" });
            spec.Extra.Add(new RigBlock { Kind = RigBlockKind.ProgrammableBlock, Position = new Vector3I(3, 0, 0), Name = "S71 PB", Enabled = false, Program = _script ?? "", Tag = "pb" });
            spec.Extra.Add(new RigBlock { Kind = RigBlockKind.Lcd, Position = new Vector3I(3, 0, 1), Name = "BuildAndRepairGroup1StatusPanel", Tag = "status" });
            spec.Extra.Add(new RigBlock { Kind = RigBlockKind.Lcd, Position = new Vector3I(2, 0, 2), Name = "Hangar LCD [BaR:1]", Tag = "tagged" });
            spec.Extra.Add(new RigBlock { Kind = RigBlockKind.CockpitSeat, Position = new Vector3I(3, 0, 2), Name = "Cockpit", Tag = "cockpit" });
            _rig = ctx.SpawnRig(spec);
        }

        private T Fat<T>(string tag) where T : class
        {
            var t = _rig.Tag(tag);
            return t.Count > 0 && t[0] != null ? t[0].FatBlock as T : null;
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.Grids;
                s.WorkMode = WorkModes.WeldBeforeGrind;
                s.WeldOptions = AutoWeldOptions.WeldFull;
            });
            var tagged = Fat<Sandbox.ModAPI.IMyTerminalBlock>("tagged");
            if (tagged != null) tagged.CustomData = TaggedCustomData; // set before the PB first runs
        }

        public override bool IsReady(TestContext ctx)
        {
            if (!base.IsReady(ctx)) return false;
            if (_script == null)
            {
                ctx.Check(false, "Data/CompanionScript.txt missing in the -Testing variant (build_post.cmd)");
                _failed = true;
                return true;
            }
            var pb = Fat<IMyProgrammableBlock>("pb");
            if (pb == null) return false;
            if (pb.HasCompileErrors)
            {
                ctx.Check(false, "companion script does not compile in game: " + Clean(pb.DetailedInfo));
                _failed = true;
                return true;
            }
            if (!_pbEnabled)
            {
                pb.Enabled = true; // first run (init) after the tagged LCD's Custom Data is in place
                _pbEnabled = true;
            }
            return true;
        }

        private static string Clean(string s)
        {
            return string.IsNullOrEmpty(s) ? "" : s.Replace(';', ',').Replace('\n', ' ').Replace('\r', ' ');
        }

        private static string Text(IMyTextSurface surface)
        {
            return surface != null ? surface.GetText() ?? "" : "";
        }

        private double Queued()
        {
            return CompanionScriptContract.QueuedSteel(Fat<IMyAssembler>("assembler"), _queue);
        }

        public override void Sample(TestContext ctx)
        {
            if (_failed) return;
            var pb = Fat<IMyProgrammableBlock>("pb");
            var assembler = Fat<IMyAssembler>("assembler");
            if (pb == null || assembler == null) return;
            var now = ctx.RunSeconds;

            switch (_phase)
            {
                case 0: // script queues the missing SteelPlate
                    var queued = Queued();
                    if (queued <= 0) return;
                    _queuedAt = now;
                    _queuedAmount = queued;
                    _statusLcdWritten = Text(Fat<IMyTextSurface>("status")).Length > 0;
                    _taggedShowsEnabled = Text(Fat<IMyTextSurface>("tagged")).Contains("Enabled (1 assembler)");
                    var cockpit = Fat<IMyTextSurfaceProvider>("cockpit");
                    _cockpitWritten = cockpit != null && cockpit.SurfaceCount > 0 && Text(cockpit.GetSurface(0)).Length > 0;
                    pb.TryRun("info-only");
                    assembler.ClearQueue();
                    _phase = 1;
                    _phaseAt = now;
                    return;
                case 1: // info-only: nothing queued for a while
                    if (now < _phaseAt + QuietWindow) return;
                    _queuedDuringInfoOnly = Queued();
                    _taggedShowsInfoOnly = Text(Fat<IMyTextSurface>("tagged")).Contains("Disabled (info-only)");
                    _storageAfterInfoOnly = pb.StorageData ?? "";
                    pb.Recompile();
                    _phase = 2;
                    _phaseAt = now;
                    return;
                case 2: // still info-only after a recompile
                    if (now < _phaseAt + QuietWindow) return;
                    _queuedAfterRecompile = Queued();
                    pb.TryRun("queue");
                    _phase = 3;
                    _phaseAt = now;
                    return;
                case 3: // queue resumes
                    if (Queued() > 0) { _requeuedAt = now - _phaseAt; _phase = 4; }
                    return;
            }
        }

        public override bool IsComplete(TestContext ctx)
        {
            if (_failed || _phase == 4) return true;
            if (_phase == 0 && ctx.RunSeconds > QueueWait) return true;
            return _phase == 3 && ctx.RunSeconds > _phaseAt + 15;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            if (_failed) return;
            ctx.Metric("queuedAtS", _queuedAt);
            ctx.Metric("queuedSteel", _queuedAmount);
            ctx.Metric("statusLcdWritten", _statusLcdWritten);
            ctx.Metric("taggedShowsEnabled", _taggedShowsEnabled);
            ctx.Metric("cockpitWritten", _cockpitWritten);
            ctx.Metric("queuedDuringInfoOnly", _queuedDuringInfoOnly);
            ctx.Metric("taggedShowsInfoOnly", _taggedShowsInfoOnly);
            ctx.Metric("storageAfterInfoOnly", Clean(_storageAfterInfoOnly));
            ctx.Metric("queuedAfterRecompile", _queuedAfterRecompile);
            ctx.Metric("requeueS", _requeuedAt);

            var pb = Fat<IMyProgrammableBlock>("pb");
            ctx.Check(_queuedAt >= 0, "missing SteelPlate never queued in AssemblerGroup1 within " + QueueWait + " s (PB: " + Clean(pb != null ? pb.DetailedInfo : "") + ")");
            if (_queuedAt < 0) return;
            ctx.Check(_statusLcdWritten, "LCD 'BuildAndRepairGroup1StatusPanel' not written");
            ctx.Check(_taggedShowsEnabled, "tagged LCD [BaR:1] does not show 'Auto-queuing ... Enabled (1 assembler)'");
            ctx.Check(_cockpitWritten, "Cockpit[0] not written");
            ctx.Check(_queuedDuringInfoOnly == 0, "queued " + _queuedDuringInfoOnly + " SteelPlate after 'info-only'");
            ctx.Check(_taggedShowsInfoOnly, "tagged LCD does not show 'Disabled (info-only)'");
            ctx.Check(_queuedAfterRecompile == 0, "info-only lost on recompile: " + _queuedAfterRecompile + " SteelPlate queued");
            ctx.Check(_requeuedAt >= 0, "'queue' did not resume queuing within 15 s");
        }
    }
}

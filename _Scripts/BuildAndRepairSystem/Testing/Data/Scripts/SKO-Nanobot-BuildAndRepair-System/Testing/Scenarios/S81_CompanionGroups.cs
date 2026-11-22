using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using System.Collections.Generic;
using VRage.Game;
using VRage.Scripting.MemorySafeTypes;
using VRageMath;
using IMyAssembler = Sandbox.ModAPI.Ingame.IMyAssembler;
using IMyProgrammableBlock = Sandbox.ModAPI.IMyProgrammableBlock;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;
using MyProductionItem = Sandbox.ModAPI.Ingame.MyProductionItem;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    internal enum CompanionGroupsMode
    {
        /// <summary>Two queuing groups; group B's BaR grinds only (needs nothing): only assembler A may queue.</summary>
        TwoGroups,
        /// <summary>One group, two assemblers: total queued = missing, both used, no 0-amount items, stable 20 s.</summary>
        TwoAssemblers,
        /// <summary>Plenty of SteelPlate in cargo while welding: nothing queued.</summary>
        StockInCargo
    }

    /// <summary>
    /// SCN-API-002: the companion script with several groups / assemblers and stock, in a real PB. The harness swaps
    /// the script's group config (between "BuildAndRepairSystemQueuingGroups = {" and "};") for the variant's.
    /// Rig: 2 BaRs (per-BaR terminal groups), assemblers A (4,0,0) and B (5,0,0), PB (6,0,0), LCDs "S81 Panel A" /
    /// "S81 Panel B". Skipped when in-game scripts are disabled or script control is locked.
    /// </summary>
    internal sealed class S81_CompanionGroups : TestScenarioBase
    {
        private const string ConfigStart = "static BuildAndRepairSystemQueuingGroup[] BuildAndRepairSystemQueuingGroups = {";
        private const double QueueWait = 40.0;
        private const double StableWindow = 20.0;

        private readonly string _id;
        private readonly CompanionGroupsMode _mode;
        private TestRig _rig;
        private string _script;
        private string _setupError;
        private bool _failed;
        private bool _pbEnabled;
        private readonly List<MyProductionItem> _queue = new List<MyProductionItem>();

        private double _firstQueuedAt = -1;
        private double _checkedAt = -1;
        private double _queuedA = -1, _queuedB = -1, _queuedTotalFirst = -1, _queuedTotalLater = -1;
        private int _missingSteel = -1;
        private int _zeroAmountItems;
        private double _queuedMax;
        private int _missingMax;
        private double _integrityStart = -1, _integrityEnd = -1;
        private string _panelA = "", _panelB = "";

        public S81_CompanionGroups(string id, CompanionGroupsMode mode)
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
                    case CompanionGroupsMode.TwoGroups: return "Companion script, two groups: each queues into its own assembler and titles its own LCD";
                    case CompanionGroupsMode.TwoAssemblers: return "Companion script, two assemblers: queue = missing, both used, no 0 items, stable 20 s";
                    default: return "Companion script, stock in cargo: nothing is queued while the BaR welds from stock";
                }
            }
        }
        public override int TimeoutSeconds { get { return 120; } }

        public override string SkipReason(TestContext ctx)
        {
            var settings = MyAPIGateway.Session.SessionSettings;
            if (settings != null && !settings.EnableIngameScripts) return "in-game scripts are disabled in this world";
            if (Mod.Settings.Welder.ScriptControllFixed) return "script control is locked (ScriptControllFixed): EnsureQueued not published";
            return null;
        }

        private static string Group(string bars, string assemblers, string panel)
        {
            return "   new BuildAndRepairSystemQueuingGroup() {\n" +
                   "      BuildAndRepairSystemGroupName = \"" + bars + "\",\n" +
                   "      AssemblerGroupName = \"" + assemblers + "\",\n" +
                   "      Displays = new [] { new DisplayDefinition { DisplayNames = new [] { \"" + panel + "\" }, DisplayKinds = new [] { DisplayKind.Status } } }\n" +
                   "   }";
        }

        private string Config()
        {
            var body = _mode == CompanionGroupsMode.TwoGroups
                ? Group("S81 BaRs A", "S81 Assemblers A", "S81 Panel A") + ",\n" + Group("S81 BaRs B", "S81 Assemblers B", "S81 Panel B")
                : Group("S81 BaRs A", "S81 Assemblers A", "S81 Panel A");
            return ConfigStart + "\n" + body + "\n};";
        }

        /// <summary>The shipped script with this variant's group config, or null (error in _setupError).</summary>
        private string BuildScript(string script)
        {
            if (script == null) { _setupError = "Data/CompanionScript.txt missing in the -Testing variant"; return null; }
            var start = script.IndexOf(ConfigStart, System.StringComparison.Ordinal);
            var end = start >= 0 ? script.IndexOf("\n};", start, System.StringComparison.Ordinal) : -1;
            if (start < 0 || end < 0) { _setupError = "group config not found in the script (config block changed?)"; return null; }
            return script.Substring(0, start) + Config() + script.Substring(end + 3);
        }

        public override void Setup(TestContext ctx)
        {
            _failed = _pbEnabled = false;
            _setupError = null;
            _firstQueuedAt = _checkedAt = _queuedA = _queuedB = _queuedTotalFirst = _queuedTotalLater = -1;
            _missingSteel = -1;
            _zeroAmountItems = 0;
            _queuedMax = 0;
            _missingMax = 0;
            _integrityStart = _integrityEnd = -1;
            _panelA = _panelB = "";
            _script = BuildScript(TestHarnessComponent.ReadModFile("Data/CompanionScript.txt"));
            ctx.SetModSettings(s => { s.Welder.WorkSpeed = _mode == CompanionGroupsMode.StockInCargo ? 1 : 10; });

            var spec = new RigSpec
            {
                Name = _id, Bars = 2,
                DamagedArmor = _mode == CompanionGroupsMode.StockInCargo ? 30 : 5,
                BarGroups = _mode == CompanionGroupsMode.TwoGroups ? new[] { "S81 BaRs A", "S81 BaRs B" } : new[] { "S81 BaRs A", "S81 BaRs A" }
            };
            spec.Extra.Add(new RigBlock { Kind = RigBlockKind.Assembler, Position = new Vector3I(4, 0, 0), Name = "S81 Assembler A", Groups = new[] { "S81 Assemblers A" }, Tag = "asmA" });
            spec.Extra.Add(new RigBlock
            {
                Kind = RigBlockKind.Assembler, Position = new Vector3I(5, 0, 0), Name = "S81 Assembler B", Tag = "asmB",
                Groups = _mode == CompanionGroupsMode.TwoGroups ? new[] { "S81 Assemblers B" } : _mode == CompanionGroupsMode.TwoAssemblers ? new[] { "S81 Assemblers A" } : null
            });
            spec.Extra.Add(new RigBlock { Kind = RigBlockKind.ProgrammableBlock, Position = new Vector3I(6, 0, 0), Name = "S81 PB", Enabled = false, Program = _script ?? "", Tag = "pb" });
            spec.Extra.Add(new RigBlock { Kind = RigBlockKind.Lcd, Position = new Vector3I(4, 0, 2), Name = "S81 Panel A", Tag = "panelA" });
            spec.Extra.Add(new RigBlock { Kind = RigBlockKind.Lcd, Position = new Vector3I(5, 0, 2), Name = "S81 Panel B", Tag = "panelB" });
            _rig = ctx.SpawnRig(spec);
        }

        private T Fat<T>(string tag) where T : class
        {
            var t = _rig.Tag(tag);
            return t.Count > 0 && t[0] != null ? t[0].FatBlock as T : null;
        }

        protected override void Configure(TestContext ctx)
        {
            if (_mode == CompanionGroupsMode.StockInCargo) ctx.StockAllCargo(_rig, "SteelPlate", 500);
            for (int i = 0; i < _rig.Bars.Count; i++)
            {
                var grindOnly = _mode == CompanionGroupsMode.TwoGroups && i == 1; // group B needs nothing
                ctx.ConfigureBar(_rig.Bars[i], s =>
                {
                    s.SearchMode = SearchModes.Grids;
                    s.WorkMode = grindOnly ? WorkModes.GrindOnly : WorkModes.WeldBeforeGrind;
                    s.WeldOptions = AutoWeldOptions.WeldFull;
                    s.UseGrindJanitorOn = 0;
                });
            }
        }

        public override bool IsReady(TestContext ctx)
        {
            if (!base.IsReady(ctx)) return false;
            if (_setupError != null) { ctx.Check(false, _setupError); _failed = true; return true; }
            var pb = Fat<IMyProgrammableBlock>("pb");
            if (pb == null) return false;
            if (pb.HasCompileErrors)
            {
                ctx.Check(false, "script with the " + _mode + " config does not compile: " + (pb.DetailedInfo ?? "").Replace(';', ',').Replace('\n', ' '));
                _failed = true;
                return true;
            }
            if (!_pbEnabled) { pb.Enabled = true; _pbEnabled = true; }
            return true;
        }

        private double Queued(string tag, bool countZero)
        {
            var asm = Fat<IMyAssembler>(tag);
            if (asm == null) return 0;
            _queue.Clear();
            asm.GetQueue(_queue);
            double total = 0;
            for (int i = 0; i < _queue.Count; i++)
            {
                if (countZero && (double)_queue[i].Amount <= 0) _zeroAmountItems++;
                if (_queue[i].BlueprintId.SubtypeName == "SteelPlate") total += (double)_queue[i].Amount;
            }
            return total;
        }

        private int MissingSteel()
        {
            var max = 0;
            foreach (var bar in _rig.Bars)
            {
                try
                {
                    var dict = bar.Welder.GetValue<MemorySafeDictionary<MyDefinitionId, int>>("BuildAndRepair.MissingComponents");
                    int n;
                    if (dict != null && dict.TryGetValue(CompanionScriptContract.SteelPlate, out n) && n > max) max = n;
                }
                catch { }
            }
            return max;
        }

        private static string Text(IMyTextSurface s) { return s != null ? s.GetText() ?? "" : ""; }

        public override void Sample(TestContext ctx)
        {
            if (_failed || (TestContext.Frame % 30) != 0) return;
            var now = ctx.RunSeconds;
            if (_integrityStart < 0) _integrityStart = ctx.IntegritySum(_rig.Targets);
            _integrityEnd = ctx.IntegritySum(_rig.Targets);

            if (_mode == CompanionGroupsMode.StockInCargo)
            {
                var q = Queued("asmA", true) + Queued("asmB", true);
                if (q > _queuedMax) _queuedMax = q;
                var m = MissingSteel();
                if (m > _missingMax) _missingMax = m;
                return;
            }

            var total = Queued("asmA", false) + Queued("asmB", false);
            if (_firstQueuedAt < 0)
            {
                if (total > 0) _firstQueuedAt = now;
                return;
            }
            if (_mode == CompanionGroupsMode.TwoGroups)
            {
                if (_checkedAt < 0 && now >= _firstQueuedAt + 10)
                {
                    _checkedAt = now;
                    _queuedA = Queued("asmA", true);
                    _queuedB = Queued("asmB", true);
                    _panelA = Text(Fat<IMyTextSurface>("panelA"));
                    _panelB = Text(Fat<IMyTextSurface>("panelB"));
                }
                return;
            }
            // TwoAssemblers
            if (_queuedTotalFirst < 0 && now >= _firstQueuedAt + 5)
            {
                _queuedA = Queued("asmA", true);
                _queuedB = Queued("asmB", true);
                _queuedTotalFirst = _queuedA + _queuedB;
                _missingSteel = MissingSteel();
                _checkedAt = now;
                return;
            }
            if (_queuedTotalFirst >= 0 && _queuedTotalLater < 0 && now >= _checkedAt + StableWindow)
                _queuedTotalLater = Queued("asmA", true) + Queued("asmB", true);
        }

        public override bool IsComplete(TestContext ctx)
        {
            if (_failed) return true;
            var now = ctx.RunSeconds;
            switch (_mode)
            {
                case CompanionGroupsMode.StockInCargo: return now >= 25;
                case CompanionGroupsMode.TwoGroups: return _checkedAt >= 0 || (_firstQueuedAt < 0 && now > QueueWait);
                default: return _queuedTotalLater >= 0 || (_firstQueuedAt < 0 && now > QueueWait);
            }
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            if (_failed) return;
            ctx.Metric("mode", _mode.ToString());
            switch (_mode)
            {
                case CompanionGroupsMode.TwoGroups:
                    ctx.Metric("queuedA", _queuedA);
                    ctx.Metric("queuedB", _queuedB);
                    ctx.Check(_firstQueuedAt >= 0, "nothing queued within " + QueueWait + " s");
                    ctx.Check(_queuedA > 0, "group A did not queue into its assembler");
                    ctx.Check(_queuedB == 0, "assembler B (group B, nothing missing) got " + _queuedB + " SteelPlate — groups leak into each other");
                    ctx.Check(_panelA.StartsWith("BaR Group 1 ("), "Panel A does not show group 1 (\"" + Head(_panelA) + "\")");
                    ctx.Check(_panelB.StartsWith("BaR Group 2 ("), "Panel B does not show group 2 (\"" + Head(_panelB) + "\")");
                    break;
                case CompanionGroupsMode.TwoAssemblers:
                    ctx.Metric("missingSteel", (long)_missingSteel);
                    ctx.Metric("queuedA", _queuedA);
                    ctx.Metric("queuedB", _queuedB);
                    ctx.Metric("queuedTotal", _queuedTotalFirst);
                    ctx.Metric("queuedTotalAfter20s", _queuedTotalLater);
                    ctx.Metric("zeroAmountItems", (long)_zeroAmountItems);
                    ctx.Check(_firstQueuedAt >= 0, "nothing queued within " + QueueWait + " s");
                    ctx.Check(_missingSteel > 0 && System.Math.Abs(_queuedTotalFirst - _missingSteel) < 1, "queued " + _queuedTotalFirst + " SteelPlate for " + _missingSteel + " missing");
                    ctx.Check(_queuedA > 0 && _queuedB > 0, "not spread over both assemblers (A " + _queuedA + ", B " + _queuedB + ")");
                    ctx.Check(_zeroAmountItems == 0, _zeroAmountItems + " queue entries with amount 0");
                    ctx.Check(_queuedTotalLater >= 0 && System.Math.Abs(_queuedTotalLater - _queuedTotalFirst) < 1, "queue changed from " + _queuedTotalFirst + " to " + _queuedTotalLater + " in " + StableWindow + " s without production");
                    break;
                default:
                    ctx.Metric("queuedMax", _queuedMax);
                    ctx.Metric("missingSteelMax", (long)_missingMax);
                    ctx.Metric("integrityGain", _integrityEnd - _integrityStart);
                    ctx.Check(_integrityEnd - _integrityStart > 0.01, "the BaR did not weld from the stocked cargo");
                    ctx.Check(_queuedMax == 0, "queued " + _queuedMax + " SteelPlate although the cargo holds plenty (BaR missing max " + _missingMax + ")");
                    ctx.Check(_zeroAmountItems == 0, _zeroAmountItems + " queue entries with amount 0");
                    break;
            }
        }

        private static string Head(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var line = s.Split('\n')[0];
            return line.Length > 40 ? line.Substring(0, 40) : line;
        }
    }
}

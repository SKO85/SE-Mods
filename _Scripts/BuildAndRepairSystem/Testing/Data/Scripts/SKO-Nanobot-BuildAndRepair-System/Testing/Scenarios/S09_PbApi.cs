using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.Scripting.MemorySafeTypes;
using VRageMath;
using IngameSlim = VRage.Game.ModAPI.Ingame.IMySlimBlock;
using MyProductionItem = Sandbox.ModAPI.Ingame.MyProductionItem;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// Every terminal property the companion script uses. Kept equal to the ids in
    /// SKO-Nanobot-BuildAndRepair-System-Script/Script.cs by CompanionScriptContractTests.
    /// </summary>
    internal static class CompanionScriptContract
    {
        public static readonly string[] Properties =
        {
            "BuildAndRepair.AllowBuild", "BuildAndRepair.AreaDepth", "BuildAndRepair.AreaHeight",
            "BuildAndRepair.AreaOffsetFrontBack", "BuildAndRepair.AreaOffsetLeftRight", "BuildAndRepair.AreaOffsetUpDown",
            "BuildAndRepair.AreaWidth", "BuildAndRepair.CollectIfIdle", "BuildAndRepair.ComponentClassList",
            "BuildAndRepair.CurrentGrindTarget", "BuildAndRepair.CurrentPickedGrindTarget", "BuildAndRepair.CurrentPickedTarget",
            "BuildAndRepair.CurrentTarget", "BuildAndRepair.GetCollectEnabled", "BuildAndRepair.GetCollectPriority",
            "BuildAndRepair.GetGrindEnabled", "BuildAndRepair.GetGrindPriority", "BuildAndRepair.GetWeldEnabled",
            "BuildAndRepair.GetWeldPriority", "BuildAndRepair.GrindColor", "BuildAndRepair.GrindJanitorEnemies",
            "BuildAndRepair.GrindJanitorNeutrals", "BuildAndRepair.GrindJanitorNotOwned", "BuildAndRepair.GrindJanitorOptionDisableOnly",
            "BuildAndRepair.GrindJanitorOptionHackOnly", "BuildAndRepair.GrindPriorityList", "BuildAndRepair.IgnoreColor",
            "BuildAndRepair.MissingComponents", "BuildAndRepair.Mode", "BuildAndRepair.PossibleCollectTargets",
            "BuildAndRepair.PossibleGrindTargets", "BuildAndRepair.PossibleTargets", "BuildAndRepair.ProductionBlock.EnsureQueued",
            "BuildAndRepair.PushIngotOreImmediately", "BuildAndRepair.ScriptControlled", "BuildAndRepair.SetCollectEnabled",
            "BuildAndRepair.SetCollectPriority", "BuildAndRepair.SetGrindEnabled", "BuildAndRepair.SetGrindPriority",
            "BuildAndRepair.SetWeldEnabled", "BuildAndRepair.SetWeldPriority", "BuildAndRepair.UseGrindColor",
            "BuildAndRepair.UseIgnoreColor", "BuildAndRepair.WeldMode", "BuildAndRepair.WeldPriorityList",
            "BuildAndRepair.WorkMode"
        };

        public static readonly MyDefinitionId SteelPlate = new MyDefinitionId(typeof(MyObjectBuilder_Component), "SteelPlate");

        /// <summary>SteelPlate amount in an assembler queue.</summary>
        public static double QueuedSteel(Sandbox.ModAPI.Ingame.IMyAssembler assembler, List<MyProductionItem> scratch)
        {
            if (assembler == null) return 0;
            scratch.Clear();
            assembler.GetQueue(scratch);
            double amount = 0;
            for (int i = 0; i < scratch.Count; i++)
                if (scratch[i].BlueprintId.SubtypeName == "SteelPlate") amount += (double)scratch[i].Amount;
            return amount;
        }
    }

    /// <summary>
    /// SCN-API-001: the PB scripting API on a real BaR. Every property the companion script uses is registered;
    /// MissingComponents / PossibleTargets / Mode / WorkMode read back what the BaR does; ScriptControlled
    /// round-trips; BuildAndRepair.ProductionBlock.EnsureQueued queues SteelPlate in a real assembler.
    /// </summary>
    internal sealed class S09_PbApi : TestScenarioBase
    {
        private const int EnsureAmount = 10;
        private TestRig _rig;
        private readonly List<MyProductionItem> _queue = new List<MyProductionItem>();
        private readonly List<string> _missingProps = new List<string>();
        private bool _propsChecked;
        private bool _done;
        private int _missingSteel = -1;
        private int _possibleTargets = -1;
        private long _mode = -1;
        private long _workMode = -1;
        private string _scriptControlled = "";
        private int _ensureResult = int.MinValue;
        private double _queuedAfterEnsure = -1;
        private string _error;

        public override string Id { get { return "S09"; } }
        public override string Description { get { return "PB API: all companion-script properties exist, read back the BaR state; EnsureQueued fills an assembler"; } }
        public override int TimeoutSeconds { get { return 60; } }

        public override string SkipReason(TestContext ctx)
        {
            // EnsureQueued and the picked-target setters are published only when script control is allowed (world load).
            return Mod.Settings.Welder.ScriptControllFixed ? "script control is locked (ScriptControllFixed)" : null;
        }

        public override void Setup(TestContext ctx)
        {
            _missingProps.Clear();
            _propsChecked = _done = false;
            _missingSteel = _possibleTargets = -1;
            _mode = _workMode = -1;
            _scriptControlled = "";
            _ensureResult = int.MinValue;
            _queuedAfterEnsure = -1;
            _error = null;
            ctx.SetModSettings(s => { s.Welder.WorkSpeed = 10; });
            var spec = new RigSpec { Name = "S09", Bars = 1, DamagedArmor = 3 }; // no stock -> SteelPlate missing
            spec.Extra.Add(new RigBlock { Kind = RigBlockKind.Assembler, Position = new Vector3I(2, 0, 0), Name = "S09 Assembler", Tag = "assembler" });
            _rig = ctx.SpawnRig(spec);
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.Grids;
                s.WorkMode = WorkModes.WeldBeforeGrind;
                s.WeldOptions = AutoWeldOptions.WeldFull;
            });
        }

        private Sandbox.ModAPI.Ingame.IMyAssembler Assembler()
        {
            var t = _rig.Tag("assembler");
            return t.Count > 0 && t[0] != null ? t[0].FatBlock as Sandbox.ModAPI.Ingame.IMyAssembler : null;
        }

        public override void Sample(TestContext ctx)
        {
            if (_done || _rig.Bars.Count == 0) return;
            var welder = _rig.Bars[0].Welder;

            if (!_propsChecked)
            {
                _propsChecked = true;
                foreach (var id in CompanionScriptContract.Properties)
                    if (welder.GetProperty(id) == null) _missingProps.Add(id);
            }

            try
            {
                var missing = welder.GetValue<MemorySafeDictionary<MyDefinitionId, int>>("BuildAndRepair.MissingComponents");
                int steel;
                if (missing == null || !missing.TryGetValue(CompanionScriptContract.SteelPlate, out steel) || steel <= 0) return;
                _missingSteel = steel;

                var targets = welder.GetValue<MemorySafeList<IngameSlim>>("BuildAndRepair.PossibleTargets");
                _possibleTargets = targets != null ? targets.Count : -1;
                _mode = welder.GetValue<long>("BuildAndRepair.Mode");
                _workMode = welder.GetValue<long>("BuildAndRepair.WorkMode");

                try
                {
                    welder.SetValueBool("BuildAndRepair.ScriptControlled", true);
                    var on = welder.GetValueBool("BuildAndRepair.ScriptControlled");
                    welder.SetValueBool("BuildAndRepair.ScriptControlled", false);
                    var off = welder.GetValueBool("BuildAndRepair.ScriptControlled");
                    _scriptControlled = on && !off ? "ok" : "on=" + on + ",off=" + off;
                }
                catch (Exception e) { _scriptControlled = "set refused: " + e.Message; }

                var ensure = welder.GetValue<Func<IEnumerable<long>, MyDefinitionId, int, int>>("BuildAndRepair.ProductionBlock.EnsureQueued");
                var assembler = Assembler();
                if (ensure != null && assembler != null)
                {
                    assembler.ClearQueue();
                    _ensureResult = ensure(new[] { assembler.EntityId }, CompanionScriptContract.SteelPlate, EnsureAmount);
                    _queuedAfterEnsure = CompanionScriptContract.QueuedSteel(assembler, _queue);
                }
            }
            catch (Exception e) { _error = e.GetType().Name + ": " + e.Message; }
            _done = true;
        }

        public override bool IsComplete(TestContext ctx)
        {
            return _done || ctx.RunSeconds >= 40;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            ctx.Metric("properties", (long)CompanionScriptContract.Properties.Length);
            ctx.Metric("missingProperties", _missingProps.Count == 0 ? "none" : string.Join(",", _missingProps));
            ctx.Metric("missingSteel", (long)_missingSteel);
            ctx.Metric("possibleTargets", (long)_possibleTargets);
            ctx.Metric("mode", _mode);
            ctx.Metric("workMode", _workMode);
            ctx.Metric("scriptControlled", _scriptControlled.Replace(';', ','));
            ctx.Metric("ensureResult", (long)_ensureResult);
            ctx.Metric("queuedAfterEnsure", _queuedAfterEnsure);

            ctx.Check(_error == null, "PB API call threw: " + _error);
            ctx.Check(_missingProps.Count == 0, "properties not registered: " + string.Join(", ", _missingProps));
            ctx.Check(_missingSteel > 0, "MissingComponents never listed SteelPlate (empty cargo)");
            ctx.Check(_possibleTargets > 0, "PossibleTargets empty while 3 blocks are damaged");
            ctx.Check(_mode == (long)SearchModes.Grids && _workMode == (long)WorkModes.WeldBeforeGrind,
                "Mode/WorkMode read " + _mode + "/" + _workMode + ", configured Grids/WeldBeforeGrind");
            ctx.Check(_scriptControlled == "ok", "ScriptControlled round-trip: " + _scriptControlled);
            ctx.Check(_queuedAfterEnsure >= EnsureAmount, "EnsureQueued(" + EnsureAmount + " SteelPlate) left " + _queuedAfterEnsure + " in the assembler queue (result " + _ensureResult + ")");
        }
    }
}

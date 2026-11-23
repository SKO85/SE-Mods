using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Handlers;
using SKONanobotBuildAndRepairSystem.Models;
using System.Collections.Generic;
using VRage;
using VRage.Game;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    internal enum CollectRuleMode { AreaAndClass, CollectIfIdle }

    /// <summary>
    /// SCN-COLLECT (coverage follow-up). S131 AreaAndClass: one BaR (BoundingBox, area 60 m, ore class switched off)
    /// with 3 SteelPlate stacks inside the area, 2 SteelPlate stacks 70 m out and 3 iron ore stacks inside: only the 3
    /// plate stacks inside are collected, nothing lost. S132 CollectIfIdle: "collect only when idle" on, 6 damaged armor
    /// blocks to weld and 3 plate stacks inside: no stack collected while welding, all collected after.
    /// </summary>
    internal sealed class S131_CollectRules : TestScenarioBase
    {
        private const double Window = 60.0;
        private const int Amount = 50;
        private readonly string _id;
        private readonly CollectRuleMode _mode;
        private TestRig _rig;
        private readonly List<IMyEntity> _inside = new List<IMyEntity>();
        private readonly List<IMyEntity> _outside = new List<IMyEntity>();
        private readonly List<IMyEntity> _ore = new List<IMyEntity>();
        private double _plateStart = -1, _weldDoneAt = -1, _firstCollectAt = -1, _doneAt = -1;

        private static readonly MyDefinitionId PlateId = new MyDefinitionId(typeof(MyObjectBuilder_Component), "SteelPlate");

        public S131_CollectRules(string id, CollectRuleMode mode)
        {
            _id = id;
            _mode = mode;
        }

        public override string Id { get { return _id; } }
        public override string Description
        {
            get
            {
                return _mode == CollectRuleMode.AreaAndClass
                    ? "Collect: only stacks inside the area and of enabled classes (ore off, 70 m out ignored)"
                    : "Collect only when idle: nothing collected while welding, everything after";
            }
        }
        public override int TimeoutSeconds { get { return 90; } }

        public override void Setup(TestContext ctx)
        {
            _inside.Clear();
            _outside.Clear();
            _ore.Clear();
            _plateStart = _weldDoneAt = _firstCollectAt = _doneAt = -1;
            ctx.SetModSettings(s => { s.Welder.WorkSpeed = _mode == CollectRuleMode.CollectIfIdle ? 2 : 10; });
            _rig = ctx.SpawnRig(new RigSpec { Name = _id, Bars = 1, DamagedArmor = _mode == CollectRuleMode.CollectIfIdle ? 6 : 0 });
        }

        protected override void Configure(TestContext ctx)
        {
            if (_mode == CollectRuleMode.CollectIfIdle) ctx.StockCargo(_rig, "SteelPlate", 500);
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.BoundingBox;
                s.WorkMode = WorkModes.WeldBeforeGrind;
                s.AreaSize = new Vector3(60f, 60f, 60f);
                if (_mode == CollectRuleMode.CollectIfIdle) s.Flags |= SyncBlockSettings.Settings.ComponentCollectIfIdle;
                else s.Flags &= ~SyncBlockSettings.Settings.ComponentCollectIfIdle;
            });
            if (_mode == CollectRuleMode.AreaAndClass && _rig.Bars.Count > 0)
            {
                // Same path as the terminal / PB: writes the priority list into the synced settings.
                _rig.Bars[0].ScriptSetCollectEnabled((int)ComponentClass.Ore, false);
                _rig.Bars[0].SettingsChanged();
            }

            var grid = _rig.Grid;
            var center = grid.GridIntegerToWorld(_rig.BarPositions[0]);
            var up = grid.WorldMatrix.Up;
            var right = grid.WorldMatrix.Right;
            for (int i = 0; i < 3; i++)
                _inside.Add(Spawn(center + up * (6 + i) + right * (3 * (i - 1)), new MyObjectBuilder_Component { SubtypeName = "SteelPlate" }));
            if (_mode == CollectRuleMode.AreaAndClass)
            {
                for (int i = 0; i < 2; i++)
                    _outside.Add(Spawn(center + up * 8 + right * (70 + 4 * i), new MyObjectBuilder_Component { SubtypeName = "SteelPlate" }));
                for (int i = 0; i < 3; i++)
                    _ore.Add(Spawn(center + up * (10 + i) - right * (3 * (i - 1)), new MyObjectBuilder_Ore { SubtypeName = "Iron" }));
            }
        }

        private static IMyEntity Spawn(Vector3D pos, MyObjectBuilder_PhysicalObject content)
        {
            var ob = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_FloatingObject>();
            ob.Item = new MyObjectBuilder_InventoryItem { Amount = (MyFixedPoint)Amount, PhysicalContent = content };
            ob.PersistentFlags = MyPersistentEntityFlags2.InScene;
            ob.PositionAndOrientation = new MyPositionAndOrientation(pos, Vector3.Forward, Vector3.Up);
            return MyAPIGateway.Entities.CreateFromObjectBuilderAndAdd(ob);
        }

        private static int Alive(List<IMyEntity> list)
        {
            var n = 0;
            for (int i = 0; i < list.Count; i++) if (list[i] != null && !list[i].Closed && !list[i].MarkedForClose) n++;
            return n;
        }

        public override void Sample(TestContext ctx)
        {
            var now = ctx.RunSeconds;
            if (_mode == CollectRuleMode.CollectIfIdle)
            {
                if (_weldDoneAt < 0 && ctx.CountFullIntegrity(_rig.Targets) == _rig.Targets.Count) _weldDoneAt = now;
                if (_firstCollectAt < 0 && Alive(_inside) < _inside.Count) _firstCollectAt = now;
            }
            if (_doneAt < 0 && Alive(_inside) == 0 && (_mode == CollectRuleMode.AreaAndClass || _weldDoneAt >= 0)) _doneAt = now;
        }

        public override bool IsComplete(TestContext ctx)
        {
            // AreaAndClass: give the BaR time to (wrongly) go for the outside / ore stacks after the plates.
            return (_doneAt >= 0 && ctx.RunSeconds >= _doneAt + (_mode == CollectRuleMode.AreaAndClass ? 10.0 : 1.0)) || ctx.RunSeconds >= Window;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            ctx.Metric("insideLeft", (long)Alive(_inside));
            ctx.Metric("outsideLeft", (long)Alive(_outside));
            ctx.Metric("oreLeft", (long)Alive(_ore));
            ctx.Metric("doneS", _doneAt);
            ctx.Check(_inside.Count == 3, "layout: " + _inside.Count + "/3 plate stacks spawned");
            ctx.Check(Alive(_inside) == 0, Alive(_inside) + " plate stack(s) inside the area not collected");
            if (_mode == CollectRuleMode.AreaAndClass)
            {
                ctx.Check(Alive(_outside) == _outside.Count, (_outside.Count - Alive(_outside)) + " stack(s) 70 m outside the area were collected");
                ctx.Check(Alive(_ore) == _ore.Count, (_ore.Count - Alive(_ore)) + " ore stack(s) collected although the ore class is off");
            }
            else
            {
                ctx.Metric("weldDoneS", _weldDoneAt);
                ctx.Metric("firstCollectS", _firstCollectAt);
                ctx.Check(_weldDoneAt >= 0, "welding not finished in " + Window + " s");
                ctx.Check(_firstCollectAt < 0 || _weldDoneAt < 0 || _firstCollectAt >= _weldDoneAt - 0.5,
                    "collected at " + _firstCollectAt.ToString("F1") + " s while still welding (done at " + _weldDoneAt.ToString("F1") + " s)");
            }
        }
    }
}

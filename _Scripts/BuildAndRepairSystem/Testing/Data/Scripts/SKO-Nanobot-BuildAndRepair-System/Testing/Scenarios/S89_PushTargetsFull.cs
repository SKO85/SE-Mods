using SKONanobotBuildAndRepairSystem.Utils;
using VRage;
using VRage.Game;
using VRageMath;
using IMyInventory = VRage.Game.ModAPI.IMyInventory;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// SCN-INV-001 / SCN-INV-002: push targets full, then freed. Rig: BaR (GrindOnly, janitor NoOwnership, push
    /// ingot/ore on), all rig cargo filled with stone. Derelict 25 m away: unowned container full of iron ore.
    /// Phase 1 (30 s): the BaR fills up, cannot push, pauses (InventoryFull) without losing ore. Then two rig containers
    /// are emptied: the BaR must resume, push the ore and finish the derelict. Iron ore is conserved throughout.
    /// </summary>
    internal sealed class S89_PushTargetsFull : TestScenarioBase
    {
        private const double FreeAt = 30.0;
        private const double Window = 180.0;
        private static readonly MyItemType Iron = MyItemType.MakeOre("Iron");
        private static readonly MyItemType Stone = MyItemType.MakeOre("Stone");

        private TestRig _rig;
        // Three single-container derelicts: no armor next to them, so grinding never splits a container off.
        private readonly System.Collections.Generic.List<TestRig> _derelicts = new System.Collections.Generic.List<TestRig>();
        private bool _filled, _freed, _sawFullBeforeFree;
        private double _initialIron = -1, _minIronSeen = double.MaxValue, _doneAt = -1;
        private double _lootMaxVolume, _ironAdded, _stoneAdded, _barMaxVolume;
        private double _ironLoot, _ironRig, _ironBar, _ironTransport;
        private double _lossAt = -1;
        private string _lossBreakdown = "";
        private readonly System.Collections.Generic.HashSet<VRage.ModAPI.IMyEntity> _entities = new System.Collections.Generic.HashSet<VRage.ModAPI.IMyEntity>();

        /// <summary>Iron ore in floating objects within 150 m of the origin.</summary>
        private double FloatingIron(TestContext ctx)
        {
            _entities.Clear();
            Sandbox.ModAPI.MyAPIGateway.Entities.GetEntities(_entities, e => e is Sandbox.Game.Entities.MyFloatingObject);
            double total = 0;
            foreach (var e in _entities)
            {
                var f = e as Sandbox.Game.Entities.MyFloatingObject;
                if (f == null || Vector3D.Distance(f.PositionComp.GetPosition(), ctx.Origin) > 150) continue;
                var id = f.Item.Content.GetId();
                if (id.SubtypeName == "Iron" && id.TypeId == typeof(MyObjectBuilder_Ore)) total += (double)f.Item.Amount;
            }
            _entities.Clear();
            return total;
        }

        private string Breakdown(TestContext ctx)
        {
            return string.Format("loot {0:F0}, rig {1:F0}, bar {2:F0}, transport {3:F0}, floating {4:F0}", _ironLoot, _ironRig, _ironBar, _ironTransport, FloatingIron(ctx));
        }

        public override string Id { get { return "S89"; } }
        public override string Description { get { return "Push targets full: BaR pauses without losing ore, resumes once space is freed"; } }
        public override int TimeoutSeconds { get { return 210; } }

        public override void Setup(TestContext ctx)
        {
            _filled = _freed = _sawFullBeforeFree = false;
            _initialIron = -1;
            _minIronSeen = double.MaxValue;
            _doneAt = -1;
            ctx.SetModSettings(s =>
            {
                s.Welder.WorkSpeed = 10;
                s.Welder.AllowedGrindJanitorRelations |= AutoGrindRelation.NoOwnership;
            });
            _rig = ctx.SpawnRig(new RigSpec { Name = "S89", Bars = 1 });
            _derelicts.Clear();
            for (int i = 0; i < 3; i++)
            {
                var derelict = new RigSpec { Name = "S89-derelict" + i, Bars = 0, OwnerId = 0, Offset = new Vector3D(i * 5.0, 0, -25) };
                derelict.Extra.Add(new RigBlock { Kind = RigBlockKind.Cargo, Position = Vector3I.Zero, Tag = "loot" });
                _derelicts.Add(ctx.SpawnRig(derelict));
            }
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.BoundingBox;
                s.WorkMode = WorkModes.GrindOnly;
                s.UseGrindJanitorOn = AutoGrindRelation.NoOwnership;
                s.GrindJanitorOptions = 0;
                s.Flags |= Models.SyncBlockSettings.Settings.PushIngotOreImmediately;
            });
        }

        private readonly System.Collections.Generic.List<IMyInventory> _loot = new System.Collections.Generic.List<IMyInventory>();

        /// <summary>Inventories of the loot containers still standing.</summary>
        private System.Collections.Generic.List<IMyInventory> Loot()
        {
            _loot.Clear();
            for (int d = 0; d < _derelicts.Count; d++)
            {
                var t = _derelicts[d].Tag("loot");
                for (int i = 0; i < t.Count; i++)
                {
                    var block = t[i] != null ? t[i].FatBlock : null;
                    if (block != null && !block.Closed && !block.MarkedForClose) _loot.Add(block.GetInventory(0));
                }
            }
            return _loot;
        }

        public override bool IsReady(TestContext ctx)
        {
            if (!base.IsReady(ctx)) return false;
            if (_filled) return true;
            var loot = Loot();
            if (loot.Count < 3 || _rig.Cargos.Count == 0) return false;
            _lootMaxVolume = 0;
            _ironAdded = 0;
            for (int i = 0; i < loot.Count; i++)
            {
                _lootMaxVolume += (double)loot[i].MaxVolume;
                _ironAdded += Fill(loot[i], Iron, "Iron");
            }
            _stoneAdded = 0;
            for (int pass = 0; pass < 3; pass++)
                for (int i = 0; i < _rig.Cargos.Count; i++) _stoneAdded += Fill(_rig.Cargos[i].GetInventory(0), Stone, "Stone");
            var bar0 = _rig.Bars.Count > 0 ? _rig.Bars[0] : null;
            var barInv = bar0 != null && bar0.Welder != null ? bar0.Welder.GetInventory(0) : null;
            _barMaxVolume = barInv != null ? (double)barInv.MaxVolume : -1;
            _initialIron = IronTotal();
            _filled = true;
            return true;
        }

        /// <summary>
        /// Fills an inventory (nearly) to the brim with one ore: amount from free volume / item volume (CanItemsBeAdded
        /// refused every amount here). Returns the amount actually added.
        /// </summary>
        private static double Fill(IMyInventory inv, MyItemType type, string subtype)
        {
            if (inv == null) return -1;
            var def = Sandbox.Definitions.MyDefinitionManager.Static.GetPhysicalItemDefinition(new MyDefinitionId(typeof(MyObjectBuilder_Ore), subtype));
            if (def == null || def.Volume <= 0) return -2;
            var free = (double)inv.MaxVolume - (double)inv.CurrentVolume;
            var amount = System.Math.Floor(free / def.Volume * 0.98);
            if (amount <= 0) return 0;
            var before = (double)inv.GetItemAmount(type);
            inv.AddItems((MyFixedPoint)amount, new MyObjectBuilder_Ore { SubtypeName = subtype });
            return (double)inv.GetItemAmount(type) - before;
        }

        /// <summary>Iron ore in the derelict container, the rig cargo, the BaR and its transport.</summary>
        private double IronTotal()
        {
            _ironLoot = _ironRig = _ironBar = _ironTransport = 0;
            var loot = Loot();
            for (int i = 0; i < loot.Count; i++) _ironLoot += (double)loot[i].GetItemAmount(Iron);
            for (int i = 0; i < _rig.Cargos.Count; i++)
            {
                var inv = _rig.Cargos[i].GetInventory(0);
                if (inv != null) _ironRig += (double)inv.GetItemAmount(Iron);
            }
            for (int b = 0; b < _rig.Bars.Count; b++)
            {
                var bar = _rig.Bars[b];
                var inv = bar.Welder != null ? bar.Welder.GetInventory(0) : null;
                if (inv != null) _ironBar += (double)inv.GetItemAmount(Iron);
                if (bar._TransportInventory != null) _ironTransport += (double)bar._TransportInventory.GetItemAmount(Iron);
            }
            return _ironLoot + _ironRig + _ironBar + _ironTransport;
        }

        private bool DerelictGone(TestContext ctx)
        {
            for (int d = 0; d < _derelicts.Count; d++)
                if (!ScenarioUtil.AllGone(ctx, _derelicts[d], _derelicts[d].Tag("loot"))) return false;
            return true;
        }

        // FreeAt / Window: three containers take longer than one.

        public override void Sample(TestContext ctx)
        {
            if ((TestContext.Frame % 30) != 0) return;
            var now = ctx.RunSeconds;
            var bar = _rig.Bars.Count > 0 ? _rig.Bars[0] : null;
            if (!_freed && bar != null && bar.State.InventoryFull) _sawFullBeforeFree = true;
            if (!_freed && now >= FreeAt)
            {
                // A player frees space: empty two rig containers.
                for (int i = 0; i < _rig.Cargos.Count && i < 2; i++) ctx.ClearInventory(_rig.Cargos[i]);
                _freed = true;
            }
            var iron = IronTotal();
            if (iron < _minIronSeen) _minIronSeen = iron;
            if (_lossAt < 0 && iron < _initialIron - 1)
            {
                _lossAt = now;
                _lossBreakdown = Breakdown(ctx);
            }
            if (_doneAt < 0 && _freed && DerelictGone(ctx)) _doneAt = now;
        }

        public override bool IsComplete(TestContext ctx)
        {
            return _doneAt >= 0 || ctx.RunSeconds >= Window;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var gone = DerelictGone(ctx);
            var iron = IronTotal();
            ctx.Metric("barMaxVolume", _barMaxVolume);
            var settings = Sandbox.ModAPI.MyAPIGateway.Session.SessionSettings;
            ctx.Metric("worldMaxFloatingObjects", settings != null ? (long)settings.MaxFloatingObjects : -1L);
            ctx.Metric("ironAtEnd", Breakdown(ctx).Replace(',', ' '));
            ctx.Metric("lossAtS", _lossAt);
            ctx.Metric("ironAtLoss", _lossBreakdown.Replace(',', ' '));
            ctx.Metric("initialIron", _initialIron);
            ctx.Metric("lootMaxVolume", _lootMaxVolume);
            ctx.Metric("ironAdded", _ironAdded);
            ctx.Metric("stoneAdded", _stoneAdded);
            var rigFull = 0;
            for (int i = 0; i < _rig.Cargos.Count; i++)
            {
                var inv = _rig.Cargos[i].GetInventory(0);
                if (inv != null && (double)inv.CurrentVolume >= (double)inv.MaxVolume * 0.99) rigFull++;
            }
            ctx.Metric("rigCargoFullAtEnd", (long)rigFull);
            ctx.Metric("ironEnd", iron);
            ctx.Metric("ironMin", _minIronSeen);
            ctx.Metric("sawInventoryFull", _sawFullBeforeFree);
            ctx.Metric("doneS", _doneAt);
            ctx.Check(_initialIron > 0, "layout: derelict container holds no iron ore");
            ctx.Check(_sawFullBeforeFree, "the BaR never reported a full inventory while all storage was full");
            ctx.Check(gone, "the derelict container was not ground within " + Window + " s after space was freed at " + FreeAt + " s");
            ctx.Check(_minIronSeen >= _initialIron - 1, "iron ore lost while grinding: lowest total " + _minIronSeen + " of " + _initialIron);
            ctx.Check(iron >= _initialIron - 1, "iron ore lost: " + iron + " of " + _initialIron + " left at the end");
        }
    }
}

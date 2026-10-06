using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Handlers;
using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Profiling;
using System;
using System.Collections.Generic;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing
{
    /// <summary>What a scenario asks the harness to build.</summary>
    internal sealed class RigSpec
    {
        public string Name = "rig";
        /// <summary>Large BaR blocks on the rig (0 = target-only grid).</summary>
        public int Bars = 1;
        /// <summary>Armor blocks spawned at DamagedBuildPercent (weld targets).</summary>
        public int DamagedArmor;
        /// <summary>Armor blocks spawned at full integrity (grind targets / filler).</summary>
        public int Armor;
        public float DamagedBuildPercent = 0.2f;
        /// <summary>World offset from the scenario origin.</summary>
        public Vector3D Offset = Vector3D.Zero;
        /// <summary>Block owner; -1 = the admin running the test, 0 = nobody.</summary>
        public long OwnerId = -1;
    }

    /// <summary>A spawned rig with its resolved BaRs and target blocks.</summary>
    internal sealed class TestRig
    {
        public string Name;
        public IMyCubeGrid Grid;
        public readonly List<Vector3I> BarPositions = new List<Vector3I>();
        public readonly List<Vector3I> CargoPositions = new List<Vector3I>();
        public readonly List<Vector3I> TargetPositions = new List<Vector3I>();
        public readonly List<NanobotSystem> Bars = new List<NanobotSystem>();
        public readonly List<IMySlimBlock> Targets = new List<IMySlimBlock>();
        public IMyCubeBlock Cargo;
        public bool Resolved;
    }

    /// <summary>
    /// Per-scenario context: spawn/stock/configure primitives, observation helpers,
    /// metric + check recording, and teardown of everything it spawned.
    /// </summary>
    internal sealed class TestContext
    {
        public readonly TestResult Result;
        public readonly ulong SenderSteamId;
        public readonly long OwnerId;
        public readonly Vector3D Origin;
        public readonly IList<string> PerfMethods;
        public readonly List<TestRig> Rigs = new List<TestRig>();

        /// <summary>Play time after Setup/Configure; BaRs must have scanned after this to count as ready.</summary>
        public TimeSpan SetupTime;
        public TimeSpan RunStartTime;

        // Run-phase samples (the debug HUD resets Mod's peak counters every 2 s, so we track our own).
        public double SimMin = double.MaxValue;
        public double TickPeakMs;
        public int WeldBudgetPeak;
        public int GrindBudgetPeak;
        public int AssignmentsPeak;
        private double _simSum;
        private int _simSamples;
        private double _tickAvgSum;
        private int _tickAvgSamples;

        private readonly List<IMyEntity> _spawned = new List<IMyEntity>();

        public TestContext(TestResult result, ulong senderSteamId, long ownerId, Vector3D origin, IList<string> perfMethods)
        {
            Result = result;
            SenderSteamId = senderSteamId;
            OwnerId = ownerId;
            Origin = origin;
            PerfMethods = perfMethods;
        }

        public static TimeSpan Now { get { return MyAPIGateway.Session.ElapsedPlayTime; } }
        public static int Frame { get { return MyAPIGateway.Session.GameplayFrameCounter; } }
        public double SimAvg { get { return _simSamples > 0 ? _simSum / _simSamples : 0.0; } }
        public double TickAvgMs { get { return _tickAvgSamples > 0 ? _tickAvgSum / _tickAvgSamples : 0.0; } }
        public double RunSeconds { get { return (Now - RunStartTime).TotalSeconds; } }

        // ---------------------------------------------------------------- spawning

        public TestRig SpawnRig(RigSpec spec)
        {
            var rig = new TestRig { Name = spec.Name };
            var owner = spec.OwnerId < 0 ? OwnerId : spec.OwnerId;
            var ob = RigBuilder.Build(spec, Origin + spec.Offset, owner, rig);
            MyAPIGateway.Entities.RemapObjectBuilder(ob);
            var entity = MyAPIGateway.Entities.CreateFromObjectBuilderAndAdd(ob);
            rig.Grid = entity as IMyCubeGrid;
            if (rig.Grid == null) throw new Exception("SpawnRig(" + spec.Name + "): grid did not spawn");
            _spawned.Add(entity);
            Rigs.Add(rig);
            return rig;
        }

        /// <summary>Resolves BaR game logic + target blocks for every rig. False while anything is still missing.</summary>
        public bool TryResolveRigs()
        {
            var all = true;
            for (int i = 0; i < Rigs.Count; i++)
            {
                var rig = Rigs[i];
                if (rig.Resolved) continue;
                if (rig.Grid == null || rig.Grid.Closed) { all = false; continue; }

                rig.Bars.Clear();
                var barsOk = true;
                for (int b = 0; b < rig.BarPositions.Count; b++)
                {
                    var slim = rig.Grid.GetCubeBlock(rig.BarPositions[b]);
                    NanobotSystem sys = null;
                    if (slim != null && slim.FatBlock != null) Mod.NanobotSystems.TryGetValue(slim.FatBlock.EntityId, out sys);
                    if (sys == null) { barsOk = false; break; }
                    rig.Bars.Add(sys);
                }
                if (!barsOk) { all = false; continue; }

                rig.Targets.Clear();
                for (int t = 0; t < rig.TargetPositions.Count; t++)
                {
                    var slim = rig.Grid.GetCubeBlock(rig.TargetPositions[t]);
                    if (slim != null) rig.Targets.Add(slim);
                }
                if (rig.CargoPositions.Count > 0)
                {
                    var cargo = rig.Grid.GetCubeBlock(rig.CargoPositions[0]);
                    rig.Cargo = cargo != null ? cargo.FatBlock : null;
                }
                rig.Resolved = true;
            }
            return all;
        }

        public void MarkSetupTime()
        {
            SetupTime = Now;
        }

        /// <summary>Every rig BaR registered, State.Ready, and scanned after SetupTime.</summary>
        public bool AllBarsReadyAndScanned()
        {
            for (int i = 0; i < Rigs.Count; i++)
            {
                var rig = Rigs[i];
                if (!rig.Resolved) return false;
                for (int b = 0; b < rig.Bars.Count; b++)
                {
                    var bar = rig.Bars[b];
                    if (bar.Welder == null || !bar.State.Ready) return false;
                    if (bar.LastTargetsUpdate <= SetupTime) return false;
                }
            }
            return true;
        }

        // ---------------------------------------------------------------- manipulation

        public void StockCargo(TestRig rig, string componentSubtype, int amount)
        {
            if (rig.Cargo == null) throw new Exception("StockCargo(" + rig.Name + "): no cargo on rig");
            var inv = rig.Cargo.GetInventory(0);
            if (inv == null) throw new Exception("StockCargo(" + rig.Name + "): cargo has no inventory");
            inv.AddItems((MyFixedPoint)amount, new MyObjectBuilder_Component { SubtypeName = componentSubtype });
        }

        public void ClearInventory(IMyCubeBlock block)
        {
            var inv = block != null ? block.GetInventory(0) : null;
            if (inv != null) inv.Clear();
        }

        public void ConfigureBar(NanobotSystem bar, Action<SyncBlockSettings> edit)
        {
            edit(bar.Settings);
            bar.SettingsChanged();
        }

        public void ConfigureBars(TestRig rig, Action<SyncBlockSettings> edit)
        {
            for (int b = 0; b < rig.Bars.Count; b++) ConfigureBar(rig.Bars[b], edit);
        }

        /// <summary>Mutates the live mod settings; TestRunner restores the snapshot in Teardown.</summary>
        public void SetModSettings(Action<SyncModSettings> edit)
        {
            edit(Mod.Settings);
            Mod.SettingsChanged();
            try { NetworkMessagingHandler.BroadcastModSettings(); } catch { }
        }

        // ---------------------------------------------------------------- observation

        public int CountFullIntegrity(List<IMySlimBlock> blocks)
        {
            var n = 0;
            for (int i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
                if (b != null && !b.IsDestroyed && b.IsFullIntegrity) n++;
            }
            return n;
        }

        public bool TryGetMethodStats(string methodName, out MethodProfiler.MethodStatsSnapshot snapshot)
        {
            return MethodProfiler.TryGetMethodStats(methodName, out snapshot);
        }

        /// <summary>BaRs in the world that are not part of any rig (perf caveat).</summary>
        public int OtherBarCount()
        {
            var rigBars = 0;
            for (int i = 0; i < Rigs.Count; i++) rigBars += Rigs[i].Bars.Count;
            return Math.Max(0, Mod.NanobotSystems.Count - rigBars);
        }

        /// <summary>Called by TestRunner every tick during Run.</summary>
        public void SampleTick()
        {
            var sim = MyAPIGateway.Physics != null ? MyAPIGateway.Physics.ServerSimulationRatio : 1.0f;
            if (sim < SimMin) SimMin = sim;
            _simSum += sim;
            _simSamples++;

            var peak = Mod.TickCostPeakMs;
            if (peak > TickPeakMs) TickPeakMs = peak;
            var avg = Mod.TickCostAvgMs;
            if (avg > 0.0) { _tickAvgSum += avg; _tickAvgSamples++; }

            var w = Mod.WeldBudgetPeakUsed; if (w > WeldBudgetPeak) WeldBudgetPeak = w;
            var g = Mod.GrindBudgetPeakUsed; if (g > GrindBudgetPeak) GrindBudgetPeak = g;
            var a = BlockSystemAssigningHandler.AssignmentCount; if (a > AssignmentsPeak) AssignmentsPeak = a;
        }

        // ---------------------------------------------------------------- recording

        public void Metric(string key, double value) { Result.Metric(key, value); }
        public void Metric(string key, long value) { Result.Metric(key, value); }
        public void Metric(string key, bool value) { Result.Metric(key, value); }

        public void Check(bool ok, string failure)
        {
            if (!ok) Result.Fail(failure);
        }

        public void CheckMax(string key, double value, double max)
        {
            Result.Metric(key, value);
            if (value > max) Result.Fail(key + "=" + value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + ">" + max.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        public void CheckMin(string key, double value, double min)
        {
            Result.Metric(key, value);
            if (value < min) Result.Fail(key + "=" + value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "<" + min.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>Records calls/steadyAvgMs/maxMs for a profiled method (no check).</summary>
        public void RecordMethodStats(string method)
        {
            MethodProfiler.MethodStatsSnapshot s;
            if (!TryGetMethodStats(method, out s))
            {
                Result.Metric(method + ".calls", 0L);
                return;
            }
            Result.Metric(method + ".calls", s.Calls);
            Result.Metric(method + ".steadyAvgMs", s.SteadyCalls > 0 ? s.SteadyAvgMs : s.AvgMs);
            Result.Metric(method + ".maxMs", s.MaxMs);
        }

        public void CheckMethodAvgMax(string method, double maxAvg)
        {
            MethodProfiler.MethodStatsSnapshot s;
            if (!TryGetMethodStats(method, out s) || s.Calls == 0) return;
            var avg = s.SteadyCalls > 0 ? s.SteadyAvgMs : s.AvgMs;
            if (avg > maxAvg) Result.Fail(method + ".steadyAvgMs=" + avg.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + ">" + maxAvg.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        public void CheckMethodMaxMax(string method, double maxMax)
        {
            MethodProfiler.MethodStatsSnapshot s;
            if (!TryGetMethodStats(method, out s) || s.Calls == 0) return;
            if (s.MaxMs > maxMax) Result.Fail(method + ".maxMs=" + s.MaxMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + ">" + maxMax.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        public void CheckMethodCalls(string method, long minCalls, long maxCalls)
        {
            MethodProfiler.MethodStatsSnapshot s;
            var calls = TryGetMethodStats(method, out s) ? s.Calls : 0L;
            if (calls < minCalls || calls > maxCalls) Result.Fail(method + ".calls=" + calls + " outside [" + minCalls + "," + maxCalls + "]");
        }

        // ---------------------------------------------------------------- teardown

        /// <summary>Closes every spawned entity, then sweeps floating objects out of the scenario area.</summary>
        public void CloseSpawned()
        {
            var box = BoundingBoxD.CreateInvalid();
            for (int i = 0; i < _spawned.Count; i++)
            {
                var e = _spawned[i];
                if (e == null) continue;
                try
                {
                    if (!e.Closed) box.Include(e.WorldAABB);
                    if (!e.Closed && !e.MarkedForClose) e.Close();
                }
                catch { }
            }
            _spawned.Clear();
            for (int i = 0; i < Rigs.Count; i++) { Rigs[i].Bars.Clear(); Rigs[i].Targets.Clear(); Rigs[i].Grid = null; }

            if (!box.Valid) return;
            box.Inflate(50.0);
            try
            {
                List<IMyEntity> entities;
                lock (MyAPIGateway.Entities)
                {
                    entities = MyAPIGateway.Entities.GetTopMostEntitiesInBox(ref box);
                }
                for (int i = 0; i < entities.Count; i++)
                {
                    var e = entities[i];
                    if (e is Sandbox.Game.Entities.MyFloatingObject && !e.Closed && !e.MarkedForClose) e.Close();
                }
            }
            catch { }
        }
    }
}

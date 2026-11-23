using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Handlers;
using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Profiling;
using SKONanobotBuildAndRepairSystem.Testing.Scenarios;
using SKONanobotBuildAndRepairSystem.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing
{
    /// <summary>
    /// Main-thread scenario state machine, ticked from Mod.UpdateBeforeSimulation (server branch).
    /// Idle → Setup → WaitReady → WarmUp → Run → Assert → Teardown → Report → next/Idle.
    /// Never blocks; exceptions anywhere fail the run and still go through Teardown.
    /// </summary>
    internal static class TestRunner
    {
        private enum Phase { Idle, Setup, WaitReady, WarmUp, Run, Assert, Teardown, Report }

        private struct QueueItem
        {
            public ITestScenario Scenario;
            public int Run;
            public int Runs;
        }

        private const int PollFrames = 30;
        private const int WarmUpFrames = 180;
        private const string LogPrefix = "NanobotTest.";

        private static Phase _phase = Phase.Idle;
        private static readonly Queue<QueueItem> _queue = new Queue<QueueItem>();
        private static readonly List<TestResult> _sessionResults = new List<TestResult>();

        private static QueueItem _current;
        private static TestContext _ctx;
        private static TestResult _result;
        private static int _phaseStartFrame;
        private static TimeSpan _phaseStartTime;
        private static ulong _steamId;
        private static long _ownerId;
        private static Vector3D _origin;
        private static string _session;
        private static TextWriter _log;
        private static string _settingsSnapshot;
        private static int _savedMinDuration = -1;
        private static bool _profilerStarted;
        private static bool _runStarting; // first Run frame: reset stats, switch the rig BaRs on
        private static int _sessionTotal; // queued runs this session, for the "n/total" start line
        private static readonly ITestScenario WarmUp = new S00_WarmUp();

        public static Vector3D? OriginOverride;

        // "test run ... detail": keep per-call profiler logs (MinDurationMs 0) for analysis runs.
        private static bool _detail;

        public static bool IsBusy { get { return _phase != Phase.Idle || _queue.Count > 0; } }

        // ---------------------------------------------------------------- command surface

        public static string Enqueue(IList<ITestScenario> scenarios, int repeat, ulong steamId, bool detail = false)
        {
            if (IsBusy) return null;
            _detail = detail;

            long ownerId;
            Vector3D origin;
            string error;
            if (!ResolveOriginAndOwner(steamId, out ownerId, out origin, out error)) return error;

            // Test grids left in the world (e.g. saved by an autosave mid-run) would be spawned into: remove them.
            var removed = RemoveLeftoverTestGrids();
            // Anything else in the spawn area would be hit by the rigs (physics explosion): refuse instead.
            var blockers = BlockersInSpawnArea(origin);
            if (blockers != null)
                return string.Format(CultureInfo.InvariantCulture,
                    "Spawn area not clear ({0:F0} m around {1:F0},{2:F0},{3:F0}): {4}. Move away, or set another origin with /nanobars test origin <x> <y> <z>.",
                    SpawnClearRadius, origin.X, origin.Y, origin.Z, blockers);

            _steamId = steamId;
            _ownerId = ownerId;
            _origin = origin;
            _session = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            _sessionResults.Clear();

            // Warm-up first, so the first real scenario does not measure the one-time JIT cost.
            _queue.Enqueue(new QueueItem { Scenario = WarmUp, Run = 1, Runs = 1 });
            for (int i = 0; i < scenarios.Count; i++)
                for (int r = 1; r <= repeat; r++)
                    _queue.Enqueue(new QueueItem { Scenario = scenarios[i], Run = r, Runs = repeat });
            _sessionTotal = _queue.Count;

            OpenLog();
            return string.Format(CultureInfo.InvariantCulture,
                "{6}Queued {0} scenario run(s), session {1}. Origin {2:F0},{3:F0},{4:F0}. Results in {5}{1}.log",
                _queue.Count, _session, origin.X, origin.Y, origin.Z, LogPrefix,
                removed > 0 ? "Removed " + removed + " leftover test grid(s). " : "");
        }

        // ---------------------------------------------------------------- spawn-area safety

        /// <summary>Every rig is named "NanobotTest-..." (RigBuilder); the load scenarios reach ~130 m from the origin.</summary>
        private const string TestGridPrefix = "NanobotTest-";
        private const double SpawnClearRadius = 150.0;
        private static readonly HashSet<IMyEntity> _entityScratch = new HashSet<IMyEntity>();
        private static readonly List<IMyCubeGrid> _groupScratch = new List<IMyCubeGrid>();

        private static bool IsTestGrid(IMyEntity entity)
        {
            var name = entity != null ? entity.DisplayName : null;
            return name != null && name.StartsWith(TestGridPrefix, StringComparison.Ordinal);
        }

        /// <summary>Closes leftover test grids and everything attached to them (rotor heads, docked test ships).</summary>
        private static int RemoveLeftoverTestGrids()
        {
            _entityScratch.Clear();
            MyAPIGateway.Entities.GetEntities(_entityScratch, e => e is IMyCubeGrid && IsTestGrid(e));
            var closed = new HashSet<long>();
            foreach (var entity in _entityScratch)
            {
                var grid = entity as IMyCubeGrid;
                if (grid == null || grid.Closed || grid.MarkedForClose) continue;
                _groupScratch.Clear();
                MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Logical, _groupScratch);
                if (_groupScratch.Count == 0) _groupScratch.Add(grid);
                foreach (var member in _groupScratch)
                {
                    if (member == null || member.Closed || member.MarkedForClose || !closed.Add(member.EntityId)) continue;
                    member.Close();
                }
            }
            _entityScratch.Clear();
            _groupScratch.Clear();
            return closed.Count;
        }

        /// <summary>Names of non-test grids within SpawnClearRadius of the origin (null when clear).</summary>
        private static string BlockersInSpawnArea(Vector3D origin)
        {
            var sphere = new BoundingSphereD(origin, SpawnClearRadius);
            var hits = MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref sphere);
            var names = new List<string>();
            var more = 0;
            foreach (var entity in hits)
            {
                var grid = entity as IMyCubeGrid;
                if (grid == null || grid.Closed || grid.MarkedForClose || IsTestGrid(grid)) continue;
                if (names.Count < 5) names.Add(string.IsNullOrEmpty(grid.DisplayName) ? grid.EntityId.ToString() : grid.DisplayName);
                else more++;
            }
            if (names.Count == 0) return null;
            return string.Join(", ", names) + (more > 0 ? " (+" + more + " more)" : "");
        }

        public static string Status()
        {
            if (!IsBusy) return "Test harness idle. " + ScenarioRegistry.All.Length + " scenarios registered.";
            var sb = new StringBuilder();
            sb.Append("Session ").Append(_session).Append(": ");
            if (_current.Scenario != null)
                sb.Append(_current.Scenario.Id).Append(" run ").Append(_current.Run).Append('/').Append(_current.Runs)
                  .Append(" phase ").Append(_phase).Append(" (")
                  .Append(((int)(TestContext.Now - _phaseStartTime).TotalSeconds).ToString(CultureInfo.InvariantCulture)).Append(" s)");
            sb.Append(", queued: ").Append(_queue.Count);
            sb.Append(", done: ").Append(_sessionResults.Count);
            return sb.ToString();
        }

        public static void Abort()
        {
            _queue.Clear();
            if (_phase == Phase.Idle) return;
            if (_result != null) _result.Fail("aborted");
            EnterPhase(Phase.Teardown);
        }

        // ---------------------------------------------------------------- tick

        public static void Tick()
        {
            try
            {
                switch (_phase)
                {
                    case Phase.Idle: TickIdle(); break;
                    case Phase.Setup: TickSetup(); break;
                    case Phase.WaitReady: TickWaitReady(); break;
                    case Phase.WarmUp: TickWarmUp(); break;
                    case Phase.Run: TickRun(); break;
                    case Phase.Assert: TickAssert(); break;
                    case Phase.Teardown: TickTeardown(); break;
                    case Phase.Report: TickReport(); break;
                }
            }
            catch (Exception ex)
            {
                // Never let the harness take the tick down; fail the run and clean up.
                if (_result != null) _result.Fail("exception in " + _phase + ": " + ex.Message);
                Logging.Instance.Write(Logging.Level.Error, "NanobotTest: {0} phase threw: {1}", _phase, ex);
                if (_phase == Phase.Teardown || _phase == Phase.Report) { ForceIdle(); }
                else EnterPhase(Phase.Teardown);
            }
        }

        private static void TickIdle()
        {
            if (_queue.Count == 0)
            {
                if (_log != null) CloseLog();
                return;
            }
            _current = _queue.Dequeue();
            _result = new TestResult(_current.Scenario.Id, _current.Run);
            _ctx = new TestContext(_result, _steamId, _ownerId, _origin, _current.Scenario.PerfMethods);
            _runStarting = false;

            var skip = _current.Scenario.SkipReason(_ctx);
            if (skip != null)
            {
                _result.SkipReason = skip;
                _result.Passed = false;
                EnterPhase(Phase.Report);
                return;
            }

            // Start line in chat, so a long scenario is not mistaken for a hang.
            Notify(string.Format(CultureInfo.InvariantCulture, "Running {0}{1} ({2}/{3}): {4} (max {5} s)",
                _current.Scenario.Id,
                _current.Runs > 1 ? " run " + _current.Run + "/" + _current.Runs : "",
                _sessionResults.Count + 1, _sessionTotal,
                _current.Scenario.Description, _current.Scenario.TimeoutSeconds));

            _settingsSnapshot = MyAPIGateway.Utilities.SerializeToXML(Mod.Settings);
            _savedMinDuration = MethodProfiler.MinDurationMs;
            string msg;
            MethodProfiler.SetMinDurationMs(_detail ? 0 : 10000, out msg); // aggregates only unless "detail" (per-call logs)
            TestIsolation.Reset();

            var baseScenario = _current.Scenario as TestScenarioBase;
            if (baseScenario != null) baseScenario.ResetForRun();
            EnterPhase(Phase.Setup);
        }

        private static void TickSetup()
        {
            _current.Scenario.Setup(_ctx);
            _ctx.MarkSetupTime();
            EnterPhase(Phase.WaitReady);
        }

        private static void TickWaitReady()
        {
            if ((TestContext.Frame - _phaseStartFrame) % PollFrames != 0) return;
            if (_current.Scenario.IsReady(_ctx))
            {
                EnterPhase(Phase.WarmUp);
                return;
            }
            if ((TestContext.Now - _phaseStartTime).TotalSeconds > _current.Scenario.ReadyTimeoutSeconds)
            {
                _result.Fail("ready-timeout");
                EnterPhase(Phase.Teardown);
            }
        }

        private static void TickWarmUp()
        {
            if (TestContext.Frame - _phaseStartFrame < WarmUpFrames) return;
            // Session start writes files; keep it out of the measured ticks (stats reset next frame).
            string msg;
            _profilerStarted = MethodProfiler.StartSession(0, _steamId, out msg, "test-" + _current.Scenario.Id + "-r" + _current.Run);
            _runStarting = true;
            EnterPhase(Phase.Run);
        }

        private static void TickRun()
        {
            if (_runStarting)
            {
                // The rig BaRs were held off since spawn, so every scan and weld lands in the measured run.
                _runStarting = false;
                Mod.ResetTickCostStats();
                Mod.ResetWeldBudgetStats();
                Mod.ResetGrindBudgetStats();
                _ctx.SetRigBarsEnabled(true);
                _ctx.RunStartTime = TestContext.Now;
                _ctx.NetStart = NetStats.Take(true); // FEAT-261123.7
                return;
            }

            _ctx.SampleTick();
            _current.Scenario.Sample(_ctx);

            var frames = TestContext.Frame - _phaseStartFrame;
            if (frames % PollFrames != 0) return;
            if (_current.Scenario.IsComplete(_ctx))
            {
                EnterPhase(Phase.Assert);
                return;
            }
            if (_ctx.RunSeconds > _current.Scenario.TimeoutSeconds)
            {
                _result.Fail("run-timeout after " + _current.Scenario.TimeoutSeconds + " s");
                EnterPhase(Phase.Assert); // perf numbers are still worth recording
            }
        }

        private static void TickAssert()
        {
            _result.DurationSeconds = _ctx.RunSeconds;
            if (_profilerStarted)
            {
                string msg;
                MethodProfiler.StopSession(out msg);
                _profilerStarted = false;
            }
            CollectStandardMetrics();
            try
            {
                _current.Scenario.Assert(_ctx, _result);
            }
            catch (Exception ex)
            {
                _result.Fail("assert threw: " + ex.Message);
            }
            EnterPhase(Phase.Teardown);
        }

        private static void TickTeardown()
        {
            if (_profilerStarted)
            {
                string msg;
                try { MethodProfiler.StopSession(out msg); } catch { }
                _profilerStarted = false;
            }
            try { if (_ctx != null) _current.Scenario.Teardown(_ctx); }
            catch (Exception ex) { _result.Fail("teardown threw: " + ex.Message); }
            try { if (_ctx != null) _ctx.CloseSpawned(); } catch { }

            RestoreSettings();
            if (_savedMinDuration >= 0)
            {
                string msg;
                MethodProfiler.SetMinDurationMs(_savedMinDuration, out msg);
                _savedMinDuration = -1;
            }
            TestIsolation.Reset();
            EnterPhase(Phase.Report);
        }

        private static void TickReport()
        {
            if (_result != null)
            {
                _sessionResults.Add(_result);
                WriteResult(_result);
                Notify(FormatChatLine(_result));
            }
            if (_queue.Count == 0)
            {
                WriteSessionSummary();
                Notify(string.Format(CultureInfo.InvariantCulture, "Session {0} finished: {1} passed, {2} failed, {3} skipped.",
                    _session, CountPassed(), CountFailed(), CountSkipped()));
            }
            ForceIdle();
        }

        // ---------------------------------------------------------------- helpers

        private static void EnterPhase(Phase phase)
        {
            _phase = phase;
            _phaseStartFrame = TestContext.Frame;
            _phaseStartTime = TestContext.Now;
        }

        private static void ForceIdle()
        {
            _ctx = null;
            _result = null;
            _current = new QueueItem();
            EnterPhase(Phase.Idle);
        }

        private static bool ResolveOriginAndOwner(ulong steamId, out long ownerId, out Vector3D origin, out string error)
        {
            ownerId = 0;
            origin = Vector3D.Zero;
            error = null;

            VRage.Game.ModAPI.IMyPlayer player = null;
            if (steamId != 0 && MyAPIGateway.Players != null)
            {
                var players = new List<VRage.Game.ModAPI.IMyPlayer>();
                MyAPIGateway.Players.GetPlayers(players, p => p.SteamUserId == steamId);
                if (players.Count > 0) player = players[0];
            }
            if (player == null && MyAPIGateway.Session != null) player = MyAPIGateway.Session.Player;
            if (player == null)
            {
                error = "Cannot resolve the calling player (owner/origin). On a DS run the command from a connected admin client.";
                return false;
            }
            ownerId = player.IdentityId;

            if (OriginOverride.HasValue)
            {
                origin = OriginOverride.Value;
                return true;
            }
            var character = player.Character;
            if (character == null)
            {
                error = "No character position for origin; set one with /nanobars test origin <x> <y> <z>.";
                return false;
            }
            var matrix = character.WorldMatrix;
            origin = character.GetPosition() + matrix.Forward * 300.0 + matrix.Up * 10.0;
            return true;
        }

        private static void RestoreSettings()
        {
            if (string.IsNullOrEmpty(_settingsSnapshot)) return;
            try
            {
                var restored = MyAPIGateway.Utilities.SerializeFromXML<SyncModSettings>(_settingsSnapshot);
                if (restored != null)
                {
                    SyncModSettings.ValidateAndClamp(restored);
                    Mod.Settings = restored;
                    Mod.SettingsChanged();
                    try { NetworkMessagingHandler.BroadcastModSettings(); } catch { }
                }
            }
            catch (Exception ex)
            {
                Logging.Instance.Write(Logging.Level.Error, "NanobotTest: settings restore failed: {0}", ex.Message);
            }
            _settingsSnapshot = null;
        }

        private static void CollectStandardMetrics()
        {
            _result.Metric("runS", _result.DurationSeconds);
            _result.Metric("tickAvgMs", _ctx.TickAvgMs);
            _result.Metric("tickPeakMs", _ctx.TickPeakMs);
            _result.Metric("simMin", _ctx.SimMin == double.MaxValue ? 0.0 : _ctx.SimMin);
            _result.Metric("simAvg", _ctx.SimAvg);
            _result.Metric("weldBudgetPeak", (long)_ctx.WeldBudgetPeak);
            _result.Metric("grindBudgetPeak", (long)_ctx.GrindBudgetPeak);
            _result.Metric("assignmentsPeak", (long)_ctx.AssignmentsPeak);
            _result.Metric("bgEnqueued", (long)Mod.BackgroundTasksEnqueued);
            _result.Metric("otherBars", (long)_ctx.OtherBarCount());
            var methods = _current.Scenario.PerfMethods;
            for (int i = 0; i < methods.Count; i++) _ctx.RecordMethodStats(methods[i]);
            _ctx.RecordNetMetrics();
        }

        private static int CountPassed()
        {
            var n = 0;
            for (int i = 0; i < _sessionResults.Count; i++) if (_sessionResults[i].Passed) n++;
            return n;
        }

        private static int CountSkipped()
        {
            var n = 0;
            for (int i = 0; i < _sessionResults.Count; i++) if (_sessionResults[i].SkipReason != null) n++;
            return n;
        }

        private static int CountFailed()
        {
            return _sessionResults.Count - CountPassed() - CountSkipped();
        }

        private static string FormatChatLine(TestResult r)
        {
            var sb = new StringBuilder();
            if (r.SkipReason != null) return sb.Append(r.Id).Append(" SKIPPED — ").Append(r.SkipReason).ToString();
            sb.Append(r.Id).Append(r.Passed ? " PASS " : " FAIL ");
            sb.Append(r.DurationSeconds.ToString("F1", CultureInfo.InvariantCulture)).Append("s");
            if (!r.Passed)
            {
                sb.Append(" — ");
                for (int i = 0; i < r.Failures.Count && i < 3; i++)
                {
                    if (i > 0) sb.Append("; ");
                    sb.Append(r.Failures[i]);
                }
                if (r.Failures.Count > 3) sb.Append("; +").Append(r.Failures.Count - 3);
            }
            return sb.ToString();
        }

        private static void Notify(string message)
        {
            try
            {
                if (_steamId != 0 && MyAPIGateway.Multiplayer != null && MyAPIGateway.Multiplayer.MultiplayerActive)
                    NetworkMessagingHandler.SendCommandResponse(_steamId, message, false, false, null, null);
                else if (MyAPIGateway.Utilities != null)
                    MyAPIGateway.Utilities.ShowMessage("Nanobars", message);
            }
            catch { }
            Logging.Instance.Write(Logging.Level.Event, "NanobotTest: {0}", message);
        }

        // ---------------------------------------------------------------- log file

        private static void OpenLog()
        {
            try
            {
                if (MyAPIGateway.Utilities == null) return;
                _log = MyAPIGateway.Utilities.WriteFileInLocalStorage(LogPrefix + _session + ".log", typeof(Mod));
                _log.WriteLine("# NanobotTest;modVersion=" + Constants.ModVersion + ";buildId=" + Constants.BuildId +
                               ";session=" + _session + ";generatedUtc=" + DateTime.UtcNow.ToString("u", CultureInfo.InvariantCulture));
                _log.Flush();
                var sessions = ReadSessionIndex();
                sessions.Add(_session);
                WriteSessionIndex(sessions);
            }
            catch (Exception ex)
            {
                Logging.Instance.Write(Logging.Level.Error, "NanobotTest: cannot open log: {0}", ex.Message);
                _log = null;
            }
        }

        // ---------------------------------------------------------------- clear

        // Local storage cannot be listed, so result logs are tracked in an index (like the profiler's).
        private const string SessionsIndexFileName = "NanobotTest.sessions";

        /// <summary>Deletes all result logs and the harness profiler sessions ("test-*").</summary>
        public static string ClearResults()
        {
            if (IsBusy) return "A test session is running; abort it first.";
            if (MethodProfiler.IsRunning) return "A profiling session is active; stop it first (/nanobars profile stop).";
            if (MyAPIGateway.Utilities == null) return "Storage not available.";

            var sessions = ReadSessionIndex();
            var logs = 0;
            for (int i = 0; i < sessions.Count; i++)
            {
                var name = LogPrefix + sessions[i] + ".log";
                try
                {
                    if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(name, typeof(Mod))) continue;
                    MyAPIGateway.Utilities.DeleteFileInLocalStorage(name, typeof(Mod));
                    logs++;
                }
                catch { }
            }
            try
            {
                if (MyAPIGateway.Utilities.FileExistsInLocalStorage(SessionsIndexFileName, typeof(Mod)))
                    MyAPIGateway.Utilities.DeleteFileInLocalStorage(SessionsIndexFileName, typeof(Mod));
            }
            catch { }
            var profiles = MethodProfiler.ClearSessionsWithPrefix("test-");
            return string.Format(CultureInfo.InvariantCulture, "Deleted {0} test result log(s) and {1} test profiler session(s).", logs, profiles);
        }

        private static List<string> ReadSessionIndex()
        {
            var sessions = new List<string>();
            try
            {
                if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(SessionsIndexFileName, typeof(Mod))) return sessions;
                var reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(SessionsIndexFileName, typeof(Mod));
                try
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (line.Length > 0 && !sessions.Contains(line)) sessions.Add(line);
                    }
                }
                finally { reader.Close(); }
            }
            catch { }
            return sessions;
        }

        private static void WriteSessionIndex(List<string> sessions)
        {
            try
            {
                var writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(SessionsIndexFileName, typeof(Mod));
                try
                {
                    for (int i = 0; i < sessions.Count; i++) writer.WriteLine(sessions[i]);
                    writer.Flush();
                }
                finally { writer.Close(); }
            }
            catch { }
        }

        private static void CloseLog()
        {
            try { _log.Flush(); _log.Close(); } catch { }
            _log = null;
        }

        private static void WriteResult(TestResult r)
        {
            if (_log == null) return;
            var sb = new StringBuilder();
            for (int i = 0; i < r.Failures.Count; i++)
            {
                sb.Length = 0;
                sb.Append("fail=").Append(r.Id).Append(";run=").Append(r.Run).Append(";reason=").Append(r.Failures[i].Replace('\n', ' ').Replace('\r', ' '));
                _log.WriteLine(sb.ToString());
            }
            sb.Length = 0;
            if (r.SkipReason != null)
            {
                _log.WriteLine("result=" + r.Id + ";run=" + r.Run + ";pass=skipped;reason=" + r.SkipReason.Replace(';', ',').Replace('\n', ' '));
                _log.Flush();
                return;
            }
            sb.Append("result=").Append(r.Id).Append(";run=").Append(r.Run).Append(";pass=").Append(r.Passed ? "true" : "false");
            for (int i = 0; i < r.Metrics.Count; i++)
                sb.Append(';').Append(r.Metrics[i].Key).Append('=').Append(r.Metrics[i].Value);
            _log.WriteLine(sb.ToString());
            _log.Flush();
        }

        private static void WriteSessionSummary()
        {
            if (_log == null) return;
            // agg= per scenario with more than one run: min/avg/max for every numeric metric.
            var ids = new List<string>();
            for (int i = 0; i < _sessionResults.Count; i++) if (!ids.Contains(_sessionResults[i].Id)) ids.Add(_sessionResults[i].Id);
            for (int s = 0; s < ids.Count; s++)
            {
                var runs = new List<TestResult>();
                for (int i = 0; i < _sessionResults.Count; i++) if (_sessionResults[i].Id == ids[s]) runs.Add(_sessionResults[i]);
                if (runs.Count < 2) continue;
                var sb = new StringBuilder();
                sb.Append("agg=").Append(ids[s]).Append(";runs=").Append(runs.Count);
                var keys = runs[0].Metrics;
                for (int k = 0; k < keys.Count; k++)
                {
                    var key = keys[k].Key;
                    double min = double.MaxValue, max = double.MinValue, sum = 0; var n = 0;
                    for (int r = 0; r < runs.Count; r++)
                    {
                        var v = runs[r].GetNumeric(key);
                        if (double.IsNaN(v)) continue;
                        if (v < min) min = v; if (v > max) max = v; sum += v; n++;
                    }
                    if (n == 0) continue;
                    sb.Append(';').Append(key).Append('=')
                      .Append(min.ToString("F3", CultureInfo.InvariantCulture)).Append('/')
                      .Append((sum / n).ToString("F3", CultureInfo.InvariantCulture)).Append('/')
                      .Append(max.ToString("F3", CultureInfo.InvariantCulture));
                }
                _log.WriteLine(sb.ToString());
            }
            var passed = CountPassed();
            _log.WriteLine("summary=passed=" + passed + ";failed=" + CountFailed() + ";skipped=" + CountSkipped() + ";session=" + _session);
            _log.Flush();
        }
    }
}

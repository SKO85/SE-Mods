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
        private const int WaitReadySeconds = 60;
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

        public static Vector3D? OriginOverride;

        public static bool IsBusy { get { return _phase != Phase.Idle || _queue.Count > 0; } }

        // ---------------------------------------------------------------- command surface

        public static string Enqueue(IList<ITestScenario> scenarios, int repeat, ulong steamId)
        {
            if (IsBusy) return null;

            long ownerId;
            Vector3D origin;
            string error;
            if (!ResolveOriginAndOwner(steamId, out ownerId, out origin, out error)) return error;

            _steamId = steamId;
            _ownerId = ownerId;
            _origin = origin;
            _session = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            _sessionResults.Clear();

            for (int i = 0; i < scenarios.Count; i++)
                for (int r = 1; r <= repeat; r++)
                    _queue.Enqueue(new QueueItem { Scenario = scenarios[i], Run = r, Runs = repeat });

            OpenLog();
            return string.Format(CultureInfo.InvariantCulture,
                "Queued {0} scenario run(s), session {1}. Origin {2:F0},{3:F0},{4:F0}. Results in {5}{1}.log",
                _queue.Count, _session, origin.X, origin.Y, origin.Z, LogPrefix);
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

            _settingsSnapshot = MyAPIGateway.Utilities.SerializeToXML(Mod.Settings);
            _savedMinDuration = MethodProfiler.MinDurationMs;
            string msg;
            MethodProfiler.SetMinDurationMs(10000, out msg); // aggregates only, no per-call file spam
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
            if ((TestContext.Now - _phaseStartTime).TotalSeconds > WaitReadySeconds)
            {
                _result.Fail("ready-timeout");
                EnterPhase(Phase.Teardown);
            }
        }

        private static void TickWarmUp()
        {
            if (TestContext.Frame - _phaseStartFrame < WarmUpFrames) return;
            Mod.ResetTickCostStats();
            Mod.ResetWeldBudgetStats();
            Mod.ResetGrindBudgetStats();
            string msg;
            _profilerStarted = MethodProfiler.StartSession(0, _steamId, out msg, "test-" + _current.Scenario.Id + "-r" + _current.Run);
            _ctx.RunStartTime = TestContext.Now;
            EnterPhase(Phase.Run);
        }

        private static void TickRun()
        {
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
                Notify(string.Format(CultureInfo.InvariantCulture, "Session {0} finished: {1} passed, {2} failed.",
                    _session, CountPassed(), _sessionResults.Count - CountPassed()));
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
        }

        private static int CountPassed()
        {
            var n = 0;
            for (int i = 0; i < _sessionResults.Count; i++) if (_sessionResults[i].Passed) n++;
            return n;
        }

        private static string FormatChatLine(TestResult r)
        {
            var sb = new StringBuilder();
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
            }
            catch (Exception ex)
            {
                Logging.Instance.Write(Logging.Level.Error, "NanobotTest: cannot open log: {0}", ex.Message);
                _log = null;
            }
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
            _log.WriteLine("summary=passed=" + passed + ";failed=" + (_sessionResults.Count - passed) + ";session=" + _session);
            _log.Flush();
        }
    }
}

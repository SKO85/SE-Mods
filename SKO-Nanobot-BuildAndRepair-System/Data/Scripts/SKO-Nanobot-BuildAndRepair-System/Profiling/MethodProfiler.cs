using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using VRage.Utils;

namespace SKONanobotBuildAndRepairSystem.Profiling
{
    internal static class MethodProfiler
    {
        private const int MaxDetailLength = 4096;
        private const int WarmupCallsPerMethod = 20;
        private const int DefaultAutoStopSeconds = 5 * 60;
        private const string SessionsIndexFileName = "NanobotProfiler.sessions";
        private static readonly object _syncRoot = new object();
        private static readonly Dictionary<string, TextWriter> _writers = new Dictionary<string, TextWriter>();
        private static readonly Dictionary<string, MethodStats> _methodStats = new Dictionary<string, MethodStats>();
        private static bool _isRunning;
        private static DateTime _startedUtc;
        private static DateTime? _autoStopUtc;
        private static ulong _startedBySteamId;
        private static string _sessionName;
        private static string _sessionPrefix;

        private static float _minSimSpeed = float.MaxValue;
        private static float _maxSimSpeed = float.MinValue;
        private static double _sumSimSpeed;
        private static long _simSpeedSamples;
        private static int _minDurationMs = 1;

        private const int MaxAutoStopSeconds = 24 * 60 * 60;
        private const int MinDurationMsMin = 0;
        private const int MinDurationMsMax = 10000;

        private sealed class MethodStats
        {
            public long Calls;
            public double TotalMs;
            public double MinMs = double.MaxValue;
            public double MaxMs = double.MinValue;
            public long WarmupCalls;
            public double WarmupTotalMs;
            public long SteadyCalls;
            public double SteadyTotalMs;
        }

        // --- Per-grid cost tracking ---
        private sealed class GridCostEntry
        {
            public long EntityId;
            public string Name;
            public double TotalMs;
            public long Calls;
        }

        private static readonly Dictionary<string, GridCostEntry> _gridCosts = new Dictionary<string, GridCostEntry>();

        /// <summary>
        /// Report time spent on a specific grid. Called from scan/weld/grind methods.
        /// </summary>
        public static void ReportGridCost(long gridEntityId, string gridName, double elapsedMs)
        {
            if (!_isRunning) return;
            lock (_syncRoot)
            {
                var key = gridEntityId.ToString();
                GridCostEntry entry;
                if (!_gridCosts.TryGetValue(key, out entry))
                {
                    entry = new GridCostEntry { EntityId = gridEntityId, Name = gridName ?? key };
                    _gridCosts[key] = entry;
                }
                entry.TotalMs += elapsedMs;
                entry.Calls++;
            }
        }

        public static bool HasSummaryData
        {
            get { lock (_syncRoot) { return _methodStats.Count > 0; } }
        }

        /// <summary>
        /// Build a profile summary message for network broadcast and local rendering.
        /// Returns null if no data is available.
        /// </summary>
        public static MsgProfileSummary BuildSummaryMessage(int maxMethods, int maxGrids)
        {
            List<KeyValuePair<string, MethodStats>> methods;
            List<KeyValuePair<string, GridCostEntry>> grids;

            lock (_syncRoot)
            {
                if (_methodStats.Count == 0) return null;
                methods = _methodStats.ToList();
                grids = _gridCosts.Count > 0 ? _gridCosts.ToList() : null;
            }

            var msg = new MsgProfileSummary();
            msg.IsRunning = _isRunning;
            msg.ElapsedSeconds = _isRunning ? (float)(DateTime.UtcNow - _startedUtc).TotalSeconds : 0;
            msg.MethodCount = methods.Count;
            msg.SimSpeedMin = _minSimSpeed == float.MaxValue ? 0f : _minSimSpeed;
            msg.SimSpeedAvg = _simSpeedSamples > 0 ? (float)(_sumSimSpeed / _simSpeedSamples) : 0f;
            msg.SessionName = _sessionName;

            // Domain summary
            var domainAgg = new Dictionary<string, MethodStats>();
            foreach (var m in methods)
            {
                var domain = ClassifyDomain(m.Key);
                MethodStats agg;
                if (!domainAgg.TryGetValue(domain, out agg))
                {
                    agg = new MethodStats();
                    domainAgg[domain] = agg;
                }
                MergeStats(agg, m.Value);
            }

            msg.Domains = new List<ProfileDomainEntry>();
            var domainList = domainAgg.ToList();
            domainList.Sort((a, b) => b.Value.TotalMs.CompareTo(a.Value.TotalMs));
            foreach (var d in domainList)
            {
                var s = d.Value;
                msg.Domains.Add(new ProfileDomainEntry
                {
                    Name = d.Key,
                    Calls = s.Calls,
                    TotalMs = (float)s.TotalMs,
                    AvgMs = s.Calls > 0 ? (float)(s.TotalMs / s.Calls) : 0,
                    MaxMs = s.MaxMs == double.MinValue ? 0f : (float)s.MaxMs
                });
            }

            // Top methods
            methods.Sort((a, b) => b.Value.TotalMs.CompareTo(a.Value.TotalMs));
            var topCount = Math.Min(maxMethods, methods.Count);

            msg.TopMethods = new List<ProfileMethodEntry>(topCount);
            for (int i = 0; i < topCount; i++)
            {
                var name = methods[i].Key;
                var s = methods[i].Value;
                var calls = s.SteadyCalls > 0 ? s.SteadyCalls : s.Calls;
                var total = s.SteadyCalls > 0 ? s.SteadyTotalMs : s.TotalMs;
                msg.TopMethods.Add(new ProfileMethodEntry
                {
                    Name = name,
                    Calls = calls,
                    TotalMs = (float)total,
                    AvgMs = calls > 0 ? (float)(total / calls) : 0,
                    MinMs = s.MinMs == double.MaxValue ? 0f : (float)s.MinMs,
                    MaxMs = s.MaxMs == double.MinValue ? 0f : (float)s.MaxMs
                });
            }

            // Top grids with owner name lookup
            if (grids != null && grids.Count > 0)
            {
                grids.Sort((a, b) => b.Value.TotalMs.CompareTo(a.Value.TotalMs));
                var gridCount = Math.Min(maxGrids, grids.Count);
                msg.TopGrids = new List<ProfileGridEntry>(gridCount);
                for (int i = 0; i < gridCount; i++)
                {
                    var g = grids[i].Value;
                    string ownerName = null;
                    try
                    {
                        VRage.ModAPI.IMyEntity ent;
                        if (MyAPIGateway.Entities.TryGetEntityById(g.EntityId, out ent) && ent != null)
                        {
                            var grid = ent as VRage.Game.ModAPI.IMyCubeGrid;
                            if (grid != null && grid.BigOwners != null && grid.BigOwners.Count > 0)
                            {
                                var ownerId = grid.BigOwners[0];
                                var players = new List<VRage.Game.ModAPI.IMyPlayer>();
                                MyAPIGateway.Players.GetPlayers(players, p => p.IdentityId == ownerId);
                                if (players.Count > 0)
                                    ownerName = players[0].DisplayName;
                            }
                        }
                    }
                    catch { }

                    msg.TopGrids.Add(new ProfileGridEntry
                    {
                        Name = g.Name,
                        Calls = g.Calls,
                        TotalMs = (float)g.TotalMs,
                        OwnerName = ownerName
                    });
                }
            }

            return msg;
        }

        public static string SessionName
        {
            get { return _sessionName; }
        }

        public static bool IsEnabled
        {
            get { return _isRunning; }
        }

        public static bool IsRunning
        {
            get { return _isRunning; }
        }

        public static int DefaultAutoStopDurationSeconds
        {
            get { return DefaultAutoStopSeconds; }
        }

        public static int MinDurationMs
        {
            get { return _minDurationMs; }
        }

        public static double ElapsedSeconds
        {
            get { return _isRunning ? (DateTime.UtcNow - _startedUtc).TotalSeconds : 0; }
        }

        public static double TotalSessionSeconds
        {
            get { return _isRunning && _autoStopUtc.HasValue ? (_autoStopUtc.Value - _startedUtc).TotalSeconds : 0; }
        }

        public static bool SetMinDurationMs(int value, out string message)
        {
            if (value < MinDurationMsMin || value > MinDurationMsMax)
            {
                message = string.Format("Invalid value. Range: {0}-{1}.", MinDurationMsMin, MinDurationMsMax);
                return false;
            }

            _minDurationMs = value;
            message = string.Format("MinDurationMs set to {0}.", value);
            return true;
        }

        public static string GetStatusMessage()
        {
            if (!_isRunning)
            {
                var stopped = string.Format("Profiling status: stopped (MinDurationMs={0}).", _minDurationMs);
                if (!string.IsNullOrEmpty(_sessionName))
                    stopped += string.Format(" Last session: {0}", _sessionName);
                return stopped;
            }

            var duration = DateTime.UtcNow - _startedUtc;
            var status = string.Format("Profiling status: running for {0:F1}s. Session: {1} (MinDurationMs={2}).",
                duration.TotalSeconds, _sessionName ?? "?", _minDurationMs);
            if (_autoStopUtc.HasValue)
            {
                var remaining = _autoStopUtc.Value - DateTime.UtcNow;
                if (remaining.TotalSeconds > 0)
                    status += string.Format(" Auto-stop in {0:F1}s.", remaining.TotalSeconds);
                else
                    status += " Auto-stop is due.";
            }

            return status;
        }

        /// <summary>
        /// Returns a formatted summary of current profiling stats for in-game display.
        /// Works whether profiling is running or has been stopped (shows last collected data).
        /// </summary>
        public static string GetSummaryText()
        {
            List<KeyValuePair<string, MethodStats>> methods;
            bool running;
            double elapsedSec;

            lock (_syncRoot)
            {
                if (_methodStats.Count == 0)
                    return "No profiling data collected. Start a profiling session first with /nanobars profile start.";

                methods = _methodStats.ToList();
                running = _isRunning;
                elapsedSec = running ? (DateTime.UtcNow - _startedUtc).TotalSeconds : 0;
            }

            var sb = new StringBuilder(2048);

            // Header
            if (running)
                sb.AppendLine(string.Format("Profiling: RUNNING ({0:F1}s elapsed, MinDurationMs={1})", elapsedSec, _minDurationMs));
            else
                sb.AppendLine(string.Format("Profiling: STOPPED (MinDurationMs={0})", _minDurationMs));

            sb.AppendLine(string.Format("Methods tracked: {0}", methods.Count));
            sb.AppendLine();

            // Domain summary
            var domainStats = new Dictionary<string, MethodStats>();
            foreach (var method in methods)
            {
                var domain = ClassifyDomain(method.Key);
                MethodStats aggregate;
                if (!domainStats.TryGetValue(domain, out aggregate))
                {
                    aggregate = new MethodStats();
                    domainStats[domain] = aggregate;
                }
                MergeStats(aggregate, method.Value);
            }

            sb.AppendLine("--- DOMAIN SUMMARY ---");
            sb.AppendLine(string.Format("{0,-12} {1,8} {2,10} {3,8} {4,8}",
                "Domain", "Calls", "TotalMs", "AvgMs", "MaxMs"));

            var domainList = domainStats.ToList();
            domainList.Sort((a, b) => b.Value.TotalMs.CompareTo(a.Value.TotalMs));
            foreach (var entry in domainList)
            {
                var s = entry.Value;
                sb.AppendLine(string.Format("{0,-12} {1,8} {2,10:F1} {3,8:F3} {4,8:F3}",
                    entry.Key,
                    s.Calls,
                    s.TotalMs,
                    SafeAverage(s.TotalMs, s.Calls),
                    s.MaxMs == double.MinValue ? 0.0 : s.MaxMs));
            }

            sb.AppendLine();

            // Top methods by total time (top 20)
            methods.Sort((a, b) => b.Value.TotalMs.CompareTo(a.Value.TotalMs));
            var topCount = Math.Min(20, methods.Count);

            sb.AppendLine(string.Format("--- TOP {0} METHODS (by total time) ---", topCount));
            sb.AppendLine(string.Format("{0,-38} {1,8} {2,10} {3,8} {4,8} {5,8}",
                "Method", "Calls", "TotalMs", "AvgMs", "MinMs", "MaxMs"));

            for (int i = 0; i < topCount; i++)
            {
                var name = methods[i].Key;
                var s = methods[i].Value;
                // Truncate long method names for display
                if (name.Length > 38) name = name.Substring(0, 35) + "...";
                sb.AppendLine(string.Format("{0,-38} {1,8} {2,10:F1} {3,8:F3} {4,8:F3} {5,8:F3}",
                    name,
                    s.SteadyCalls > 0 ? s.SteadyCalls : s.Calls,
                    s.SteadyCalls > 0 ? s.SteadyTotalMs : s.TotalMs,
                    s.SteadyCalls > 0 ? SafeAverage(s.SteadyTotalMs, s.SteadyCalls) : SafeAverage(s.TotalMs, s.Calls),
                    s.MinMs == double.MaxValue ? 0.0 : s.MinMs,
                    s.MaxMs == double.MinValue ? 0.0 : s.MaxMs));
            }

            return sb.ToString();
        }

        public static bool StartSession(out string message)
        {
            return StartSession(DefaultAutoStopSeconds, 0, out message, null);
        }

        public static bool StartSession(int autoStopSeconds, out string message)
        {
            return StartSession(autoStopSeconds, 0, out message, null);
        }

        public static bool StartSession(int autoStopSeconds, ulong startedBySteamId, out string message, string sessionName = null)
        {
            lock (_syncRoot)
            {
                if (_isRunning)
                {
                    message = "Profiling is already running.";
                    return false;
                }

                if (autoStopSeconds < 0 || autoStopSeconds > MaxAutoStopSeconds)
                {
                    message = string.Format("Invalid auto-stop seconds. Use a value between 0 and {0}.", MaxAutoStopSeconds);
                    return false;
                }

                // Generate or sanitize session name.
                if (string.IsNullOrEmpty(sessionName))
                    sessionName = DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-profiling";
                _sessionName = SanitizeName(sessionName, 60);
                _sessionPrefix = _sessionName + ".";

                // If a session with this name already exists, delete its files first.
                DeleteSessionFiles(_sessionPrefix);

                CloseInternal();
                _methodStats.Clear();
                _gridCosts.Clear();
                _minSimSpeed = float.MaxValue;
                _maxSimSpeed = float.MinValue;
                _sumSimSpeed = 0;
                _simSpeedSamples = 0;
                _isRunning = true;
                _startedBySteamId = startedBySteamId;
                _startedUtc = DateTime.UtcNow;
                _autoStopUtc = autoStopSeconds > 0 ? _startedUtc.AddSeconds(autoStopSeconds) : (DateTime?)null;
                message = autoStopSeconds > 0
                    ? string.Format("Profiling started for {0}s. Session: {1}", autoStopSeconds, _sessionName)
                    : string.Format("Profiling started. Session: {0}. Use /nanobars profile stop when done.", _sessionName);
                return true;
            }
        }

        public static bool StopSession(out string message)
        {
            lock (_syncRoot)
            {
                if (!_isRunning)
                {
                    message = "Profiling is not running.";
                    return false;
                }

                _isRunning = false;
                var duration = DateTime.UtcNow - _startedUtc;
                _autoStopUtc = null;
                WriteSummary(duration);
                WriteManifest();
                AppendSessionIndex();
                CloseInternal();
                message = string.Format("Profiling done. Session: {0}. Duration: {1:F1}s. Files: {0}.NanobotProfiler.*.log",
                    _sessionName, duration.TotalSeconds);
                return true;
            }
        }

        public static bool TickAutoStop(out string message, out ulong steamId)
        {
            message = null;
            steamId = 0;
            lock (_syncRoot)
            {
                if (!_isRunning || !_autoStopUtc.HasValue || DateTime.UtcNow < _autoStopUtc.Value)
                    return false;

                _isRunning = false;
                var duration = DateTime.UtcNow - _startedUtc;
                _autoStopUtc = null;
                steamId = _startedBySteamId;
                WriteSummary(duration);
                WriteManifest();
                AppendSessionIndex();
                CloseInternal();
                message = string.Format("Profiling auto-stopped after {0:F1}s. Session: {1}", duration.TotalSeconds, _sessionName);
                MyLog.Default.WriteLineAndConsole("MethodProfiler: " + message);
                return true;
            }
        }

        public static long Start()
        {
            if (!IsEnabled)
                return 0L;

            return Stopwatch.GetTimestamp();
        }

        public static void StopAndLog(string methodName, long startTimestamp, Func<string> detailsFactory = null)
        {
            if (startTimestamp == 0L || !IsEnabled)
                return;

            var elapsedTicks = Stopwatch.GetTimestamp() - startTimestamp;
            var elapsedMs = elapsedTicks * 1000.0 / Stopwatch.Frequency;

            UpdateAggregate(methodName, elapsedMs);

            if (elapsedMs < _minDurationMs)
                return;

            string details = null;
            if (detailsFactory != null)
            {
                try
                {
                    details = detailsFactory();
                }
                catch (Exception ex)
                {
                    details = "details-error=" + ex.Message;
                }
            }

            Write(methodName, elapsedMs, details);
        }

        private static void UpdateAggregate(string methodName, double elapsedMs)
        {
            if (string.IsNullOrWhiteSpace(methodName))
                return;

            lock (_syncRoot)
            {
                MethodStats stats;
                if (!_methodStats.TryGetValue(methodName, out stats))
                {
                    stats = new MethodStats();
                    _methodStats[methodName] = stats;
                }

                stats.Calls++;
                stats.TotalMs += elapsedMs;
                if (elapsedMs < stats.MinMs)
                    stats.MinMs = elapsedMs;
                if (elapsedMs > stats.MaxMs)
                    stats.MaxMs = elapsedMs;

                if (stats.Calls <= WarmupCallsPerMethod)
                {
                    stats.WarmupCalls++;
                    stats.WarmupTotalMs += elapsedMs;
                }
                else
                {
                    stats.SteadyCalls++;
                    stats.SteadyTotalMs += elapsedMs;
                }
            }
        }

        private static void WriteSummary(TimeSpan sessionDuration)
        {
            if (MyAPIGateway.Utilities == null)
                return;

            List<KeyValuePair<string, MethodStats>> methods;
            lock (_syncRoot)
            {
                methods = _methodStats.ToList();
            }

            if (methods.Count == 0)
                return;

            var domainStats = new Dictionary<string, MethodStats>();
            foreach (var method in methods)
            {
                var domain = ClassifyDomain(method.Key);
                MethodStats aggregate;
                if (!domainStats.TryGetValue(domain, out aggregate))
                {
                    aggregate = new MethodStats();
                    domainStats[domain] = aggregate;
                }

                MergeStats(aggregate, method.Value);
            }

            TextWriter writer = null;
            try
            {
                writer = MyAPIGateway.Utilities.WriteFileInLocalStorage((_sessionPrefix ?? "") + "NanobotProfiler.Summary.log", typeof(Mod));
                writer.WriteLine("# Nanobot method profiler summary");
                writer.WriteLine(string.Format("# session={0};sessionSeconds={1:F1};warmupCallsPerMethod={2};generatedUtc={3:u}", _sessionName, sessionDuration.TotalSeconds, WarmupCallsPerMethod, DateTime.UtcNow));
                writer.WriteLine(string.Format("# simSpeed: min={0:F2};max={1:F2};avg={2:F2};samples={3}",
                    _minSimSpeed == float.MaxValue ? 0f : _minSimSpeed,
                    _maxSimSpeed == float.MinValue ? 0f : _maxSimSpeed,
                    _simSpeedSamples > 0 ? _sumSimSpeed / _simSpeedSamples : 0.0,
                    _simSpeedSamples));
                writer.WriteLine("# domain summary: domain;calls;totalMs;avgMs;minMs;maxMs;warmupAvgMs;steadyAvgMs");

                foreach (var entry in domainStats.OrderByDescending(e => e.Value.TotalMs))
                {
                    var stats = entry.Value;
                    writer.WriteLine(string.Format("domain={0};calls={1};totalMs={2:F3};avgMs={3:F3};minMs={4:F3};maxMs={5:F3};warmupAvgMs={6:F3};steadyAvgMs={7:F3}",
                        entry.Key,
                        stats.Calls,
                        stats.TotalMs,
                        SafeAverage(stats.TotalMs, stats.Calls),
                        stats.MinMs == double.MaxValue ? 0.0 : stats.MinMs,
                        stats.MaxMs == double.MinValue ? 0.0 : stats.MaxMs,
                        SafeAverage(stats.WarmupTotalMs, stats.WarmupCalls),
                        SafeAverage(stats.SteadyTotalMs, stats.SteadyCalls)));
                }

                writer.WriteLine("# method summary: method;domain;calls;totalMs;avgMs;minMs;maxMs;warmupCalls;warmupAvgMs;steadyCalls;steadyAvgMs");
                foreach (var entry in methods.OrderByDescending(e => e.Value.TotalMs))
                {
                    var methodName = entry.Key;
                    var stats = entry.Value;
                    writer.WriteLine(string.Format("method={0};domain={1};calls={2};totalMs={3:F3};avgMs={4:F3};minMs={5:F3};maxMs={6:F3};warmupCalls={7};warmupAvgMs={8:F3};steadyCalls={9};steadyAvgMs={10:F3}",
                        methodName,
                        ClassifyDomain(methodName),
                        stats.Calls,
                        stats.TotalMs,
                        SafeAverage(stats.TotalMs, stats.Calls),
                        stats.MinMs == double.MaxValue ? 0.0 : stats.MinMs,
                        stats.MaxMs == double.MinValue ? 0.0 : stats.MaxMs,
                        stats.WarmupCalls,
                        SafeAverage(stats.WarmupTotalMs, stats.WarmupCalls),
                        stats.SteadyCalls,
                        SafeAverage(stats.SteadyTotalMs, stats.SteadyCalls)));
                }

                writer.Flush();
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("MethodProfiler summary failed: " + ex.Message);
            }
            finally
            {
                if (writer != null)
                {
                    try
                    {
                        writer.Close();
                        writer.Dispose();
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static void MergeStats(MethodStats aggregate, MethodStats source)
        {
            aggregate.Calls += source.Calls;
            aggregate.TotalMs += source.TotalMs;
            if (source.MinMs < aggregate.MinMs)
                aggregate.MinMs = source.MinMs;
            if (source.MaxMs > aggregate.MaxMs)
                aggregate.MaxMs = source.MaxMs;

            aggregate.WarmupCalls += source.WarmupCalls;
            aggregate.WarmupTotalMs += source.WarmupTotalMs;
            aggregate.SteadyCalls += source.SteadyCalls;
            aggregate.SteadyTotalMs += source.SteadyTotalMs;
        }

        private static string ClassifyDomain(string methodName)
        {
            if (string.IsNullOrWhiteSpace(methodName))
                return "Utility";

            if (methodName.IndexOf("Scan", StringComparison.OrdinalIgnoreCase) >= 0 || methodName.IndexOf("Target", StringComparison.OrdinalIgnoreCase) >= 0 || methodName.IndexOf("Source", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Scan";

            if (methodName.IndexOf("Weld", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Weld";

            if (methodName.IndexOf("Grind", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Grind";

            if (methodName.IndexOf("Collect", StringComparison.OrdinalIgnoreCase) >= 0 || methodName.IndexOf("Floating", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Collect";

            if (methodName.IndexOf("Push", StringComparison.OrdinalIgnoreCase) >= 0 || methodName.IndexOf("Inventory", StringComparison.OrdinalIgnoreCase) >= 0 || methodName.IndexOf("Pull", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Inventory";

            if (methodName.IndexOf("Update", StringComparison.OrdinalIgnoreCase) >= 0 || methodName.IndexOf("Simulation", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Update";

            return "Utility";
        }

        private static double SafeAverage(double total, long calls)
        {
            if (calls <= 0)
                return 0.0;

            return total / calls;
        }

        private static void Write(string methodName, double elapsedMs, string details)
        {
            try
            {
                var writer = GetOrCreateWriter(methodName);
                if (writer == null)
                    return;

                var simSpeed = MyAPIGateway.Physics != null ? MyAPIGateway.Physics.ServerSimulationRatio : 1.0f;

                lock (_syncRoot)
                {
                    if (simSpeed < _minSimSpeed) _minSimSpeed = simSpeed;
                    if (simSpeed > _maxSimSpeed) _maxSimSpeed = simSpeed;
                    _sumSimSpeed += simSpeed;
                    _simSpeedSamples++;
                }

                var line = new StringBuilder(512);
                line.Append(DateTime.UtcNow.ToString("u"));
                line.Append(";ms=").Append(elapsedMs.ToString("F3"));
                line.Append(";simSpeed=").Append(simSpeed.ToString("F2"));

                if (!string.IsNullOrWhiteSpace(details))
                {
                    if (details.Length > MaxDetailLength)
                        details = details.Substring(0, MaxDetailLength);

                    line.Append(';').Append(details.Replace('\r', ' ').Replace('\n', ' '));
                }

                lock (_syncRoot)
                {
                    writer.WriteLine(line.ToString());
                    // BUG-125: removed per-line `writer.Flush()`. Under 58-BaR contention the sync
                    // file flush + global lock added ~8 ms wall time per profiler exit during heavy
                    // ticks (measured via BUG-124's outer-vs-inner sub-timer gap on session
                    // 20260429185841 — `transportEmptyMs=8.418` vs inner `ms=0.474` for the same
                    // call). TextWriter buffering still persists data on buffer-fill / close /
                    // session-end; periodic defensive flush is added separately so a long-running
                    // profile that doesn't call StopSession still gets its data on disk.
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("MethodProfiler write failed: " + ex.Message);
            }
        }

        // BUG-125: defensive periodic flush. Called from Mod.UpdateBeforeSimulation every 5 s.
        // Without per-line Flush, data sits in TextWriter buffers until flushed; periodic flush
        // bounds data loss to ~5 s on hard crash. Single lock + flush per writer — at ~30 method
        // writers and 5 s cadence the cost is negligible.
        public static void FlushAll()
        {
            if (!IsEnabled) return;
            lock (_syncRoot)
            {
                foreach (var entry in _writers)
                {
                    try { if (entry.Value != null) entry.Value.Flush(); } catch { }
                }
            }
        }

        private static TextWriter GetOrCreateWriter(string methodName)
        {
            if (string.IsNullOrWhiteSpace(methodName) || MyAPIGateway.Utilities == null)
                return null;

            lock (_syncRoot)
            {
                TextWriter writer;
                if (_writers.TryGetValue(methodName, out writer))
                    return writer;

                var fileName = (_sessionPrefix ?? "") + "NanobotProfiler." + SanitizeMethodName(methodName) + ".log";
                writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(fileName, typeof(Mod));
                writer.WriteLine("# utc;ms;simSpeed;details");
                writer.Flush();
                _writers[methodName] = writer;
                return writer;
            }
        }

        private static string SanitizeMethodName(string methodName)
        {
            return SanitizeName(methodName, 80);
        }

        private static string SanitizeName(string name, int maxLength)
        {
            var sb = new StringBuilder(name.Length);
            foreach (var ch in name)
            {
                if (char.IsLetterOrDigit(ch) || ch == '.' || ch == '-' || ch == '_')
                    sb.Append(ch);
                else
                    sb.Append('_');
            }

            var sanitized = sb.ToString();
            if (sanitized.Length > maxLength)
                sanitized = sanitized.Substring(0, maxLength);

            return sanitized;
        }

        public static void Close()
        {
            lock (_syncRoot)
            {
                _isRunning = false;
                _autoStopUtc = null;
                WriteManifest();
                AppendSessionIndex();
                CloseInternal();
                _methodStats.Clear();
                _gridCosts.Clear();
            }
        }

        /// <summary>
        /// Clear (truncate) all files for a specific session or all sessions.
        /// </summary>
        public static string ClearSession(string sessionNameOrAll)
        {
            if (MyAPIGateway.Utilities == null)
                return "Storage not available.";

            if (_isRunning)
                return "Cannot clear while profiling is running. Stop the session first.";

            var isAll = sessionNameOrAll == "all";
            var sessions = ReadSessionIndex();
            if (sessions.Count == 0)
                return "No profiling sessions found.";

            var cleared = 0;
            var toClear = new List<string>();

            foreach (var session in sessions)
            {
                if (!isAll && session != sessionNameOrAll)
                    continue;

                var manifestName = session + ".NanobotProfiler.manifest";
                if (MyAPIGateway.Utilities.FileExistsInLocalStorage(manifestName, typeof(Mod)))
                {
                    var files = new List<string>();
                    try
                    {
                        var reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(manifestName, typeof(Mod));
                        try
                        {
                            string line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                line = line.Trim();
                                if (!string.IsNullOrEmpty(line))
                                    files.Add(line);
                            }
                        }
                        finally { reader.Close(); }
                    }
                    catch { }

                    // Truncate each log file listed in the manifest.
                    foreach (var file in files)
                    {
                        DeleteFile(file);
                    }
                    // Truncate the manifest itself.
                    DeleteFile(manifestName);
                    cleared++;
                }

                toClear.Add(session);
            }

            // Rewrite sessions index without the cleared sessions.
            if (toClear.Count > 0)
            {
                var remaining = new List<string>();
                foreach (var s in sessions)
                {
                    if (!toClear.Contains(s))
                        remaining.Add(s);
                }
                WriteSessionIndex(remaining);
            }

            if (!isAll && cleared == 0)
                return string.Format("Session '{0}' not found. Use /nanobars profile clear all to clear everything.", sessionNameOrAll);

            return string.Format("Cleared {0} session(s).", cleared);
        }

        /// <summary>
        /// Returns a formatted list of known profiling sessions.
        /// </summary>
        public static string GetSessionListText()
        {
            var sessions = ReadSessionIndex();
            if (sessions.Count == 0)
                return "No profiling sessions found.";

            var sb = new StringBuilder();
            sb.AppendLine(string.Format("{0} session(s):", sessions.Count));
            foreach (var s in sessions)
            {
                sb.AppendLine(string.Format("  {0}", s));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Delete all files belonging to a session by reading its manifest.
        /// </summary>
        private static void DeleteSessionFiles(string prefix)
        {
            try
            {
                if (MyAPIGateway.Utilities == null) return;
                var manifestName = prefix + "NanobotProfiler.manifest";
                if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(manifestName, typeof(Mod))) return;

                var files = new List<string>();
                var reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(manifestName, typeof(Mod));
                try
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (!string.IsNullOrEmpty(line))
                            files.Add(line);
                    }
                }
                finally { reader.Close(); }

                foreach (var file in files)
                    DeleteFile(file);
                DeleteFile(manifestName);
            }
            catch { }
        }

        private static void DeleteFile(string fileName)
        {
            try
            {
                if (MyAPIGateway.Utilities.FileExistsInLocalStorage(fileName, typeof(Mod)))
                    MyAPIGateway.Utilities.DeleteFileInLocalStorage(fileName, typeof(Mod));
            }
            catch { }
        }

        private static List<string> ReadSessionIndex()
        {
            var sessions = new List<string>();
            try
            {
                if (MyAPIGateway.Utilities == null) return sessions;
                if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(SessionsIndexFileName, typeof(Mod))) return sessions;
                var reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(SessionsIndexFileName, typeof(Mod));
                try
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (!string.IsNullOrEmpty(line))
                            sessions.Add(line);
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
                if (MyAPIGateway.Utilities == null) return;
                var writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(SessionsIndexFileName, typeof(Mod));
                try
                {
                    foreach (var s in sessions)
                        writer.WriteLine(s);
                    writer.Flush();
                }
                finally
                {
                    writer.Close();
                    writer.Dispose();
                }
            }
            catch { }
        }

        private static void AppendSessionIndex()
        {
            try
            {
                var sessions = ReadSessionIndex();
                if (!string.IsNullOrEmpty(_sessionName) && !sessions.Contains(_sessionName))
                {
                    sessions.Add(_sessionName);
                    WriteSessionIndex(sessions);
                }
            }
            catch { }
        }

        private static void WriteManifest()
        {
            try
            {
                if (MyAPIGateway.Utilities == null || _writers.Count == 0) return;

                var manifestName = (_sessionPrefix ?? "") + "NanobotProfiler.manifest";
                var writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(manifestName, typeof(Mod));
                try
                {
                    var prefix = _sessionPrefix ?? "";
                    writer.WriteLine(prefix + "NanobotProfiler.Summary.log");
                    foreach (var methodName in _writers.Keys)
                    {
                        writer.WriteLine(prefix + "NanobotProfiler." + SanitizeMethodName(methodName) + ".log");
                    }
                    writer.Flush();
                }
                finally
                {
                    writer.Close();
                    writer.Dispose();
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("MethodProfiler: Failed to write manifest: " + ex.Message);
            }
        }

        private static void CloseInternal()
        {
            foreach (var entry in _writers)
            {
                try
                {
                    entry.Value.Flush();
                    entry.Value.Close();
                    entry.Value.Dispose();
                }
                catch
                {
                }
            }

            _writers.Clear();
            // Note: _methodStats is intentionally NOT cleared here.
            // Data is preserved so /nanobars profile summary can display
            // results after the session stops. Stats are cleared when a
            // new session starts (StartSession).
        }
    }
}

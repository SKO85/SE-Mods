using Sandbox.ModAPI;
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
        private const string ManifestFileName = "NanobotProfiler.manifest";
        private static readonly object _syncRoot = new object();
        private static readonly Dictionary<string, TextWriter> _writers = new Dictionary<string, TextWriter>();
        private static readonly Dictionary<string, MethodStats> _methodStats = new Dictionary<string, MethodStats>();
        private static bool _isRunning;
        private static DateTime _startedUtc;
        private static DateTime? _autoStopUtc;
        private static ulong _startedBySteamId;

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
                return string.Format("Profiling status: stopped (MinDurationMs={0}).", _minDurationMs);
            }

            var duration = DateTime.UtcNow - _startedUtc;
            var status = string.Format("Profiling status: running for {0:F1}s (MinDurationMs={1}).", duration.TotalSeconds, _minDurationMs);
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

        public static bool StartSession(out string message)
        {
            return StartSession(DefaultAutoStopSeconds, out message);
        }

        public static bool StartSession(int autoStopSeconds, out string message)
        {
            return StartSession(autoStopSeconds, 0, out message);
        }

        public static bool StartSession(int autoStopSeconds, ulong startedBySteamId, out string message)
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

                DeletePreviousLogs();
                CloseInternal();
                _methodStats.Clear();
                _minSimSpeed = float.MaxValue;
                _maxSimSpeed = float.MinValue;
                _sumSimSpeed = 0;
                _simSpeedSamples = 0;
                _isRunning = true;
                _startedBySteamId = startedBySteamId;
                _startedUtc = DateTime.UtcNow;
                _autoStopUtc = autoStopSeconds > 0 ? _startedUtc.AddSeconds(autoStopSeconds) : (DateTime?)null;
                message = autoStopSeconds > 0
                    ? string.Format("Profiling started for {0}s. It will auto-stop unless stopped manually first.", autoStopSeconds)
                    : "Profiling started. Use /nanobars profile stop when done.";
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
                CloseInternal();
                message = string.Format("Profiling done. Session duration: {0:F1}s. Logs are in local mod storage as NanobotProfiler.<MethodName>.log.", duration.TotalSeconds);
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
                CloseInternal();
                message = string.Format("Profiling auto-stopped after {0:F1}s.", duration.TotalSeconds);
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
                writer = MyAPIGateway.Utilities.WriteFileInLocalStorage("NanobotProfiler.Summary.log", typeof(Mod));
                writer.WriteLine("# Nanobot method profiler summary");
                writer.WriteLine(string.Format("# sessionSeconds={0:F1};warmupCallsPerMethod={1};generatedUtc={2:u}", sessionDuration.TotalSeconds, WarmupCallsPerMethod, DateTime.UtcNow));
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
                    writer.Flush();
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("MethodProfiler write failed: " + ex.Message);
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

                var fileName = "NanobotProfiler." + SanitizeMethodName(methodName) + ".log";
                writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(fileName, typeof(Mod));
                writer.WriteLine("# utc;ms;simSpeed;details");
                writer.Flush();
                _writers[methodName] = writer;
                return writer;
            }
        }

        private static string SanitizeMethodName(string methodName)
        {
            var sb = new StringBuilder(methodName.Length);
            foreach (var ch in methodName)
            {
                if (char.IsLetterOrDigit(ch) || ch == '.' || ch == '-' || ch == '_')
                    sb.Append(ch);
                else
                    sb.Append('_');
            }

            var sanitized = sb.ToString();
            if (sanitized.Length > 80)
                sanitized = sanitized.Substring(0, 80);

            return sanitized;
        }

        public static void Close()
        {
            lock (_syncRoot)
            {
                _isRunning = false;
                _autoStopUtc = null;
                WriteManifest();
                CloseInternal();
            }
        }

        private static void DeletePreviousLogs()
        {
            try
            {
                if (MyAPIGateway.Utilities == null) return;
                if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(ManifestFileName, typeof(Mod))) return;

                var files = new List<string>();
                var reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(ManifestFileName, typeof(Mod));
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
                finally
                {
                    reader.Close();
                }

                foreach (var file in files)
                {
                    try
                    {
                        if (MyAPIGateway.Utilities.FileExistsInLocalStorage(file, typeof(Mod)))
                        {
                            var w = MyAPIGateway.Utilities.WriteFileInLocalStorage(file, typeof(Mod));
                            w.Flush();
                            w.Close();
                            w.Dispose();
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("MethodProfiler: Failed to delete previous logs: " + ex.Message);
            }
        }

        private static void WriteManifest()
        {
            try
            {
                if (MyAPIGateway.Utilities == null || _writers.Count == 0) return;

                var writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(ManifestFileName, typeof(Mod));
                try
                {
                    writer.WriteLine("NanobotProfiler.Summary.log");
                    foreach (var methodName in _writers.Keys)
                    {
                        writer.WriteLine("NanobotProfiler." + SanitizeMethodName(methodName) + ".log");
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
            _methodStats.Clear();
        }
    }
}

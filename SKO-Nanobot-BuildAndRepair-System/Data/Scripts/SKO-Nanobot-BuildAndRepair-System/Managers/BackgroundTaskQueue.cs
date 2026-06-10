using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Utils;
using System;
using System.Collections.Generic;

namespace SKONanobotBuildAndRepairSystem.Managers
{
    /// <summary>
    /// Session-level pool that runs background actions on the SE Parallel scheduler.
    /// Workers spawn lazily on Enqueue() up to Mod.Settings.MaxBackgroundTasks; each
    /// worker drains the queue until empty, then exits. System.Threading is prohibited
    /// in the SE sandbox, so all concurrency goes through MyAPIGateway.Parallel.StartBackground.
    /// Lock is on a private object — never expose the queue itself as a lock target.
    /// </summary>
    public static class BackgroundTaskQueue
    {
        public const int MaxBackgroundTasks_Default = 4;
        public const int MaxBackgroundTasks_Max = 10;
        public const int MaxBackgroundTasks_Min = 1;

        private static readonly object _lock = new object();
        private static readonly Queue<Action> _queue = new Queue<Action>();
        private static int _runningWorkers;

        // BUG-260610.15: set at the start of world unload. Stops new enqueues and
        // makes workers drop remaining actions, so stale scan work never runs
        // against a world being torn down (or against the NEXT world via leftover
        // queue entries — statics survive unload).
        private static volatile bool _shuttingDown;

        // Cumulative stats for HUD — reset by ResetStats().
        private static int _enqueued;
        private static int _completed;
        private static int _peakRunning;

        public static int Enqueued { get { lock (_lock) { return _enqueued; } } }
        public static int Completed { get { lock (_lock) { return _completed; } } }
        public static int PeakRunning { get { lock (_lock) { return _peakRunning; } } }
        public static int RunningWorkers { get { lock (_lock) { return _runningWorkers; } } }

        public static void ResetStats()
        {
            lock (_lock)
            {
                _enqueued = 0;
                _completed = 0;
                _peakRunning = _runningWorkers;
            }
        }

        /// <summary>
        /// BUG-260610.15: called first thing in Mod.UnloadData, before the drain wait.
        /// </summary>
        public static void BeginShutdown()
        {
            lock (_lock)
            {
                _shuttingDown = true;
                _queue.Clear();
            }
        }

        /// <summary>
        /// BUG-260610.15: re-arms the queue for the next session (end of UnloadData).
        /// </summary>
        public static void Reset()
        {
            lock (_lock)
            {
                _queue.Clear();
                _shuttingDown = false;
            }
        }

        public static void Enqueue(Action action)
        {
            lock (_lock)
            {
                if (_shuttingDown) return;
                _queue.Enqueue(action);
                _enqueued++;
                if (_runningWorkers < Mod.Settings.MaxBackgroundTasks)
                {
                    _runningWorkers++;
                    if (_runningWorkers > _peakRunning) _peakRunning = _runningWorkers;
                    MyAPIGateway.Parallel.StartBackground(WorkerLoop);
                }
            }
        }

        private static void WorkerLoop()
        {
            try
            {
                while (true)
                {
                    Action pendingAction = null;
                    lock (_lock)
                    {
                        // BUG-260610.15: stop picking up work once unload began.
                        if (!_shuttingDown && _queue.Count > 0)
                        {
                            pendingAction = _queue.Dequeue();
                        }
                        if (pendingAction == null)
                        {
                            _runningWorkers--;
                            break;
                        }
                    }
                    if (pendingAction != null)
                    {
                        try
                        {
                            pendingAction();
                        }
                        catch (Exception ex)
                        {
                            // BUG-260610.15: was a bare catch — recurring task failures
                            // (e.g. a scan aborting on a systematically bad candidate)
                            // were completely invisible while target lists silently
                            // stayed stale.
                            try { Logging.Instance.Write(Logging.Level.Error, "BackgroundTaskQueue: task failed: {0}", ex); } catch { }
                        }
                        lock (_lock) { _completed++; }
                    }
                }
            }
            catch
            {
                lock (_lock)
                {
                    _runningWorkers--;
                }
            }
        }
    }
}

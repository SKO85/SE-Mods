using Sandbox.ModAPI;
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

        public static void Enqueue(Action action)
        {
            lock (_lock)
            {
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
                        if (_queue.Count > 0)
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
                        catch { }
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

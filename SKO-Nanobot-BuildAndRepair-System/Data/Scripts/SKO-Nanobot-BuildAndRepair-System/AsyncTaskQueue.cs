using Sandbox.ModAPI;
using System;
using System.Collections.Generic;

namespace SKONanobotBuildAndRepairSystem
{
    public static class AsyncTaskQueue
    {
        private static readonly Queue<Action> _taskQueue = new Queue<Action>();
        private static readonly object _lock = new object();
        private static int _runningTasks = 0;

        public static void Enqueue(Action action)
        {
            if (action == null) return;

            lock (_lock)
            {
                _taskQueue.Enqueue(action);
                TryRunNext();
            }
        }

        private static void TryRunNext()
        {
            if (_runningTasks >= NanobotBuildAndRepairSystemMod.Settings.MaxBackgroundTasks || _taskQueue.Count == 0)
                return;

            var task = _taskQueue.Dequeue();
            _runningTasks++;

            MyAPIGateway.Parallel.StartBackground(() =>
            {
                try
                {
                    task();
                }
                catch (Exception ex)
                {
                    Logging.Instance?.Error("AsyncTaskQueue Error: {0}", ex);
                }
                finally
                {
                    lock (_lock)
                    {
                        _runningTasks--;
                        TryRunNext();
                    }
                }
            });
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _taskQueue.Clear();
            }
        }
    }
}
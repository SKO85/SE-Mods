using Sandbox.ModAPI;
using System;
using System.Collections.Concurrent;

namespace SKONanobotBuildAndRepairSystem
{
    public static class AsyncTaskQueue
    {
        private static readonly ConcurrentQueue<Action> _taskQueue = new ConcurrentQueue<Action>();
        private static int _runningTasks = 0;

        public static void Enqueue(Action action)
        {
            if (action == null) return;

            lock(_taskQueue)
            {
                _taskQueue.Enqueue(action);
            }            
            TryRunNext();            
        }

        private static void TryRunNext()
        {
            if (_runningTasks >= NanobotBuildAndRepairSystemMod.Settings.MaxBackgroundTasks || _taskQueue.Count == 0)
                return;

            Action task = null;
            lock (_taskQueue)
            {
                _taskQueue.TryDequeue(out task);
            }
            _runningTasks++;

            MyAPIGateway.Parallel.StartBackground(() =>
            {
                try
                {
                    if (task != null)
                    {
                        task();
                    }
                }
                catch (Exception ex)
                {
                    Logging.Instance?.Error("AsyncTaskQueue Error: {0}", ex);
                }
                finally
                {
                    if (task != null)
                    {
                        _runningTasks--;
                    }
                    TryRunNext();
                }
            });
        }

        public static void Clear()
        {
            lock (_taskQueue)
            {
                Action item;
                while (_taskQueue.TryDequeue(out item)) { }
            }
        }
    }
}
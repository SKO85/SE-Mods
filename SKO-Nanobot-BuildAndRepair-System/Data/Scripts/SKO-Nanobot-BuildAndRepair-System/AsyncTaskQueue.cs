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

            bool shouldStartTask = false;

            lock (_lock)
            {
                _taskQueue.Enqueue(action);

                // Start a task only if allowed by limit
                if (_runningTasks < NanobotBuildAndRepairSystemMod.Settings.MaxBackgroundTasks)
                {
                    _runningTasks++;
                    shouldStartTask = true;
                }
            }

            if (shouldStartTask)
                StartNextTask();
        }

        private static void StartNextTask()
        {
            Action taskToRun = null;

            lock (_lock)
            {
                if (_taskQueue.Count > 0)
                {
                    taskToRun = _taskQueue.Dequeue();
                }
                else
                {
                    // Safe decrement: never let _runningTasks go below zero
                    _runningTasks = Math.Max(0, _runningTasks - 1);
                    return;
                }
            }

            MyAPIGateway.Parallel.StartBackground(() =>
            {
                try
                {
                    taskToRun();
                }
                catch (Exception ex)
                {
                    Logging.Instance?.Error("AsyncTaskQueue Error: {0}", ex);
                }
                finally
                {
                    StartNextTask();
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

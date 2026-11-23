using NSubstitute;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Cluster;
using SKONanobotBuildAndRepairSystem.Managers;
using System;
using System.Threading;
using VRage.Game.ModAPI;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-BUDGET-011 — async handling: BackgroundTaskQueue (all background scans run on it) under concurrent load, the
    // worker cap, failing tasks, world unload; the cluster scan result handover between threads. Test code may use
    // System.Threading; the mod uses MyAPIGateway.Parallel, faked here with real threads.
    public class AsyncHandlingTests : ModTestBase
    {
        public AsyncHandlingTests()
        {
            var parallel = Substitute.For<IMyParallelTask>();
            parallel.When(p => p.StartBackground(Arg.Any<Action>())).Do(ci =>
            {
                var work = ci.Arg<Action>();
                var t = new Thread(() => work()) { IsBackground = true };
                t.Start();
            });
            MyAPIGateway.Parallel = parallel;
            BackgroundTaskQueue.Reset();
            BackgroundTaskQueue.ResetStats();
        }

        private static void WaitDrained(int expectedCompleted, int timeoutMs = 10000)
        {
            var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < until)
            {
                if (BackgroundTaskQueue.Completed >= expectedCompleted && BackgroundTaskQueue.RunningWorkers == 0) return;
                Thread.Sleep(5);
            }
            Assert.Fail("queue did not drain: completed " + BackgroundTaskQueue.Completed + "/" + expectedCompleted
                + ", workers " + BackgroundTaskQueue.RunningWorkers);
        }

        [Fact]
        public void EveryTaskRunsExactlyOnce_UnderConcurrentEnqueue()
        {
            Mod.Settings.MaxBackgroundTasks = 4;
            const int producers = 8, perProducer = 250, total = producers * perProducer;
            var runs = new int[total];
            var threads = new Thread[producers];
            for (int p = 0; p < producers; p++)
            {
                var offset = p * perProducer;
                threads[p] = new Thread(() =>
                {
                    for (int i = 0; i < perProducer; i++)
                    {
                        var id = offset + i;
                        BackgroundTaskQueue.Enqueue(() => Interlocked.Increment(ref runs[id]));
                    }
                });
                threads[p].Start();
            }
            foreach (var t in threads) t.Join();
            WaitDrained(total);

            for (int i = 0; i < total; i++) Assert.Equal(1, runs[i]);
            Assert.Equal(total, BackgroundTaskQueue.Enqueued);
            Assert.Equal(total, BackgroundTaskQueue.Completed);
            Assert.Equal(0, BackgroundTaskQueue.RunningWorkers);
        }

        [Fact]
        public void NeverMoreWorkersThanMaxBackgroundTasks()
        {
            Mod.Settings.MaxBackgroundTasks = 3;
            int active = 0, maxActive = 0;
            const int total = 60;
            for (int i = 0; i < total; i++)
            {
                BackgroundTaskQueue.Enqueue(() =>
                {
                    var now = Interlocked.Increment(ref active);
                    int seen;
                    while ((seen = Volatile.Read(ref maxActive)) < now && Interlocked.CompareExchange(ref maxActive, now, seen) != seen) { }
                    Thread.Sleep(2);
                    Interlocked.Decrement(ref active);
                });
            }
            WaitDrained(total);
            Assert.InRange(maxActive, 1, 3);
            Assert.InRange(BackgroundTaskQueue.PeakRunning, 1, 3);
        }

        [Fact]
        public void AFailingTask_DoesNotStopTheQueue()
        {
            Mod.Settings.MaxBackgroundTasks = 2;
            var ok = 0;
            for (int i = 0; i < 40; i++)
            {
                if (i % 4 == 0) BackgroundTaskQueue.Enqueue(() => { throw new InvalidOperationException("scan failed"); });
                else BackgroundTaskQueue.Enqueue(() => Interlocked.Increment(ref ok));
            }
            WaitDrained(40);
            Assert.Equal(30, ok);
            Assert.Equal(0, BackgroundTaskQueue.RunningWorkers);
        }

        [Fact]
        public void Unload_DropsQueuedWork_AndRejectsNew_UntilReset()
        {
            Mod.Settings.MaxBackgroundTasks = 1;
            var gate = new ManualResetEventSlim(false);
            var started = new ManualResetEventSlim(false);
            var ran = 0;
            BackgroundTaskQueue.Enqueue(() => { started.Set(); gate.Wait(5000); Interlocked.Increment(ref ran); });
            for (int i = 0; i < 10; i++) BackgroundTaskQueue.Enqueue(() => Interlocked.Increment(ref ran));
            Assert.True(started.Wait(5000), "first task never started");

            BackgroundTaskQueue.BeginShutdown();
            BackgroundTaskQueue.Enqueue(() => Interlocked.Increment(ref ran)); // rejected
            gate.Set();
            WaitDrained(1);
            Thread.Sleep(50);
            Assert.Equal(1, ran); // only the task already running finished

            BackgroundTaskQueue.Reset();
            BackgroundTaskQueue.Enqueue(() => Interlocked.Increment(ref ran));
            WaitDrained(2);
            Assert.Equal(2, ran);
        }

        [Fact]
        public void WorkersExit_WhenIdle_AndRestartOnNewWork()
        {
            Mod.Settings.MaxBackgroundTasks = 4;
            var ran = 0;
            for (int i = 0; i < 10; i++) BackgroundTaskQueue.Enqueue(() => Interlocked.Increment(ref ran));
            WaitDrained(10);
            Assert.Equal(0, BackgroundTaskQueue.RunningWorkers);
            BackgroundTaskQueue.Enqueue(() => Interlocked.Increment(ref ran));
            WaitDrained(11);
            Assert.Equal(11, ran);
        }

        [Fact]
        public void ClusterResult_ReadersNeverSeeAHalfBuiltResult()
        {
            // The coordinator builds a result off-thread and swaps it in; members read it concurrently. A reader must
            // always see a complete result: its candidate count matches the count encoded in its timestamp.
            var cluster = new ScanCluster("k");
            var stop = 0;
            var bad = 0;
            var reads = 0;
            var readers = new Thread[4];
            for (int r = 0; r < readers.Length; r++)
            {
                readers[r] = new Thread(() =>
                {
                    while (Volatile.Read(ref stop) == 0)
                    {
                        var res = cluster.GetResult();
                        if (res == null) continue;
                        if (res.WeldCandidates.Count != (int)res.Timestamp.TotalMilliseconds) Interlocked.Increment(ref bad);
                        Interlocked.Increment(ref reads);
                    }
                });
                readers[r].Start();
            }
            for (int n = 1; n <= 2000; n++)
            {
                var result = new ScanClusterResult();
                for (int i = 0; i < n % 50; i++) result.WeldCandidates.Add(default(ClusterTargetCandidate));
                result.Timestamp = TimeSpan.FromMilliseconds(n % 50);
                cluster.SetResult(result);
            }
            Thread.Sleep(20);
            Volatile.Write(ref stop, 1);
            foreach (var t in readers) t.Join();
            Assert.Equal(0, bad);
            Assert.True(reads > 0);
        }
    }
}

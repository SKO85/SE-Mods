using System.Collections.Generic;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// One catalogue scenario (docs/plan/scenarios) as code. All methods run on the main thread
    /// from TestRunner; none may block. Setup spawns, IsReady/IsComplete are polled every 30
    /// frames, Sample runs every tick during Run and must stay O(rig BaRs).
    /// </summary>
    internal interface ITestScenario
    {
        string Id { get; }
        string Description { get; }
        int TimeoutSeconds { get; }
        /// <summary>Max seconds in WaitReady (setup until the measured run may start).</summary>
        int ReadyTimeoutSeconds { get; }
        /// <summary>Profiler methods whose steady-state stats are recorded as metrics.</summary>
        IList<string> PerfMethods { get; }

        /// <summary>Checked before Setup: null to run, else why it cannot run in this world (reported as skipped).</summary>
        string SkipReason(TestContext ctx);

        void Setup(TestContext ctx);
        bool IsReady(TestContext ctx);
        bool IsComplete(TestContext ctx);
        void Sample(TestContext ctx);
        void Assert(TestContext ctx, TestResult result);
        void Teardown(TestContext ctx);
    }
}

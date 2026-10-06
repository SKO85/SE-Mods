using System.Collections.Generic;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// Defaults shared by all scenarios: 120 s timeout, readiness = all rig BaRs registered
    /// and scanned once, a one-shot Configure hook once BaRs are resolvable, teardown = close
    /// everything the context spawned.
    /// </summary>
    internal abstract class TestScenarioBase : ITestScenario
    {
        private static readonly string[] DefaultPerfMethods =
        {
            "ServerDoWeld",
            "ServerTryWelding",
            "ServerTryWeldingGrindingCollecting",
            "UpdateBeforeSimulation10_100",
            "AsyncClusterScan",
            "ApplyClusterResultToSelf",
            "RebuildMissingComponentsAggregate"
        };

        private bool _configured;

        public abstract string Id { get; }
        public abstract string Description { get; }
        public virtual int TimeoutSeconds { get { return 120; } }
        public virtual IList<string> PerfMethods { get { return DefaultPerfMethods; } }

        public abstract void Setup(TestContext ctx);

        /// <summary>Called once, the first IsReady poll after every rig's BaRs are resolvable.</summary>
        protected virtual void Configure(TestContext ctx) { }

        public virtual bool IsReady(TestContext ctx)
        {
            if (!ctx.TryResolveRigs()) return false;
            if (!_configured)
            {
                _configured = true;
                Configure(ctx);
                // Configure may change settings that trigger a rescan; wait for that scan.
                ctx.MarkSetupTime();
                return false;
            }
            return ctx.AllBarsReadyAndScanned();
        }

        public abstract bool IsComplete(TestContext ctx);

        public virtual void Sample(TestContext ctx) { }

        public abstract void Assert(TestContext ctx, TestResult result);

        public virtual void Teardown(TestContext ctx)
        {
            ctx.CloseSpawned();
        }

        /// <summary>Runner calls this before Setup so a scenario instance can be re-run (xN).</summary>
        internal void ResetForRun()
        {
            _configured = false;
        }
    }
}

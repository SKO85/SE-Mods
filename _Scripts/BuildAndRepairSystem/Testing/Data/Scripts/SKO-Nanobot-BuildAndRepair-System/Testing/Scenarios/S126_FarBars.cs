using SKONanobotBuildAndRepairSystem.Models;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// FEAT-261123.8 / SCN-BUDGET-010: 5 BaRs repairing 20 damaged blocks 10 km away from the caller (beyond the sync
    /// distance, so the client does not have them loaded). With a remote player (Torch): no state message goes to that
    /// player (`net.state.farSkipped` > 0, state wire bytes 0) while the BaRs work normally. Single player: work only.
    /// </summary>
    internal sealed class S126_FarBars : TestScenarioBase
    {
        private const double Window = 60.0;
        private TestRig _rig;

        public override string Id { get { return "S126"; } }
        public override string Description { get { return "Sync: BaRs working 10 km from every player send them no state"; } }
        public override int TimeoutSeconds { get { return 90; } }

        public override void Setup(TestContext ctx)
        {
            ctx.SetModSettings(s => { s.Welder.WorkSpeed = 3; });
            _rig = ctx.SpawnRig(new RigSpec { Name = "S126", Bars = 5, DamagedArmor = 20, Offset = new Vector3D(0, 0, -10000) });
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.StockCargo(_rig, "SteelPlate", 1000);
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.Grids;
                s.WorkMode = WorkModes.WeldOnly;
            });
        }

        public override bool IsComplete(TestContext ctx)
        {
            return ctx.CountFullIntegrity(_rig.Targets) == _rig.Targets.Count || ctx.RunSeconds >= Window;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var welded = ctx.CountFullIntegrity(_rig.Targets);
            ctx.Metric("welded", (long)welded);
            ctx.Metric("distanceToCallerKm", 10.0);
            ctx.Check(welded == _rig.Targets.Count, "welded=" + welded + "/" + _rig.Targets.Count + " (far BaRs must keep working)");
            if (ctx.RemotePlayers() == 0)
            {
                ctx.Metric("netChecks", "skipped (no remote player connected)");
                return;
            }
            long m, b, w; int mx;
            ctx.NetSent(40104, out m, out b, out w, out mx);
            ctx.Metric("stateWireBytes", w);
            ctx.Check(w == 0, "far BaRs sent " + w + " bytes of state to players 10 km away");
        }
    }
}

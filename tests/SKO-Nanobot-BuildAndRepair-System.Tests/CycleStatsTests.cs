using SKONanobotBuildAndRepairSystem.Handlers;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-BUDGET-008 — FEAT-261123.4: every ready BaR cycle is counted under one outcome; idle cycles get the most
    // specific reason (no components > refused > budget > failure skip > grid limit > claimed > exhausted).
    public class CycleStatsTests
    {
        [Fact]
        public void WeldIdleReason_MostSpecificFirst()
        {
            Assert.Equal(CycleOutcome.NoComponents, CycleStats.WeldIdleReason(1, 1, true, 5, 5, 5, true));
            Assert.Equal(CycleOutcome.Refused, CycleStats.WeldIdleReason(0, 2, true, 5, 5, 5, true));
            Assert.Equal(CycleOutcome.BudgetDeferred, CycleStats.WeldIdleReason(0, 0, true, 5, 5, 5, true));
            Assert.Equal(CycleOutcome.FailCooldown, CycleStats.WeldIdleReason(0, 0, false, 5, 5, 5, true));
            Assert.Equal(CycleOutcome.GridLimit, CycleStats.WeldIdleReason(0, 0, false, 0, 5, 5, true));
            Assert.Equal(CycleOutcome.AllClaimed, CycleStats.WeldIdleReason(0, 0, false, 0, 0, 5, true));
            Assert.Equal(CycleOutcome.Exhausted, CycleStats.WeldIdleReason(0, 0, false, 0, 0, 0, true));
            Assert.Equal(CycleOutcome.Other, CycleStats.WeldIdleReason(0, 0, false, 0, 0, 0, false));
        }

        [Fact]
        public void GrindIdleReason_MostSpecificFirst()
        {
            Assert.Equal(CycleOutcome.Refused, CycleStats.GrindIdleReason(1, true, true, 5, 5, true));
            Assert.Equal(CycleOutcome.Protected, CycleStats.GrindIdleReason(0, true, true, 5, 5, true));
            Assert.Equal(CycleOutcome.BudgetDeferred, CycleStats.GrindIdleReason(0, false, true, 5, 5, true));
            Assert.Equal(CycleOutcome.GridLimit, CycleStats.GrindIdleReason(0, false, false, 5, 5, true));
            Assert.Equal(CycleOutcome.AllClaimed, CycleStats.GrindIdleReason(0, false, false, 0, 5, true));
            Assert.Equal(CycleOutcome.Exhausted, CycleStats.GrindIdleReason(0, false, false, 0, 0, true));
            Assert.Equal(CycleOutcome.Other, CycleStats.GrindIdleReason(0, false, false, 0, 0, false));
        }

        [Fact]
        public void Record_CountsPerOutcome_SnapshotIsACopy()
        {
            CycleStats.ResetForTests();
            CycleStats.Record(CycleOutcome.Worked);
            CycleStats.Record(CycleOutcome.Worked);
            CycleStats.Record(CycleOutcome.GridLimit);
            var snap = CycleStats.Snapshot();
            Assert.Equal(2, snap[(int)CycleOutcome.Worked]);
            Assert.Equal(1, snap[(int)CycleOutcome.GridLimit]);
            CycleStats.Record(CycleOutcome.Worked);
            Assert.Equal(2, snap[(int)CycleOutcome.Worked]);
            CycleStats.ResetForTests();
        }
    }
}

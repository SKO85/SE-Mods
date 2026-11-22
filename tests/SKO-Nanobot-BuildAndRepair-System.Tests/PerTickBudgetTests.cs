using SKONanobotBuildAndRepairSystem.Managers;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-BUDGET-002 / SCN-BUDGET-003 / SCN-GRIND-004 — global per-tick weld/grind/dismount budgets.
    public class PerTickBudgetTests : ModTestBase
    {
        [Fact]
        public void ClaimsUpToMax_ThenRefuses()
        {
            var budget = new PerTickBudget(3);
            Frame = 10;
            Assert.True(budget.TryClaim());
            Assert.True(budget.TryClaim());
            Assert.True(budget.TryClaim());
            Assert.False(budget.TryClaim());
        }

        [Fact]
        public void ResetsWhenFrameAdvances_PeakKeptUntilResetStats()
        {
            var budget = new PerTickBudget(2);
            Frame = 10;
            budget.TryClaim();
            budget.TryClaim();
            Assert.False(budget.TryClaim());

            Frame = 11;
            Assert.True(budget.TryClaim());
            Assert.Equal(2, budget.PeakUsed);

            budget.ResetStats();
            Assert.True(budget.PeakUsed <= 1);
        }

        [Fact]
        public void MsCapWins_WhenCountBudgetRemains()
        {
            var budget = new PerTickBudget(() => 20, () => 8.0);
            Frame = 10;
            Assert.True(budget.TryClaim());
            budget.ReportTime(8.0);
            Assert.False(budget.TryClaim());

            Frame = 11; // ms accumulator resets with the frame
            Assert.True(budget.TryClaim());
        }

        [Fact]
        public void Refund_ReturnsOneSlot_NotBelowZero()
        {
            var budget = new PerTickBudget(1);
            Frame = 10;
            Assert.True(budget.TryClaim());
            Assert.False(budget.TryClaim());
            budget.Refund();
            Assert.True(budget.TryClaim());   // BUG-260610.35: deferred work returns its slot

            budget.Refund();
            budget.Refund();                  // extra refunds must not mint slots
            Assert.True(budget.TryClaim());
            Assert.False(budget.TryClaim());
        }

        [Fact]
        public void EffectiveMax_FollowsResolver()
        {
            var max = 2;
            var budget = new PerTickBudget(() => max, () => 100.0);
            Assert.Equal(2, budget.EffectiveMax);
            max = 5;
            Assert.Equal(5, budget.EffectiveMax);
        }
    }
}

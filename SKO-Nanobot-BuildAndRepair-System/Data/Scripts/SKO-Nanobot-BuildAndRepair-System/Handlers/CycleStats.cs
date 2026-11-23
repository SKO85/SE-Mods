namespace SKONanobotBuildAndRepairSystem.Handlers
{
    /// <summary>FEAT-261123.4: what a ready BaR's update cycle ended with.</summary>
    public enum CycleOutcome
    {
        Worked = 0,          // welded, ground or collected
        WaitingDelivery,     // nanobot transport in flight
        NoTargets,           // nothing listed
        InventoryFull,       // paused until the BaR can push / empty
        NoComponents,        // weld: the pull found nothing for the chosen block
        Refused,             // the game / a protection mod refused the weld or grind
        BudgetDeferred,      // global weld / grind slot or ms budget used up this tick
        FailCooldown,        // listed blocks parked after earlier failures
        GridLimit,           // MaxSystemsPerTargetGrid reached on the listed grids
        AllClaimed,          // listed blocks claimed by other BaRs
        Protected,           // grind target in a shield / safe zone
        Exhausted,           // nothing grindable / weldable left in the list (exhausted skip, none weldable)
        NoPower,             // not enough power for the work
        OutOfArea,           // the chosen target left the working area (dropped)
        Other,
        Count
    }

    /// <summary>
    /// FEAT-261123.4: counts cycle outcomes of all BaRs (main thread only, one array increment per cycle). Read by the
    /// test harness (share per outcome) to see why BaRs did nothing.
    /// </summary>
    public static class CycleStats
    {
        private static readonly long[] _counts = new long[(int)CycleOutcome.Count];

        public static void Record(CycleOutcome outcome)
        {
            _counts[(int)outcome]++;
        }

        public static long[] Snapshot()
        {
            var copy = new long[_counts.Length];
            for (int i = 0; i < copy.Length; i++) copy[i] = _counts[i];
            return copy;
        }

        public static void ResetForTests()
        {
            for (int i = 0; i < _counts.Length; i++) _counts[i] = 0;
        }

        /// <summary>Why a weld attempt that neither welded nor started a delivery did nothing (most specific first).</summary>
        public static CycleOutcome WeldIdleReason(int componentFailures, int refused, bool budgetDeferred, int failCooldown, int gridLimit, int claimed, bool exhausted,
            bool noPower = false, bool outOfArea = false, int checkedNotWelded = 0)
        {
            if (noPower) return CycleOutcome.NoPower;
            if (componentFailures > 0) return CycleOutcome.NoComponents;
            if (refused > 0) return CycleOutcome.Refused;
            if (budgetDeferred) return CycleOutcome.BudgetDeferred;
            if (failCooldown > 0) return CycleOutcome.FailCooldown;
            if (gridLimit > 0) return CycleOutcome.GridLimit;
            if (claimed > 0) return CycleOutcome.AllClaimed;
            if (outOfArea) return CycleOutcome.OutOfArea;
            if (exhausted || checkedNotWelded > 0) return CycleOutcome.Exhausted;
            return CycleOutcome.Other;
        }

        /// <summary>Why a grind attempt did nothing (most specific first).</summary>
        public static CycleOutcome GrindIdleReason(int refused, bool protectedTarget, bool budgetDeferred, int gridLimit, int claimed, bool exhausted,
            bool noPower = false, int outOfArea = 0)
        {
            if (noPower) return CycleOutcome.NoPower;
            if (refused > 0) return CycleOutcome.Refused;
            if (protectedTarget) return CycleOutcome.Protected;
            if (budgetDeferred) return CycleOutcome.BudgetDeferred;
            if (gridLimit > 0) return CycleOutcome.GridLimit;
            if (claimed > 0) return CycleOutcome.AllClaimed;
            if (outOfArea > 0) return CycleOutcome.OutOfArea;
            if (exhausted) return CycleOutcome.Exhausted;
            return CycleOutcome.Other;
        }
    }
}

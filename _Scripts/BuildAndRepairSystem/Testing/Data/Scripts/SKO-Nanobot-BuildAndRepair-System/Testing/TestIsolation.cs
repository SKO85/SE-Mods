using SKONanobotBuildAndRepairSystem.Caches;
using SKONanobotBuildAndRepairSystem.Cluster;
using SKONanobotBuildAndRepairSystem.Handlers;

namespace SKONanobotBuildAndRepairSystem.Testing
{
    /// <summary>
    /// Static handler/cache reset between scenarios so one run cannot leak claims, cooldowns,
    /// cluster membership or counters into the next. Mod.GridSystemCount is deliberately left
    /// alone: it is maintained by the lock-on setters and closing the spawned grids decrements it.
    /// </summary>
    internal static class TestIsolation
    {
        public static void Reset()
        {
            try { BlockSystemAssigningHandler.Clear(); } catch { }
            try { BlockFailureCooldownHandler.Clear(); } catch { }
            try { RazeQueueHandler.Clear(); } catch { }
            try { SkeletonResetHandler.Clear(); } catch { }
            try { ScanClusterCoordinator.Clear(); } catch { }
            try { GridScanCache.Clear(); } catch { }
            try { SharedGridBlockCache.Clear(); } catch { }
            try { SharedEntityCache.Clear(); } catch { }
            try { FriendlyRelationsHandler.Clear(); } catch { }
            try { Mod.ResetGrindBudgetStats(); } catch { }
            try { Mod.ResetWeldBudgetStats(); } catch { }
            try { Mod.ResetTickCostStats(); } catch { }
            try { Mod.ResetSyncStats(); } catch { }
            try { Mod.ResetBackgroundTaskStats(); } catch { }
            Mod.SimSpeedOverride = null;
        }
    }
}

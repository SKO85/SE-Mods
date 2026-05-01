using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Caches;
using SKONanobotBuildAndRepairSystem.Handlers;
using SKONanobotBuildAndRepairSystem.Helpers;
using SKONanobotBuildAndRepairSystem.Profiling;
using System;

namespace SKONanobotBuildAndRepairSystem.Managers
{
    /// <summary>
    /// Server-side time-driven background tasks. Tick(now) is called from
    /// Mod.UpdateBeforeSimulation each frame; each task fires on its own interval
    /// and dispatches its Work onto MyAPIGateway.Parallel.StartBackground.
    /// Initial last-run timestamps are TimeSpan.Zero (NOT MinValue, which overflows
    /// the now.Subtract(...) comparison and aborts UpdateBeforeSimulation).
    /// </summary>
    public static class PeriodicMaintenanceScheduler
    {
        private struct PeriodicTask
        {
            public TimeSpan Interval;
            public TimeSpan LastRun;
            public Action Work;
        }

        // Periodic ownership cache refresh (10 s).
        private static PeriodicTask _ownership = new PeriodicTask
        {
            Interval = TimeSpan.FromSeconds(10),
            LastRun = TimeSpan.Zero,
            Work = () => GridOwnershipCacheHandler.Update()
        };

        // Safe-zone cache cleanup (6 s). BUG-143: was GetSafeZones() which did a full
        // MyAPIGateway.Entities.GetEntities walk (1-3 ms per tick). The walk was redundant
        // because OnEntityAdd/OnEntityRemove already maintain the Zones dict in real time.
        // CleanupSafeZones does just the cache cleanups (~µs) and the stale-entry guard.
        private static PeriodicTask _safeZone = new PeriodicTask
        {
            Interval = TimeSpan.FromSeconds(6),
            LastRun = TimeSpan.Zero,
            Work = () => SafeZoneHandler.CleanupSafeZones()
        };

        // TTL-cache cleanup batch (2 min).
        private static PeriodicTask _ttlCleanup = new PeriodicTask
        {
            Interval = TimeSpan.FromMinutes(2),
            LastRun = TimeSpan.Zero,
            Work = () =>
            {
                try { InventoryHelper.Cleanup(); } catch { }
                try { BlockPriorityHandling.GetItemKeyCache.CleanupExpired(); } catch { }
                try { BlockSystemAssigningHandler.Cleanup(); } catch { }
                try { BlockFailureCooldownHandler.Cleanup(); } catch { }
                try { SharedGridBlockCache.Cleanup(); } catch { }
                try { SharedEntityCache.Cleanup(); } catch { }
            }
        };

        // BUG-123: friendly-BaR cache rebuild + BUG-125: defensive profiler flush
        // piggy-backed on the same 5 s cadence. Bounds profiler-buffer loss to ~5 s on
        // hard crash even when a profile session never calls StopSession.
        private static PeriodicTask _friendlyAndProfiler = new PeriodicTask
        {
            Interval = TimeSpan.FromSeconds(5),
            LastRun = TimeSpan.Zero,
            Work = () =>
            {
                try { FriendlyRelationsHandler.Rebuild(); } catch { }
                try { MethodProfiler.FlushAll(); } catch { }
            }
        };

        /// <summary>
        /// Called once per server-side UpdateBeforeSimulation. Each task whose interval
        /// has elapsed dispatches its Work onto a background thread.
        /// </summary>
        public static void Tick(TimeSpan now)
        {
            TryFire(ref _ownership, now);
            TryFire(ref _safeZone, now);
            TryFire(ref _ttlCleanup, now);
            TryFire(ref _friendlyAndProfiler, now);
        }

        private static void TryFire(ref PeriodicTask task, TimeSpan now)
        {
            if (now.Subtract(task.LastRun) < task.Interval) return;
            task.LastRun = now;
            var work = task.Work;
            MyAPIGateway.Parallel.StartBackground(() =>
            {
                try { work(); } catch { }
            });
        }
    }
}

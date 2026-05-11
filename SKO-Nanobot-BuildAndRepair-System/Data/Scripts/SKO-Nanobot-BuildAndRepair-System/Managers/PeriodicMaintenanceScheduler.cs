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
            /// <summary>
            /// When true, Work runs inline on the main thread instead of being
            /// dispatched to MyAPIGateway.Parallel.StartBackground. Required for
            /// tasks that touch engine APIs not safe off the main thread
            /// (faction/relation, entity tree, projector state, etc.).
            /// </summary>
            public bool MainThread;
        }

        // Periodic ownership cache refresh (10 s). MUST run on the main thread —
        // RefreshExpiredEntries touches MyAPIGateway.Entities.TryGetEntityById and
        // reads grid state (.Closed, .BigOwners), none of which is safe to call
        // from MyAPIGateway.Parallel.StartBackground.
        private static PeriodicTask _ownership = new PeriodicTask
        {
            Interval = TimeSpan.FromSeconds(10),
            LastRun = TimeSpan.Zero,
            MainThread = true,
            Work = () => GridOwnershipCacheHandler.Update()
        };

        // BUG-143: safe-zone cache cleanup (6 s); skips the redundant GetSafeZones
        // walk. MUST run on the main thread — CleanupStaleZones reads
        // MySafeZone.MarkedForClose / .Closed (engine state) and shares the
        // _staleZoneKeys scratch buffer with main-thread maintenance paths.
        private static PeriodicTask _safeZone = new PeriodicTask
        {
            Interval = TimeSpan.FromSeconds(6),
            LastRun = TimeSpan.Zero,
            MainThread = true,
            Work = () => SafeZoneHandler.CleanupSafeZones()
        };

        // TTL-cache cleanup batch (2 min) — for caches whose TTL semantics tolerate
        // long lingering of expired entries (TryGet filters them at read time).
        private static PeriodicTask _ttlCleanup = new PeriodicTask
        {
            Interval = TimeSpan.FromMinutes(2),
            LastRun = TimeSpan.Zero,
            Work = () =>
            {
                try { InventoryHelper.Cleanup(); } catch { }
                try { BlockPriorityHandling.GetItemKeyCache.CleanupExpired(); } catch { }
                try { BlockFailureCooldownHandler.Cleanup(); } catch { }
                try { SharedGridBlockCache.Cleanup(); } catch { }
                try { SharedEntityCache.Cleanup(); } catch { }
            }
        };

        // BUG-260511.18: BlockSystemAssigningHandler cleanup runs frequently (5 s)
        // so the live AssignmentCount displayed in the debug HUD ("BlockAssigns")
        // reflects actual live claims rather than 2-minute-stale entries. TtlCache
        // TryGet already filters expired entries on read, but Count is the raw
        // dictionary size — without frequent cleanup it climbs unboundedly during
        // active grinding and looks like a leak to admins watching the panel.
        private static PeriodicTask _assignmentCleanup = new PeriodicTask
        {
            Interval = TimeSpan.FromSeconds(5),
            LastRun = TimeSpan.Zero,
            Work = () => { try { BlockSystemAssigningHandler.Cleanup(); } catch { } }
        };

        // BUG-123: friendly-BaR cache rebuild (5 s cadence). MUST run on the main
        // thread — IMyEntity.GetUserRelationToOwner touches engine faction state
        // and is not safe to call from MyAPIGateway.Parallel.StartBackground.
        private static PeriodicTask _friendlyRebuild = new PeriodicTask
        {
            Interval = TimeSpan.FromSeconds(5),
            LastRun = TimeSpan.Zero,
            MainThread = true,
            Work = () => FriendlyRelationsHandler.Rebuild()
        };

        // BUG-125: profiler flush (5 s cadence). Plain .NET file I/O — safe off
        // the main thread, kept on background to avoid blocking the sim tick.
        private static PeriodicTask _profilerFlush = new PeriodicTask
        {
            Interval = TimeSpan.FromSeconds(5),
            LastRun = TimeSpan.Zero,
            Work = () => MethodProfiler.FlushAll()
        };

        /// <summary>
        /// Called once per server-side UpdateBeforeSimulation. Each task whose interval
        /// has elapsed runs either inline (MainThread=true) or on a background thread.
        /// </summary>
        public static void Tick(TimeSpan now)
        {
            TryFire(ref _ownership, now);
            TryFire(ref _safeZone, now);
            TryFire(ref _ttlCleanup, now);
            TryFire(ref _assignmentCleanup, now);
            TryFire(ref _friendlyRebuild, now);
            TryFire(ref _profilerFlush, now);
        }

        private static void TryFire(ref PeriodicTask task, TimeSpan now)
        {
            if (now.Subtract(task.LastRun) < task.Interval) return;
            task.LastRun = now;
            var work = task.Work;
            if (task.MainThread)
            {
                try { work(); } catch { }
                return;
            }
            MyAPIGateway.Parallel.StartBackground(() =>
            {
                try { work(); } catch { }
            });
        }
    }
}

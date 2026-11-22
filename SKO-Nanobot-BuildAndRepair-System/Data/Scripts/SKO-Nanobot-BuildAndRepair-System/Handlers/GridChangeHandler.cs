using SKONanobotBuildAndRepairSystem.Cluster;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace SKONanobotBuildAndRepairSystem.Handlers
{
    /// <summary>
    /// BUG-261122.10: last-damage time per grid, so the empty-grid skip (FEAT-017) does not hide new damage.
    /// Written from the server damage handler, read by background scans.
    /// </summary>
    public static class GridChangeHandler
    {
        // At most one write per grid per second (combat raises many damage events).
        private static readonly TimeSpan Throttle = TimeSpan.FromSeconds(1);

        // An empty mark taken this soon after a change is not trusted: the damage may not have been applied yet
        // (the game applies it on the grid's next update) and later hits inside the throttle are not stamped.
        public static readonly TimeSpan Grace = TimeSpan.FromSeconds(2);

        private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(10);

        private static readonly ConcurrentDictionary<long, TimeSpan> _changedAt = new ConcurrentDictionary<long, TimeSpan>();
        private static readonly List<long> _expired = new List<long>();
        private static TimeSpan _lastCleanup;

        public static int Count { get { return _changedAt.Count; } }

        public static void MarkChanged(long gridId, TimeSpan now)
        {
            if (Mod.Settings == null || Mod.Settings.EmptyGridRescanDelaySeconds <= 0) return; // skip disabled, nothing to invalidate

            TimeSpan last;
            if (_changedAt.TryGetValue(gridId, out last) && now - last < Throttle) return;
            _changedAt[gridId] = now;
            GridScanCache.InvalidateIfEmpty(gridId);
        }

        /// <summary>True when the grid changed after, or shortly before, it was found empty at <paramref name="emptyTime"/>.</summary>
        public static bool ChangedSince(long gridId, TimeSpan emptyTime)
        {
            TimeSpan changedAt;
            return _changedAt.TryGetValue(gridId, out changedAt) && emptyTime < changedAt + Grace;
        }

        /// <summary>Main thread. Drops stamps no live empty mark can be older than.</summary>
        public static void Cleanup(TimeSpan now)
        {
            if (now - _lastCleanup < CleanupInterval) return;
            _lastCleanup = now;
            if (_changedAt.Count == 0) return;

            var delay = Mod.Settings != null ? Math.Max(0, Mod.Settings.EmptyGridRescanDelaySeconds) : 0;
            var maxAge = TimeSpan.FromSeconds(delay) + Grace;
            _expired.Clear();
            foreach (var kvp in _changedAt)
            {
                if (now - kvp.Value > maxAge) _expired.Add(kvp.Key);
            }
            TimeSpan dummy;
            for (int i = 0; i < _expired.Count; i++) _changedAt.TryRemove(_expired[i], out dummy);
            _expired.Clear();
        }

        public static void Clear()
        {
            _changedAt.Clear();
            _expired.Clear();
            _lastCleanup = TimeSpan.Zero;
        }
    }
}

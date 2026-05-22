using System.Collections.Generic;

namespace SKONanobotBuildAndRepairSystem.Handlers
{
    /// <summary>
    /// Per-BaR set of grid IDs that are over MaxSystemsPerTargetGrid as of the
    /// last Rebuild(). Read in the inner weld/grind loops via Contains() to
    /// short-circuit the more expensive per-BaR live-count check; rebuilt once
    /// per work tick from Mod.GridSystemCount.
    /// Uses strictly-greater-than (>) because Mod.GridSystemCount includes this
    /// BaR's own contribution; the per-BaR fallback (GetCachedSystemCountOnGrid)
    /// subtracts at most 1 for self.
    /// </summary>
    public sealed class GridSaturationTracker
    {
        private readonly HashSet<long> _saturatedGridIds = new HashSet<long>();

        public int Count { get { return _saturatedGridIds.Count; } }

        public bool Contains(long gridId)
        {
            return _saturatedGridIds.Contains(gridId);
        }

        public void Rebuild()
        {
            _saturatedGridIds.Clear();
            var limit = Mod.Settings.MaxSystemsPerTargetGrid;
            foreach (var kvp in Mod.GridSystemCount)
            {
                if (kvp.Value > limit)
                    _saturatedGridIds.Add(kvp.Key);
            }
        }
    }
}

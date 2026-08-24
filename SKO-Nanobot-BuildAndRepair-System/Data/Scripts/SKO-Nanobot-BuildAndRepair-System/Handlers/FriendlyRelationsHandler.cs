using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.ModAPI;

namespace SKONanobotBuildAndRepairSystem.Handlers
{
    /// <summary>
    /// Owns the friendly-BaR cache and friendly-damage map.
    /// Two related caches sharing this file because they're read together by the
    /// grind path and refreshed together by the same 5 s rebuild timer:
    ///
    ///   * BaRsByOwner / OwnersByOwner — built by Rebuild() on a background
    ///     thread; read snapshots are atomic via volatile dictionary swap.
    ///   * DamageByOwner — written by Grinding/DamageHandler, read by Welding,
    ///     reaped by Cleanup(). Guarded by a single lock since DamageHandler is the
    ///     only non-main-thread writer in practice.
    ///
    /// Initial _LastXxx fields are TimeSpan.Zero (NOT MinValue — that overflows
    /// the now.Subtract(...) timer comparison in UpdateBeforeSimulation).
    /// </summary>
    public static class FriendlyRelationsHandler
    {
        // BUG-123: friendly-BaRs by source-owner (atomic-swapped volatile).
        private static volatile Dictionary<long, List<NanobotSystem>> _BaRsByOwner =
            new Dictionary<long, List<NanobotSystem>>();

        // BUG-130: distinct friendly-owner IDs (one shared FriendlyDamage entry per owner).
        private static volatile Dictionary<long, List<long>> _OwnersByOwner =
            new Dictionary<long, List<long>>();

        // BUG-130: shared friendly-damage map keyed on welder-owner.
        private static readonly object _DamageLock = new object();
        private static readonly Dictionary<long, Dictionary<IMySlimBlock, TimeSpan>> _DamageByOwner =
            new Dictionary<long, Dictionary<IMySlimBlock, TimeSpan>>();
        private static readonly List<IMySlimBlock> _DamageReapBuffer = new List<IMySlimBlock>(64);
        private static TimeSpan _LastDamageCleanup = TimeSpan.Zero;

        /// <summary>
        /// BUG-260610.1: _LastDamageCleanup holds session play-time and survives
        /// world unload — reset so the reap timer works in the next session.
        /// Called from Mod.UnloadData.
        /// </summary>
        public static void ResetSessionState()
        {
            _LastDamageCleanup = TimeSpan.Zero;
        }

        /// <summary>
        /// Snapshot list of BaRs whose welder considers `ownerId` friendly. Returns false
        /// (and a null `friendlies`) if the cache hasn't yet been built for this owner.
        /// </summary>
        public static bool TryGetBaRsForOwner(long ownerId, out List<NanobotSystem> friendlies)
        {
            var snapshot = _BaRsByOwner;
            return snapshot.TryGetValue(ownerId, out friendlies);
        }

        /// <summary>
        /// Distinct welder-owner IDs that consider `ownerId` friendly. Returned list is
        /// the snapshot owned by the cache and must not be mutated by callers.
        /// </summary>
        public static bool TryGetOwnersForOwner(long ownerId, out List<long> owners)
        {
            var snapshot = _OwnersByOwner;
            return snapshot.TryGetValue(ownerId, out owners);
        }

        // BUG-260824.4: memoized entries for attackers who own no BaR (not keys of the
        // rebuilt cache). Invalidated whenever Rebuild swaps in a new snapshot.
        private static readonly object _ComputedLock = new object();
        private static readonly Dictionary<long, List<long>> _ComputedOwners = new Dictionary<long, List<long>>();
        private static Dictionary<long, List<long>> _ComputedSnapshotSource;

        /// <summary>
        /// BUG-260824.4: resolve friendly welder-owners for ANY attacker. The rebuilt
        /// cache only has welder owners as keys, so a BaR-less faction member never got
        /// friendly-damage suppression (BaR instantly re-welded whatever they ground).
        /// Misses are computed against the live welders once and memoized until the next
        /// Rebuild swap (~15 s), keeping per-damage-event cost at a dictionary lookup.
        /// </summary>
        public static bool TryGetOrComputeOwnersForOwner(long attackerId, out List<long> owners)
        {
            var snapshot = _OwnersByOwner;
            if (snapshot.TryGetValue(attackerId, out owners)) return true;

            lock (_ComputedLock)
            {
                if (_ComputedSnapshotSource == snapshot && _ComputedOwners.TryGetValue(attackerId, out owners))
                    return owners != null;
            }

            List<long> computed = null;
            var seenOwners = new HashSet<long>();
            foreach (var entry in Mod.NanobotSystems)
            {
                var system = entry.Value;
                var welder = system != null ? system.Welder : null;
                if (welder == null) continue;
                var welderOwnerId = welder.OwnerId;
                if (welderOwnerId == 0 || !seenOwners.Add(welderOwnerId)) continue;
                var relation = welder.GetUserRelationToOwner(attackerId);
                if (MyRelationsBetweenPlayerAndBlockExtensions.IsFriendly(relation))
                {
                    if (computed == null) computed = new List<long>();
                    computed.Add(welderOwnerId);
                }
            }

            lock (_ComputedLock)
            {
                if (_ComputedSnapshotSource != snapshot)
                {
                    _ComputedOwners.Clear();
                    _ComputedSnapshotSource = snapshot;
                }
                _ComputedOwners[attackerId] = computed;
            }
            owners = computed;
            return computed != null;
        }

        /// <summary>BUG-130: mark friendly damage for the welder-owner (one shared entry).</summary>
        public static void MarkDamage(long welderOwnerId, IMySlimBlock block, TimeSpan deadline)
        {
            if (welderOwnerId == 0 || block == null) return;
            lock (_DamageLock)
            {
                Dictionary<IMySlimBlock, TimeSpan> map;
                if (!_DamageByOwner.TryGetValue(welderOwnerId, out map))
                {
                    map = new Dictionary<IMySlimBlock, TimeSpan>();
                    _DamageByOwner[welderOwnerId] = map;
                }
                map[block] = deadline;
            }
        }

        /// <summary>BUG-130: existence check for welding to skip recently-ground blocks.</summary>
        public static bool IsDamage(long welderOwnerId, IMySlimBlock block)
        {
            if (welderOwnerId == 0 || block == null) return false;
            lock (_DamageLock)
            {
                Dictionary<IMySlimBlock, TimeSpan> map;
                if (!_DamageByOwner.TryGetValue(welderOwnerId, out map)) return false;
                return map.ContainsKey(block);
            }
        }

        /// <summary>BUG-130: periodic reaper (two-pass collect-then-remove).</summary>
        public static void CleanupDamage()
        {
            var now = MyAPIGateway.Session.ElapsedPlayTime;
            if (now.Subtract(_LastDamageCleanup) < Mod.Settings.FriendlyDamageCleanup) return;
            _LastDamageCleanup = now;
            lock (_DamageLock)
            {
                foreach (var ownerEntry in _DamageByOwner)
                {
                    var map = ownerEntry.Value;
                    _DamageReapBuffer.Clear();
                    foreach (var kvp in map)
                    {
                        if (kvp.Value < now) _DamageReapBuffer.Add(kvp.Key);
                    }
                    for (var i = 0; i < _DamageReapBuffer.Count; i++)
                    {
                        map.Remove(_DamageReapBuffer[i]);
                    }
                    _DamageReapBuffer.Clear();
                }
            }
        }

        /// <summary>
        /// BUG-123: rebuild owner caches; deduped on source-owner IDs. Builds fresh dict
        /// and atomically swaps via the volatile field for consistent reader snapshots.
        /// </summary>
        public static void Rebuild()
        {
            var newCache = new Dictionary<long, List<NanobotSystem>>();
            var newOwnerCache = new Dictionary<long, List<long>>();
            var seenOwners = new HashSet<long>();
            var seenOwnerIds = new HashSet<long>();
            foreach (var sourceEntry in Mod.NanobotSystems)
            {
                var sourceWelder = sourceEntry.Value != null ? sourceEntry.Value.Welder : null;
                if (sourceWelder == null) continue;
                var sourceOwnerId = sourceWelder.OwnerId;
                if (sourceOwnerId == 0) continue;
                if (!seenOwners.Add(sourceOwnerId)) continue;

                List<NanobotSystem> list = null;
                List<long> ownerIds = null;
                seenOwnerIds.Clear();
                foreach (var otherEntry in Mod.NanobotSystems)
                {
                    var otherSystem = otherEntry.Value;
                    if (otherSystem == null) continue;
                    var otherWelder = otherSystem.Welder;
                    if (otherWelder == null) continue;
                    var relation = otherWelder.GetUserRelationToOwner(sourceOwnerId);
                    if (MyRelationsBetweenPlayerAndBlockExtensions.IsFriendly(relation))
                    {
                        if (list == null) list = new List<NanobotSystem>();
                        list.Add(otherSystem);
                        var otherOwnerId = otherWelder.OwnerId;
                        if (otherOwnerId != 0 && seenOwnerIds.Add(otherOwnerId))
                        {
                            if (ownerIds == null) ownerIds = new List<long>();
                            ownerIds.Add(otherOwnerId);
                        }
                    }
                }
                if (list != null) newCache[sourceOwnerId] = list;
                if (ownerIds != null) newOwnerCache[sourceOwnerId] = ownerIds;
            }
            _BaRsByOwner = newCache;
            _OwnersByOwner = newOwnerCache;
        }

        /// <summary>
        /// BUG-260610.11: drop all NanobotSystem/IMySlimBlock references on world
        /// unload — statics survive into the next session. Called from Mod.UnloadData.
        /// </summary>
        public static void Clear()
        {
            _BaRsByOwner = new Dictionary<long, List<NanobotSystem>>();
            _OwnersByOwner = new Dictionary<long, List<long>>();
            lock (_ComputedLock)
            {
                _ComputedOwners.Clear();
                _ComputedSnapshotSource = null;
            }
            lock (_DamageLock)
            {
                _DamageByOwner.Clear();
                _DamageReapBuffer.Clear();
            }
        }
    }
}

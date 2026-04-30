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
        // BUG-123: friendly-BaRs by source-owner. Volatile reference for atomic swap.
        private static volatile Dictionary<long, List<NanobotSystem>> _BaRsByOwner =
            new Dictionary<long, List<NanobotSystem>>();

        // BUG-130: distinct welder-owner IDs that consider `key` friendly. Built alongside
        // _BaRsByOwner. Lets the grind path write a single shared FriendlyDamage entry per
        // friendly OWNER instead of N per-BaR entries (174 → ~1 in single-faction worlds).
        private static volatile Dictionary<long, List<long>> _OwnersByOwner =
            new Dictionary<long, List<long>>();

        // BUG-130: shared friendly-damage map keyed on welder-owner id. All BaRs sharing
        // an owner share the same view, which matches the actual semantics (the friendly
        // relation is between OWNERS, not BaRs). Main-thread accesses dominate; the lock
        // is a defensive guard for the rare DamageHandler path.
        private static readonly object _DamageLock = new object();
        private static readonly Dictionary<long, Dictionary<IMySlimBlock, TimeSpan>> _DamageByOwner =
            new Dictionary<long, Dictionary<IMySlimBlock, TimeSpan>>();
        private static readonly List<IMySlimBlock> _DamageReapBuffer = new List<IMySlimBlock>(64);
        private static TimeSpan _LastDamageCleanup = TimeSpan.Zero;

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

        /// <summary>
        /// BUG-130: mark `block` as friendly-damaged for any BaR whose welder is owned by
        /// `welderOwnerId`. Cheap dict insert. Replaces the per-friendly-BaR CDict write loop.
        /// </summary>
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

        /// <summary>
        /// BUG-130: read path used by Welding to avoid welding a block that was just ground
        /// by a friendly. Existence-only check matches the prior State.IsFriendlyDamage
        /// semantics (the timestamp is only consulted by cleanup).
        /// </summary>
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

        /// <summary>
        /// BUG-130: periodic reaper. Runs every Mod.Settings.FriendlyDamageCleanup. Two-pass
        /// collect-then-remove to avoid mutating the dict during enumeration.
        /// </summary>
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
        /// BUG-123: rebuild the BaRsByOwner / OwnersByOwner caches. Walks NanobotSystems
        /// twice in a nested loop, but only for distinct source-owner IDs (`seenOwners`)
        /// so we never repeat work for two BaRs sharing an owner. Engine
        /// GetUserRelationToOwner is called inside the inner loop and is the dominant cost
        /// — but it now amortizes across 5 s of grind ticks instead of running per-grind.
        /// Background-thread invocation matches GridOwnershipCacheHandler. Builds a fresh
        /// dict and atomically swaps in via the volatile field — readers always see a
        /// consistent snapshot.
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
    }
}

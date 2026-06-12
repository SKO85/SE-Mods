using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Caches;
using SKONanobotBuildAndRepairSystem.Extensions;
using SKONanobotBuildAndRepairSystem.Profiling;
using SKONanobotBuildAndRepairSystem.Utils;
using SpaceEngineers.Game.ModAPI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

using VRage;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Handlers
{
    public static class SafeZoneHandler
    {
        // Sentinel returned by GetSafeZonesInRange when no zone matches; callers MUST NOT mutate.
        private static readonly List<MySafeZone> EmptyZoneList = new List<MySafeZone>();

        // BUG-260612.6: sentinel cached for grids with no intersecting zone; never mutated.
        private static readonly List<long> EmptyZoneIdList = new List<long>();

        // PERF-9: pooled scratch reused by GetSafeZones at Register/seeding time. Avoids
        // a HashSet allocation per call. GetSafeZones runs on the main thread only.
        private static readonly HashSet<IMyEntity> _seedZonesScratch = new HashSet<IMyEntity>();

        // REF-2: pooled scratch for CleanupStaleZones. Maintenance runs on the main thread
        // only (Mod.RebuildSourcesAndTargetsTimer + Register), so a single shared buffer
        // is safe and avoids the per-call List allocation.
        private static readonly List<long> _staleZoneKeys = new List<long>();

        public static readonly ConcurrentDictionary<long, MySafeZone> Zones = new ConcurrentDictionary<long, MySafeZone>();

        // BUG-260612.6: gridId → ids of ALL intersecting zones (list is cache-owned and
        // immutable after publish; EmptyZoneIdList = none). Was a single zoneId, which
        // forced first-match-wins resolution under overlapping zones.
        private static readonly TtlCache<long, List<long>> GridIntersectingZones = new TtlCache<long, List<long>>(
           defaultTtl: TimeSpan.FromSeconds(15),
           concurrencyLevel: 4,
           comparer: null,
           capacity: 100);

        private static readonly TtlCache<long, long> BlockIntersectingZones = new TtlCache<long, long>(
           defaultTtl: TimeSpan.FromSeconds(15),
           concurrencyLevel: 4,
           comparer: null,
           capacity: 100);

        private static readonly TtlCache<MyTuple<long, long>, bool> ProtectedFromGrindingCache = new TtlCache<MyTuple<long, long>, bool>(
           defaultTtl: TimeSpan.FromSeconds(15),
           concurrencyLevel: 4,
           comparer: new MyTupleComparer<long, long>(),
           capacity: 100);

        public static int GridCacheCount { get { return GridIntersectingZones.Count; } }
        public static int BlockCacheCount { get { return BlockIntersectingZones.Count; } }
        public static int GrindCacheCount { get { return ProtectedFromGrindingCache.Count; } }

        #region Registration

        private static bool _registered = false;

        public static void Register()
        {
            if (_registered || MyAPIGateway.Session == null)
                return;

            if (MyAPIGateway.Session.IsServer)
            {
                // Seed from existing entities
                GetSafeZones();

                // Register event listeners
                MyAPIGateway.Entities.OnEntityAdd += OnEntityAdd;
                MyAPIGateway.Entities.OnEntityRemove += OnEntityRemove;
            }

            _registered = true;
        }

        public static void GetSafeZones()
        {
            var profilerTs = MethodProfiler.Start();
            try
            {
                _seedZonesScratch.Clear();
                MyAPIGateway.Entities.GetEntities(_seedZonesScratch, e => e is MySafeZone);

                foreach (var entity in _seedZonesScratch)
                {
                    Zones[entity.EntityId] = entity as MySafeZone;
                }
                _seedZonesScratch.Clear();

                CleanupStaleZones();
                GridIntersectingZones.CleanupExpired();
                ProtectedFromGrindingCache.CleanupExpired();
                BlockIntersectingZones.CleanupExpired();
            }
            catch (Exception ex) { Logging.Instance.Write(Logging.Level.Error, "SafeZoneHandler.GetSafeZones: {0}", ex.Message); }
            finally
            {
                if (profilerTs != 0L)
                {
                    var _zoneCount = Zones.Count;
                    MethodProfiler.StopAndLog("SafeZoneHandler.GetSafeZones", profilerTs, () =>
                        string.Format("zones={0}", _zoneCount));
                }
            }
        }

        /// <summary>
        /// BUG-143: lightweight periodic cleanup; OnEntityAdd/OnEntityRemove keep
        /// Zones live, so we skip the full GetSafeZones entity walk.
        /// </summary>
        public static void CleanupSafeZones()
        {
            var profilerTs = MethodProfiler.Start();
            try
            {
                CleanupStaleZones();
                GridIntersectingZones.CleanupExpired();
                ProtectedFromGrindingCache.CleanupExpired();
                BlockIntersectingZones.CleanupExpired();
            }
            catch (Exception ex) { Logging.Instance.Write(Logging.Level.Error, "SafeZoneHandler.CleanupSafeZones: {0}", ex.Message); }
            finally
            {
                if (profilerTs != 0L)
                {
                    var _zoneCount = Zones.Count;
                    MethodProfiler.StopAndLog("SafeZoneHandler.CleanupSafeZones", profilerTs, () =>
                        string.Format("zones={0}", _zoneCount));
                }
            }
        }

        public static void Unregister()
        {
            if (!_registered)
                return;

            // BUG-260610.27: Session can already be null during UnloadData (ordering
            // is engine-version dependent). Skipping only the unsubscribe is fine —
            // the entity list dies with the session — but the flag reset and the
            // collection clears must always run, or the next world's Register()
            // no-ops and Zones keeps dead-world MySafeZone references.
            if (MyAPIGateway.Session != null && MyAPIGateway.Session.IsServer)
            {
                MyAPIGateway.Entities.OnEntityAdd -= OnEntityAdd;
                MyAPIGateway.Entities.OnEntityRemove -= OnEntityRemove;
            }

            Zones?.Clear();
            GridIntersectingZones.Clear();
            ProtectedFromGrindingCache.Clear();
            BlockIntersectingZones.Clear();

            _registered = false;
        }

        #endregion Registration

        private static void OnEntityAdd(IMyEntity ent)
        {
            try
            {
                var sz = ent as MySafeZone;
                if (sz != null)
                {
                    Zones[sz.EntityId] = sz;
                }
            }
            catch (Exception ex) { Logging.Instance.Write(Logging.Level.Error, "SafeZoneHandler.OnEntityAdd: {0}", ex.Message); }
        }

        private static void OnEntityRemove(IMyEntity ent)
        {
            try
            {
                var sz = ent as MySafeZone;
                if (sz != null)
                {
                    MySafeZone removed;
                    Zones.TryRemove(sz.EntityId, out removed);
                }
            }
            catch (Exception ex) { Logging.Instance.Write(Logging.Level.Error, "SafeZoneHandler.OnEntityRemove: {0}", ex.Message); }
        }

        /// <summary>
        /// Removes stale entries from the Zones dictionary (closed or marked-for-close).
        /// Call periodically (e.g., every 100 ticks) to guard against missed OnEntityRemove events.
        /// </summary>
        public static void CleanupStaleZones()
        {
            // REF-2: reuse the pooled buffer instead of allocating per call.
            _staleZoneKeys.Clear();
            foreach (var pair in Zones)
            {
                if (pair.Value == null || pair.Value.MarkedForClose || pair.Value.Closed)
                {
                    _staleZoneKeys.Add(pair.Key);
                }
            }
            MySafeZone removed;
            for (int i = 0; i < _staleZoneKeys.Count; i++)
            {
                Zones.TryRemove(_staleZoneKeys[i], out removed);
            }
            _staleZoneKeys.Clear();
        }

        /// <summary>
        /// Returns the first safe zone intersecting the grid's world AABB, or null if none.
        /// Uses a fast radius-distance precheck before the precise sphere vs AABB test.
        /// </summary>

        public static List<MySafeZone> GetSafeZonesInRange(IMyCubeGrid targetGrid, int range)
        {
            if (Zones.Count == 0) return EmptyZoneList;

            List<MySafeZone> result = null;
            var gridAabb = targetGrid.WorldAABB;
            var gridCenter = gridAabb.Center;
            // BUG-260610.4: include the grid's half-diagonal so a large grid can't
            // out-range the precheck either.
            var gridRadius = gridAabb.HalfExtents.Length();

            foreach (var kvp in Zones)
            {
                var z = kvp.Value;
                if (z == null || z.Closed || z.MarkedForClose || !z.Enabled)
                    continue;

                // BUG-260610.4: the precheck must include the zone's own reach
                // (vanilla zones reach 500 m). Comparing center distance against the
                // flat range filtered out large zones that fully engulf the grid, so
                // the precise intersection test never ran and BaRs could grind
                // inside active safe zones. Same reason there is no result cap: the
                // intersecting zone may not be among the first few enumerated.
                // BUG-260612.3: for Box zones, Radius is just the stale slider value —
                // use the world-AABB half-diagonal as the reach.
                var zoneReach = z.Shape == Sandbox.Common.ObjectBuilders.MySafeZoneShape.Box
                    ? z.PositionComp.WorldAABB.HalfExtents.Length()
                    : z.Radius;
                var distance = Vector3D.Distance(gridCenter, z.PositionComp.WorldAABB.Center);
                if (distance > range + zoneReach + gridRadius)
                    continue;

                if (result == null)
                    result = new List<MySafeZone>(4);

                result.Add(z);
            }

            return result ?? EmptyZoneList;
        }

        /// <summary>
        /// BUG-260612.6: ids of ALL zones intersecting the grid's mechanical group,
        /// cached per grid (15 s, group-wide — a zone touching any member protects
        /// all). Vanilla semantics are "prohibited if ANY containing zone prohibits",
        /// so callers must aggregate across the returned zones instead of acting on a
        /// single first match. The returned list is cache-owned — never mutate.
        /// Null or empty = no intersecting zone.
        /// </summary>
        public static List<long> GetIntersectingSafeZoneIds(IMyCubeGrid targetGrid)
        {
            var profilerTs = MethodProfiler.Start();
            var cacheHit = false;
            try
            {
                if (targetGrid == null || Zones.Count == 0)
                {
                    return null;
                }

                List<long> cached;
                if (GridIntersectingZones.TryGet(targetGrid.EntityId, out cached))
                {
                    cacheHit = true;
                    return cached;
                }

                // Get safe-zones within 300m range (cheap precheck walk; not cached when empty).
                var zones = GetSafeZonesInRange(targetGrid, 300);
                if (zones.Count == 0)
                {
                    return null;
                }

                // CON-4 / PERF-2: fetch the mechanical group ONCE per call, lazily.
                List<IMyCubeGrid> groups = null;
                List<long> intersecting = null;

                foreach (var zone in zones)
                {
                    if (zone == null || zone.Closed || zone.MarkedForClose || !zone.Enabled)
                    {
                        continue;
                    }

                    if (groups == null)
                    {
                        groups = new List<IMyCubeGrid>();
                        MyAPIGateway.GridGroups.GetGroup(targetGrid, GridLinkTypeEnum.Mechanical, groups);
                    }

                    var intersects = GridIntersects(targetGrid, zone);
                    if (!intersects)
                    {
                        for (int i = 0; i < groups.Count; i++)
                        {
                            var subGrid = groups[i];
                            if (subGrid.EntityId == targetGrid.EntityId)
                                continue;
                            if (GridIntersects(subGrid, zone))
                            {
                                intersects = true;
                                break;
                            }
                        }
                    }

                    if (intersects)
                    {
                        if (intersecting == null) intersecting = new List<long>(2);
                        intersecting.Add(zone.EntityId);
                    }
                }

                // Cache for the whole mechanical group (zone reach is group-wide).
                var cacheList = intersecting ?? EmptyZoneIdList;
                GridIntersectingZones.Set(targetGrid.EntityId, cacheList);
                if (groups != null)
                {
                    for (int i = 0; i < groups.Count; i++)
                    {
                        if (groups[i].EntityId == targetGrid.EntityId)
                            continue;
                        GridIntersectingZones.Set(groups[i].EntityId, cacheList);
                    }
                }

                return intersecting;
            }
            finally
            {
                if (profilerTs != 0L)
                {
                    var _hit = cacheHit;
                    MethodProfiler.StopAndLog("SafeZoneHandler.GetIntersectingSafeZoneIds", profilerTs, () =>
                        string.Format("cacheHit={0}", _hit));
                }
            }
        }

        /// <summary>
        /// BUG-260612.6: true when ANY zone intersecting the grid's mechanical group
        /// prohibits the given action.
        /// </summary>
        public static bool AnyIntersectingZoneProhibits(IMyCubeGrid grid, SafeZoneAction action)
        {
            var zoneIds = GetIntersectingSafeZoneIds(grid);
            if (zoneIds == null) return false;
            for (int i = 0; i < zoneIds.Count; i++)
            {
                MySafeZone zone;
                if (!Zones.TryGetValue(zoneIds[i], out zone) || zone.Closed || zone.MarkedForClose || !zone.Enabled)
                    continue;
                if (!zone.IsActionAllowed(CastProhibit(MySessionComponentSafeZones.AllowedActions, action), 0L))
                    return true;
            }
            return false;
        }

        // BUG-260612.3: zones can be Box-shaped; Radius is then just the stale slider
        // value. Boxes test against the zone's world AABB (conservative for rotated
        // boxes — over-protects, never under-protects); spheres keep the radius test.
        private static bool ZoneIntersects(MySafeZone zone, ref BoundingBoxD targetBox)
        {
            if (zone.Shape == Sandbox.Common.ObjectBuilders.MySafeZoneShape.Box)
            {
                var zoneBox = zone.PositionComp.WorldAABB;
                return zoneBox.Intersects(ref targetBox);
            }
            var checkSphere = new BoundingSphereD(zone.PositionComp.WorldAABB.Center, zone.Radius);
            return checkSphere.Intersects(targetBox);
        }

        private static bool GridIntersects(IMyCubeGrid targetGrid, MySafeZone zone)
        {
            BoundingBoxD targetBox = targetGrid.WorldAABB;
            return ZoneIntersects(zone, ref targetBox);
        }

        private static bool BlockIntersects(IMySlimBlock targetBlock, MySafeZone zone, bool cache = true)
        {
            if (targetBlock == null) return false;

            if (targetBlock.FatBlock != null && cache)
            {
                long zoneId = 0;
                if (BlockIntersectingZones.TryGet(targetBlock.FatBlock.EntityId, out zoneId))
                {
                    if (zoneId > 0)
                    {
                        return true;
                    }

                    return false;
                }
            }

            BoundingBoxD targetBox;
            targetBlock.GetWorldBoundingBox(out targetBox);

            // BUG-260612.3: shape-aware (box zones used to be tested as Radius spheres).
            var targetIntersects = ZoneIntersects(zone, ref targetBox);

            if (targetBlock.FatBlock != null && cache)
            {
                if (targetIntersects)
                {
                    BlockIntersectingZones.Set(targetBlock.FatBlock.EntityId, zone.EntityId);
                }
                else
                {
                    BlockIntersectingZones.Set(targetBlock.FatBlock.EntityId, 0);
                }
            }

            return targetIntersects;
        }

        /// <summary>
        /// Compatibility wrapper: returns true if any safe zone intersects; prefer GetIntersectingSafeZone.
        /// </summary>

        public static T CastProhibit<T>(T ptr, object val) => (T)val;

        public enum SafeZoneAction
        {
            Welding = 8,
            Grinding = 16,
            BuildingProjections = 512
        }

        public struct ActionsState
        {
            public bool IsGrindingAllowed;
            public bool IsWeldingAllowed;
            public bool IsBuildingProjectionsAllowed;
        }

        public static ActionsState GetActionsAllowedForSystem(NanobotSystem system)
        {
            var response = new ActionsState()
            {
                IsGrindingAllowed = true,
                IsWeldingAllowed = true,
                IsBuildingProjectionsAllowed = true,
            };

            try
            {
                if (!Mod.Settings.SafeZoneCheckEnabled || Zones.Count == 0)
                    return response;

                if (system != null && system.Welder != null)
                {
                    // BUG-260612.6: AND permissions across ALL zones containing the
                    // welder block — first-match resolution let whichever zone
                    // enumerated first mask a prohibiting one.
                    var safeZones = GetSafeZonesInRange(system.Welder.CubeGrid, 300);
                    for (int i = 0; i < safeZones.Count; i++)
                    {
                        var zone = safeZones[i];
                        if (!BlockIntersects(system.Welder.SlimBlock, zone, false))
                            continue;

                        response.IsGrindingAllowed &= zone.IsActionAllowed(CastProhibit(MySessionComponentSafeZones.AllowedActions, SafeZoneAction.Grinding), 0L);
                        response.IsWeldingAllowed &= zone.IsActionAllowed(CastProhibit(MySessionComponentSafeZones.AllowedActions, SafeZoneAction.Welding), 0L);
                        response.IsBuildingProjectionsAllowed &= zone.IsActionAllowed(CastProhibit(MySessionComponentSafeZones.AllowedActions, SafeZoneAction.BuildingProjections), 0L);

                        // Everything already prohibited — no zone can un-prohibit.
                        if (!response.IsGrindingAllowed && !response.IsWeldingAllowed && !response.IsBuildingProjectionsAllowed)
                            break;
                    }
                }

                return response;
            }
            catch (Exception ex)
            {
                Logging.Instance.Write(Logging.Level.Error, "SafeZoneHandler.GetActionsAllowedForSystem: {0}", ex.Message);
                // BUG-260502.1: fail closed. A transient engine exception during
                // safe-zone evaluation must not silently unlock actions that
                // should be restricted. The caller retries on the next tick;
                // by then the engine state has typically stabilised.
                return new ActionsState()
                {
                    IsGrindingAllowed = false,
                    IsWeldingAllowed = false,
                    IsBuildingProjectionsAllowed = false,
                };
            }
        }

        private static void SetIsProtectedFromGrinding(IMySlimBlock targetBlock, long attackerBlockId, bool isProtected)
        {
            if (targetBlock != null && targetBlock.FatBlock != null)
            {
                ProtectedFromGrindingCache.Set(new MyTuple<long, long>(targetBlock.FatBlock.EntityId, attackerBlockId), isProtected);
            }
        }

        public static bool IsProtectedFromGrinding(IMySlimBlock targetBlock, IMyCubeBlock attackerBlock)
        {
            var profilerTs = MethodProfiler.Start();
            var cacheHit = false;
            try
            {
                if (targetBlock == null) return false;
                if (attackerBlock == null) return false;
                if (!Mod.Settings.SafeZoneCheckEnabled) return false;
                if (Zones.Count == 0) return false;

                if (targetBlock.FatBlock != null)
                {
                    var isProtected = false;
                    if (ProtectedFromGrindingCache.TryGet(new MyTuple<long, long>(targetBlock.FatBlock.EntityId, attackerBlock.EntityId), out isProtected))
                    {
                        cacheHit = true;
                        return isProtected;
                    }
                }

                // BUG-260612.6: a block is protected if ANY intersecting zone protects
                // it — the previous single-zone resolution let a permissive zone mask
                // a prohibiting one under overlap.
                var isProtectedResult = false;
                var zoneIds = GetIntersectingSafeZoneIds(targetBlock.CubeGrid);
                if (zoneIds != null)
                {
                    for (int i = 0; i < zoneIds.Count; i++)
                    {
                        MySafeZone zone;
                        if (!Zones.TryGetValue(zoneIds[i], out zone) || zone.Closed || zone.MarkedForClose || !zone.Enabled)
                            continue;
                        if (IsProtectedFromGrindingByZone(zone, targetBlock, attackerBlock))
                        {
                            isProtectedResult = true;
                            break;
                        }
                    }
                }

                SetIsProtectedFromGrinding(targetBlock, attackerBlock.EntityId, isProtectedResult);
                return isProtectedResult;
            }
            catch (Exception ex)
            {
                // BUG-260502.2: fail closed. The original catch wrote false (not
                // protected) into the 15 s TTL cache and returned false, so a
                // single transient exception would let the BaR grind a
                // protected block for up to 15 s. Now: log, return true (treat
                // as protected), and SKIP the cache write so the next call
                // retries clean against live engine state.
                Logging.Instance.Write(Logging.Level.Error, "SafeZoneHandler.IsProtectedFromGrinding: {0}", ex.Message);
                return true;
            }
            finally
            {
                if (profilerTs != 0L)
                {
                    var _hit = cacheHit;
                    MethodProfiler.StopAndLog("SafeZoneHandler.IsProtectedFromGrinding", profilerTs, () =>
                        string.Format("cacheHit={0}", _hit));
                }
            }
        }

        /// <summary>
        /// BUG-260612.6: single-zone protection verdict (logic unchanged from the old
        /// inline body); true = this zone protects the target from this attacker.
        /// </summary>
        private static bool IsProtectedFromGrindingByZone(MySafeZone safeZone, IMySlimBlock targetBlock, IMyCubeBlock attackerBlock)
        {
            // Grinding prohibited in this zone → protected outright.
            var isAllowed = safeZone.IsActionAllowed(CastProhibit(MySessionComponentSafeZones.AllowedActions, SafeZoneAction.Grinding), 0L);
            if (!isAllowed)
                return true;

            if (safeZone.SafeZoneBlockId > 0)
            {
                var safeZoneBlock = MyEntities.GetEntityByName(safeZone.SafeZoneBlockId.ToString()) as IMySafeZoneBlock;

                if (safeZoneBlock == null)
                {
                    // Entity not loaded or cast failed — default to protected to be safe.
                    return true;
                }

                // Relation between safeZone owner and attacker.
                var relationSafeZoneAttacker = attackerBlock.CubeGrid.GetRelationBetweenGridAndPlayer(safeZoneBlock.OwnerId);
                if (relationSafeZoneAttacker != VRage.Game.MyRelationsBetweenPlayerAndBlock.Owner && relationSafeZoneAttacker != VRage.Game.MyRelationsBetweenPlayerAndBlock.FactionShare)
                {
                    return true;
                }

                if (targetBlock.OwnerId == attackerBlock.OwnerId)
                {
                    return false;
                }

                // Relation attacker grid and target block.
                var relationAttackerTarget = targetBlock.CubeGrid.GetRelationBetweenGridAndPlayer(attackerBlock.OwnerId);
                if (relationAttackerTarget == VRage.Game.MyRelationsBetweenPlayerAndBlock.Owner || relationAttackerTarget == VRage.Game.MyRelationsBetweenPlayerAndBlock.FactionShare || relationAttackerTarget == VRage.Game.MyRelationsBetweenPlayerAndBlock.NoOwnership)
                {
                    return false;
                }
            }
            else
            {
                if (targetBlock.OwnerId == attackerBlock.OwnerId)
                {
                    return false;
                }

                // Relation between target block and attacker grid.
                var relationAttackerTarget = targetBlock.CubeGrid.GetRelationBetweenGridAndPlayer(attackerBlock.OwnerId);
                if (relationAttackerTarget == VRage.Game.MyRelationsBetweenPlayerAndBlock.Owner || relationAttackerTarget == VRage.Game.MyRelationsBetweenPlayerAndBlock.FactionShare)
                {
                    return false;
                }
            }

            // OwnerId equality already covered by both branches above. Falling through
            // to the user-relation check: owner / faction member may grind in-zone.
            var targetRelation = targetBlock.GetUserRelationToOwner(attackerBlock.OwnerId);
            if (targetRelation == VRage.Game.MyRelationsBetweenPlayerAndBlock.Owner || targetRelation == VRage.Game.MyRelationsBetweenPlayerAndBlock.FactionShare)
            {
                return false;
            }

            // Cannot grind a protected target block.
            return true;
        }
    }
}
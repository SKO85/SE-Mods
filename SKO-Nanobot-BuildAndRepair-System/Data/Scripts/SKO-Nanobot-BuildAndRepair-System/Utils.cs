using System;
using System.Collections.Concurrent;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem
{
    public static class Utils
    {
        private class RelationCacheEntry
        {
            public VRage.Game.MyRelationsBetweenPlayerAndBlock Relation;
            public long ExpireTick;
        }

        // Per-grid cache to reduce key allocations and contention
        private static readonly ConcurrentDictionary<long, ConcurrentDictionary<long, RelationCacheEntry>> _relationCache =
            new ConcurrentDictionary<long, ConcurrentDictionary<long, RelationCacheEntry>>();
        private static readonly long _relationCacheTtlTicks = TimeSpan.FromSeconds(10).Ticks;

        /// <summary>
        /// Is the block damaged/incomplete/projected
        /// </summary>
        public static bool NeedRepair(this IMySlimBlock target, UtilsInventory.IntegrityLevel integrityLevel)
        {
            if (target == null) return false;

            var neededIntegrityLevel = target.GetRequiredIntegrity(integrityLevel);
            var needRepair = !target.IsDestroyed && (target.FatBlock == null || !target.FatBlock.Closed) && (target.Integrity < neededIntegrityLevel || target.MaxDeformation > 0.01f);

            return needRepair;
        }

        public static float GetRequiredIntegrity(this IMySlimBlock target, UtilsInventory.IntegrityLevel integrityLevel)
        {
            if (target == null) return 0f;

            var def = target.BlockDefinition as MyCubeBlockDefinition;
            var requiredIntegrity = target.MaxIntegrity;

            if (integrityLevel == UtilsInventory.IntegrityLevel.Functional)
            {
                var functionalIntegrity = target.MaxIntegrity * def.CriticalIntegrityRatio;
                requiredIntegrity = SetMax(functionalIntegrity + 1, target.MaxIntegrity);
            }
            else if (integrityLevel == UtilsInventory.IntegrityLevel.Skeleton)
            {
                var functionalIntegrity = target.MaxIntegrity * def.CriticalIntegrityRatio;
                var skeletonIntegrity = target.MaxIntegrity * Constants.MaxSkeletonCreateIntegrityRatio;

                requiredIntegrity = skeletonIntegrity;
            }

            return requiredIntegrity;
        }

        public static float SetMax(float value, float maxValue)
        {
            if (value > maxValue)
            {
                value = maxValue;
            }
            return value;
        }

        /// <summary>
        /// Is the grid a projected grid
        /// </summary>
        public static bool IsProjected(this IMyCubeGrid target)
        {
            var cubeGrid = target as MyCubeGrid;
            return cubeGrid?.Projector != null;
        }

        /// <summary>
        /// Is the block a projected block
        /// </summary>
        public static bool IsProjected(this IMySlimBlock target)
        {
            var cubeGrid = target.CubeGrid as MyCubeGrid;
            return cubeGrid?.Projector != null;
        }

        /// <summary>
        /// Is the block a projected block
        /// </summary>
        public static bool IsProjected(this IMySlimBlock target, out IMyProjector projector)
        {
            var cubeGrid = target.CubeGrid as MyCubeGrid;
            projector = cubeGrid?.Projector;
            return projector != null;
        }

        /// <summary>
        /// Could the projected block be build
        /// !GUI Thread!
        /// </summary>
        /// <param name="target"></param>
        /// <returns></returns>
        public static bool CanBuild(this IMySlimBlock target, bool gui)
        {
            var cubeGrid = target.CubeGrid as MyCubeGrid;
            if (cubeGrid?.Projector == null) return false;

            var projector = (IMyProjector)cubeGrid.Projector;
            return projector.CanBuild(target, gui) == BuildCheckResult.OK;
        }

        /// <summary>
        /// The inventory is filled to X percent
        /// </summary>
        /// <param name="inventory"></param>
        /// <returns></returns>
        public static float IsFilledToPercent(this IMyInventory inventory)
        {
            return Math.Max((float)inventory.CurrentVolume / (float)inventory.MaxVolume, (float)inventory.CurrentMass / (float)((MyInventory)inventory).MaxMass);
        }

        /// <summary>
        /// Checks if block is inside the given BoundingBox
        /// </summary>
        /// <param name="block"></param>
        /// <param name="areaBox"></param>
        /// <returns></returns>
        public static bool IsInRange(this IMySlimBlock block, ref MyOrientedBoundingBoxD areaBox, out double distance)
        {
            Vector3 halfExtents;
            block.ComputeScaledHalfExtents(out halfExtents);

            var matrix = block.CubeGrid.WorldMatrix;
            matrix.Translation = block.CubeGrid.GridIntegerToWorld(block.Position);

            var box = new MyOrientedBoundingBoxD(new BoundingBoxD(-halfExtents, halfExtents), matrix);
            var inRange = areaBox.Intersects(ref box);

            distance = inRange ? (areaBox.Center - box.Center).Length() : 0;
            return inRange;
        }

        /// <summary>
        /// Get the block name for GUI
        /// </summary>
        /// <param name="slimBlock"></param>
        /// <returns></returns>
        public static string BlockName(this IMySlimBlock slimBlock)
        {
            if (slimBlock != null)
            {
                var terminalBlock = slimBlock.FatBlock as IMyTerminalBlock;
                if (terminalBlock != null)
                {
                    return (terminalBlock.CubeGrid != null ? terminalBlock.CubeGrid.DisplayName : "Unknown Grid") + "." + terminalBlock.CustomName;
                }
                return (slimBlock.CubeGrid != null ? slimBlock.CubeGrid.DisplayName : "Unknown Grid") + "." + slimBlock.BlockDefinition.DisplayNameText;
            }
            return "(none)";
        }

        public static string BlockName(this VRage.Game.ModAPI.Ingame.IMySlimBlock slimBlock)
        {
            if (slimBlock != null)
            {
                var terminalBlock = slimBlock.FatBlock as Sandbox.ModAPI.Ingame.IMyTerminalBlock;
                if (terminalBlock != null)
                {
                    return (terminalBlock.CubeGrid != null ? terminalBlock.CubeGrid.DisplayName : "Unknown Grid") + "." + terminalBlock.CustomName;
                }
                else
                {
                    return (slimBlock.CubeGrid != null ? slimBlock.CubeGrid.DisplayName : "Unknown Grid") + "." + slimBlock.BlockDefinition.ToString();
                }
            }
            return "(none)";
        }

        /// <summary>
        /// Return relation between player and grid, in case of 'NoOwnership' check the grid owner.
        /// </summary>
        /// <param name="slimBlock"></param>
        /// <param name="userId"></param>
        /// <returns></returns>
        public static VRage.Game.MyRelationsBetweenPlayerAndBlock GetUserRelationToOwner(this IMySlimBlock slimBlock, long userId)
        {
            if (slimBlock == null)
            {
                return VRage.Game.MyRelationsBetweenPlayerAndBlock.NoOwnership;
            }
            var fatBlock = slimBlock.FatBlock;
            if (fatBlock != null)
            {
                var relation = fatBlock.GetUserRelationToOwner(userId);
                if (relation == VRage.Game.MyRelationsBetweenPlayerAndBlock.NoOwnership)
                {
                    relation = GridOwnershipManager.GetRelationBetweenGridAndPlayer(slimBlock.CubeGrid, userId);
                    return relation;
                }
                else
                {
                    return relation;
                }
            }
            else
            {
                var relation = GridOwnershipManager.GetRelationBetweenGridAndPlayer(slimBlock.CubeGrid, userId);
                return relation;
            }
        }

        public static VRage.MyFixedPoint AsFloorMyFixedPoint(this float value)
        {
            return new VRage.MyFixedPoint() { RawValue = (long)(value * 1000000L) };
        }

        public static int CompareDistance(double a, double b)
        {
            var diff = a - b;
            return Math.Abs(diff) < 0.00001 ? 0 : diff > 0 ? 1 : -1;
        }

        public static bool IsCharacterPlayerAndActive(IMyCharacter character)
        {
            return character != null && character.IsPlayer && !character.Closed && character.InScene && !character.IsDead;
        }
    }
}

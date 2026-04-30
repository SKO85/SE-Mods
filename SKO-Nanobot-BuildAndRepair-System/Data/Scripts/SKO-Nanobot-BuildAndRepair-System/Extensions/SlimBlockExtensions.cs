using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Handlers;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Extensions
{
    /// <summary>
    /// Extension methods on IMySlimBlock / IMyCubeGrid for the BaR pipeline:
    /// damage / integrity decisions, projector-build queries, range checks, and
    /// display-name formatting. Pure functions — no caches, no per-instance state.
    /// </summary>
    public enum IntegrityLevel
    {
        Create,
        Functional,
        Complete
    }

    public static class SlimBlockExtensions
    {
        public const float MinDeformation = 0.01f;

        /// <summary>
        /// Is the block damaged/incomplete and worth repairing under the given weld mode.
        /// </summary>
        public static bool NeedRepair(this IMySlimBlock target, AutoWeldOptions weldMode)
        {
            if (target == null) return false;
            if (target.IsDestroyed) return false;
            if (target.FatBlock != null && (target.FatBlock.Closed || target.FatBlock.MarkedForClose)) return false;

            // FEAT-034: Skeleton mode never repairs existing blocks — only places projected blocks.
            if (weldMode == AutoWeldOptions.WeldSkeleton) return false;

            var neededIntegrityLevel = GetRequiredIntegrity(target, weldMode);
            var hasReachedIntegrity = target.Integrity >= neededIntegrityLevel;

            if (!hasReachedIntegrity) return true;

            if (target.MaxDeformation > MinDeformation)
            {
                target.ResetSkeleton();
                // MaxDeformation is bugged in-game and doesn't reset until restart/full removal,
                // so don't tell BaR to weld for this case.
                return false;
            }

            if (target.HasDeformation)
            {
                target.ResetSkeleton();
                // HasDeformation is reliable (heavy though) — tell BaR to weld.
                return true;
            }

            return false;
        }

        public static void ResetSkeleton(this IMySlimBlock block)
        {
            if (block == null) return;

            var cg = block.CubeGrid as MyCubeGrid;
            if (cg != null)
            {
                try
                {
                    cg.ResetBlockSkeleton(cg.GetCubeBlock(block.Min), true);
                    return;
                }
                catch { }
            }

            block.FixBones(0, 100f);
        }

        public static bool IsProjected(this IMyCubeGrid target)
        {
            var cubeGrid = target as MyCubeGrid;
            return (cubeGrid != null && cubeGrid.Projector != null);
        }

        public static bool IsProjected(this IMySlimBlock target)
        {
            var cubeGrid = target.CubeGrid as MyCubeGrid;
            return (cubeGrid != null && cubeGrid.Projector != null);
        }

        public static bool IsProjected(this IMySlimBlock target, out IMyProjector projector)
        {
            var cubeGrid = target.CubeGrid as MyCubeGrid;
            projector = cubeGrid != null ? cubeGrid.Projector : null;
            return projector != null;
        }

        /// <summary>
        /// Could the projected block be built. !GUI Thread!
        /// </summary>
        public static bool CanBuild(this IMySlimBlock target, bool gui)
        {
            var cubeGrid = target.CubeGrid as MyCubeGrid;
            if (cubeGrid == null || cubeGrid.Projector == null) return false;

            return ((IMyProjector)cubeGrid.Projector).CanBuild(target, gui) == BuildCheckResult.OK;
        }

        public static float GetRequiredIntegrity(this IMySlimBlock target, AutoWeldOptions weldMode)
        {
            if (target == null) return 0f;

            if (weldMode == AutoWeldOptions.WeldFunctional)
            {
                var def = target.BlockDefinition as MyCubeBlockDefinition;
                var functionalIntegrity = target.MaxIntegrity * def.CriticalIntegrityRatio;
                return functionalIntegrity + 1 > target.MaxIntegrity ? target.MaxIntegrity : functionalIntegrity + 1;
            }

            return target.MaxIntegrity;
        }

        /// <summary>
        /// Get the block name for GUI ("GridName.BlockName").
        /// </summary>
        public static string BlockName(this IMySlimBlock slimBlock)
        {
            if (slimBlock != null)
            {
                var terminalBlock = slimBlock.FatBlock as IMyTerminalBlock;
                if (terminalBlock != null)
                {
                    return string.Format("{0}.{1}", terminalBlock.CubeGrid != null ? terminalBlock.CubeGrid.DisplayName : "Unknown Grid", terminalBlock.CustomName);
                }
                else
                {
                    return string.Format("{0}.{1}", slimBlock.CubeGrid != null ? slimBlock.CubeGrid.DisplayName : "Unknown Grid", slimBlock.BlockDefinition.DisplayNameText);
                }
            }
            else return "(none)";
        }

        public static string BlockName(this VRage.Game.ModAPI.Ingame.IMySlimBlock slimBlock)
        {
            if (slimBlock != null)
            {
                var terminalBlock = slimBlock.FatBlock as Sandbox.ModAPI.Ingame.IMyTerminalBlock;
                if (terminalBlock != null)
                {
                    return string.Format("{0}.{1}", terminalBlock.CubeGrid != null ? terminalBlock.CubeGrid.DisplayName : "Unknown Grid", terminalBlock.CustomName);
                }
                else
                {
                    return string.Format("{0}.{1}", slimBlock.CubeGrid != null ? slimBlock.CubeGrid.DisplayName : "Unknown Grid", slimBlock.BlockDefinition.ToString());
                }
            }
            else return "(none)";
        }

        public static VRage.Game.MyRelationsBetweenPlayerAndBlock GetUserRelationToOwner(this IMySlimBlock slimBlock, long userId)
        {
            if (slimBlock == null)
            {
                return VRage.Game.MyRelationsBetweenPlayerAndBlock.NoOwnership;
            }

            var fatBlock = slimBlock.FatBlock;
            if (fatBlock != null)
            {
                // BUG-117: When the block has no individual owner (OwnerId == 0), the engine's
                // GetUserRelationToOwner returns NoOwnership and the slow path falls back to the
                // grid relation anyway. Skip the engine call and go straight to the cached grid
                // relation. Saves the per-block engine call on the dominant case (most fat blocks
                // inherit grid ownership rather than being individually claimed) — the scan loop
                // walks ~7 000 blocks per huge grid so this matters.
                if (fatBlock.OwnerId == 0)
                {
                    return GridOwnershipCacheHandler.GetRelationBetweenGridAndPlayer(slimBlock.CubeGrid, userId);
                }

                var relation = fatBlock.GetUserRelationToOwner(userId);
                if (relation == VRage.Game.MyRelationsBetweenPlayerAndBlock.NoOwnership)
                {
                    relation = GridOwnershipCacheHandler.GetRelationBetweenGridAndPlayer(slimBlock.CubeGrid, userId);
                    return relation;
                }
                else
                {
                    return relation;
                }
            }
            else
            {
                var relation = GridOwnershipCacheHandler.GetRelationBetweenGridAndPlayer(slimBlock.CubeGrid, userId);
                return relation;
            }
        }

        /// <summary>
        /// Sum the components needed to bring `block` from its current state to the requested
        /// `level`. For projected blocks, walks blockDefinition.Components up to either
        /// CriticalGroup+1 (Functional) or all (Complete). For non-projected blocks, defers to
        /// the engine's GetMissingComponents and (when Functional) subtracts non-critical
        /// components that already meet their target counts.
        /// </summary>
        public static void GetMissingComponents(this IMySlimBlock block, Dictionary<string, int> componentList, IntegrityLevel level)
        {
            var blockDefinition = block.BlockDefinition as MyCubeBlockDefinition;

            if (blockDefinition.Components == null || blockDefinition.Components.Length == 0) return;

            if (level == IntegrityLevel.Create)
            {
                var component = blockDefinition.Components[0];
                componentList.Add(component.Definition.Id.SubtypeName, 1);
            }
            else
            {
                if (block.IsProjected())
                {
                    int maxIdx = level == IntegrityLevel.Functional ? blockDefinition.CriticalGroup + 1 : blockDefinition.Components.Length;

                    for (var idx = 0; idx < maxIdx; idx++)
                    {
                        var component = blockDefinition.Components[idx];

                        if (componentList.ContainsKey(component.Definition.Id.SubtypeName))
                        {
                            componentList[component.Definition.Id.SubtypeName] += component.Count;
                        }
                        else
                        {
                            componentList.Add(component.Definition.Id.SubtypeName, component.Count);
                        }
                    }
                }
                else
                {
                    block.GetMissingComponents(componentList);

                    if (level == IntegrityLevel.Functional)
                    {
                        for (var idx = blockDefinition.CriticalGroup + 1; idx < blockDefinition.Components.Length; idx++)
                        {
                            var component = blockDefinition.Components[idx];
                            if (componentList.ContainsKey(component.Definition.Id.SubtypeName))
                            {
                                var amount = componentList[component.Definition.Id.SubtypeName];

                                if (amount <= component.Count)
                                {
                                    componentList.Remove(component.Definition.Id.SubtypeName);
                                }
                                else
                                {
                                    componentList[component.Definition.Id.SubtypeName] -= component.Count;
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Bounding-box range check from SlimBlockHelper. Returns whether the block intersects
        /// areaBox; distance is set to the centre-to-centre distance when in range, else 0.
        /// </summary>
        public static bool IsInRange(this IMySlimBlock block, ref MyOrientedBoundingBoxD areaBox, out double distance)
        {
            Vector3 halfExtents;
            block.ComputeScaledHalfExtents(out halfExtents);

            var matrix = block.CubeGrid.WorldMatrix;
            matrix.Translation = block.CubeGrid.GridIntegerToWorld(block.Position);

            var box = new MyOrientedBoundingBoxD(new BoundingBoxD(-(halfExtents), (halfExtents)), matrix);
            var inRange = areaBox.Intersects(ref box);

            distance = inRange ? (areaBox.Center - box.Center).Length() : 0;
            return inRange;
        }
    }
}

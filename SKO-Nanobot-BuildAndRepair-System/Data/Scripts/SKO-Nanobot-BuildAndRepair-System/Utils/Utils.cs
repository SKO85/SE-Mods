using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Handlers;
using System;
using VRage.Game.ModAPI;

namespace SKONanobotBuildAndRepairSystem.Utils
{
    public static class Utils
    {
        public static float MinDeformation = 0.01f;

        /// <summary>
        /// Is the block damaged/incomplete
        /// </summary>
        public static bool NeedRepair(this IMySlimBlock target, bool functionalOnly)
        {
            if (target == null) return false;
            if (target.IsDestroyed) return false;
            if (target.FatBlock != null && (target.FatBlock.Closed || target.FatBlock.MarkedForClose)) return false;

            // Integrity check first.
            var neededIntegrityLevel = GetRequiredIntegrity(target, functionalOnly);
            var hasReachedIntegrity = target.Integrity >= neededIntegrityLevel;

            // Integrty is lower, so we can say it needs a repair without checking deformations.
            if (!hasReachedIntegrity) return true;

            // If deformation detected via MaxDeformation, just fix the bones and do not tell the BnR system to weld anything.
            if (target.MaxDeformation > MinDeformation)
            {
                // Keep trackign the minimal deformation.
                if (target.MaxDeformation < MinDeformation)
                {
                    MinDeformation = target.MaxDeformation;
                }

                // Just try fix the bones structure for defromations.
                target.ResetSkeleton();

                // Report it as target not to weld in this case as MaxDeformation is bugged in game and not resetting.
                // It resets after restart of the game or after block is fully removed.
                return false;
            }

            // If deformation detected via HasDeformation, also fix the bones.
            if (target.HasDeformation)
            {
                // try fix the bones as integrity is already on required level.
                target.ResetSkeleton();

                // Tell it to weld in this case as this property seems to be reliable, but heavy.
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

        /// <summary>
        /// Is the grid a projected grid
        /// </summary>
        public static bool IsProjected(this IMyCubeGrid target)
        {
            var cubeGrid = target as MyCubeGrid;
            return (cubeGrid != null && cubeGrid.Projector != null);
        }

        /// <summary>
        /// Is the block a projected block
        /// </summary>
        public static bool IsProjected(this IMySlimBlock target)
        {
            var cubeGrid = target.CubeGrid as MyCubeGrid;
            return (cubeGrid != null && cubeGrid.Projector != null);
        }

        /// <summary>
        /// Is the block a projected block
        /// </summary>
        public static bool IsProjected(this IMySlimBlock target, out IMyProjector projector)
        {
            var cubeGrid = target.CubeGrid as MyCubeGrid;
            projector = cubeGrid != null ? cubeGrid.Projector : null;
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
            if (cubeGrid == null || cubeGrid.Projector == null) return false;

            //Doesn't work reliable as projector does not update Dithering
            //return gui ? ((IMyProjector)cubeGrid.Projector).CanBuild(target, true) == BuildCheckResult.OK : target.Dithering >= -MyGridConstants.BUILDER_TRANSPARENCY;

            return ((IMyProjector)cubeGrid.Projector).CanBuild(target, gui) == BuildCheckResult.OK;
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

        public static float GetRequiredIntegrity(this IMySlimBlock target, bool isFunctionalOnly)
        {
            if (target == null) return 0f;

            var def = target.BlockDefinition as MyCubeBlockDefinition;
            var requiredIntegrity = target.MaxIntegrity;

            if (isFunctionalOnly)
            {
                var functionalIntegrity = target.MaxIntegrity * def.CriticalIntegrityRatio;
                requiredIntegrity = SetMax(functionalIntegrity + 1, target.MaxIntegrity);
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

        public static VRage.MyFixedPoint AsFloorMyFixedPoint(this double value)
        {
            return new VRage.MyFixedPoint() { RawValue = (long)(value * 1000000L) };
        }

        public static VRage.MyFixedPoint AsFloorMyFixedPoint(this float value)
        {
            return new VRage.MyFixedPoint() { RawValue = (long)(value * 1000000L) };
        }

        public static int CompareDistance(double a, double b)
        {
            var diff = a - b;
            return Math.Abs(diff) < 0.00001 ? 0 : (diff > 0 ? 1 : -1);
        }

        public static bool IsCharacterPlayerAndActive(IMyCharacter character)
        {
            return character != null && character.IsPlayer && !character.Closed && character.InScene && !character.IsDead;
        }
    }
}
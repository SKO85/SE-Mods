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
        /// <summary>
        /// Is the block damaged/incomplete/projected
        /// </summary>
        public static bool NeedRepair(this IMySlimBlock target, bool functionalOnly)
        {
            if (target == null) return false;

            var neededIntegrityLevel = GetRequiredIntegrity(target, functionalOnly);
            var needRepair =
                !target.IsDestroyed &&
                (target.FatBlock == null || !target.FatBlock.Closed) &&
                (target.Integrity < neededIntegrityLevel || target.MaxDeformation >= 0.0005f || target.HasDeformation);

            return needRepair;
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
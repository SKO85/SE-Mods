using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using Sandbox.Game.Entities;
using Sandbox.Game;
using Sandbox.Definitions;
using VRage.Game.ModAPI;
using VRage.Game.Components;
using VRageMath;

namespace SpaceEquipmentLtd.Utils
{
   public static class Utils
   {
      /// <summary>
      /// Is the block damaged/incomplete/projected
      /// </summary>
      public static bool NeedRepair(this IMySlimBlock target, bool functionalOnly)
      {
         //I use target.HasDeformation && target.MaxDeformation > X) as I had several times both situations, a landing gear reporting HasDeformation or a block reporting target.MaxDeformation > 0.1 both weren't repairable and caused welding this blocks forever!
         //Now I had the case that target.HasDeformation = true and target.MaxDeformation=0 and the block was deformed -> I removed the double Check
         //target.IsFullyDismounted is equals to target.IsDestroyed
         var neededIntegrityLevel = functionalOnly ? target.MaxIntegrity * ((MyCubeBlockDefinition)target.BlockDefinition).CriticalIntegrityRatio : target.MaxIntegrity;
         return !target.IsDestroyed && (target.FatBlock == null || !target.FatBlock.Closed) && ((target.Integrity < neededIntegrityLevel) || target.HasDeformation);
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
         var box = new MyOrientedBoundingBoxD(new BoundingBoxD(-(halfExtents), (halfExtents)), matrix);
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

      /// <summary>
      /// Check the ownership of the grid
      /// </summary>
      /// <param name="cubeGrid"></param>
      /// <param name="userId"></param>
      /// <returns></returns>
      public static VRage.Game.MyRelationsBetweenPlayerAndBlock GetUserRelationToOwner(this IMyCubeGrid cubeGrid, long userId, bool ignoreCubeGridList = false)
      {
         bool failed;
         return GetUserRelationToOwner(cubeGrid, userId, ignoreCubeGridList, out failed);
      }
      public static VRage.Game.MyRelationsBetweenPlayerAndBlock GetUserRelationToOwner(this IMyCubeGrid cubeGrid, long userId, bool ignoreCubeGridList, out bool failed)
      {
         failed = false;
         var enemies = false;
         var neutral = false;
         try
         {
            if (cubeGrid.BigOwners != null && cubeGrid.BigOwners.Count != 0)
            {
               foreach (var key in cubeGrid.BigOwners)
               {
                  var relation = MyIDModule.GetRelationPlayerBlock(key, userId, VRage.Game.MyOwnershipShareModeEnum.Faction);
                  if (relation == VRage.Game.MyRelationsBetweenPlayerAndBlock.Owner || relation == VRage.Game.MyRelationsBetweenPlayerAndBlock.FactionShare)
                  {
                     return relation;
                  }
                  else if (relation == VRage.Game.MyRelationsBetweenPlayerAndBlock.Enemies)
                  {
                     enemies = true;
                  }
                  else if (relation == VRage.Game.MyRelationsBetweenPlayerAndBlock.Neutral)
                  {
                     neutral = true;
                  }
               }
            } else if (!ignoreCubeGridList) {
               //E.G. the case if a landing gear is directly attatched to piston/rotor (with no ownable block in the same subgrid) and the gear gets connected to something
               var cubegridsList = new List<IMyCubeGrid>();
               MyAPIGateway.GridGroups.GetGroup(cubeGrid, GridLinkTypeEnum.Mechanical, cubegridsList);
               if (cubegridsList != null)
               {
                  foreach (var cubeGrid1 in cubegridsList)
                  {
                     if (cubeGrid1 == cubeGrid) continue;
                     var relation = cubeGrid1.GetUserRelationToOwner(userId, true, out failed); //Do not recurse as this list is already complete
                     if (relation == VRage.Game.MyRelationsBetweenPlayerAndBlock.Owner || relation == VRage.Game.MyRelationsBetweenPlayerAndBlock.FactionShare)
                     {
                        return relation;
                     }
                     else if (relation == VRage.Game.MyRelationsBetweenPlayerAndBlock.Enemies)
                     {
                        enemies = true;
                     }
                     else if (relation == VRage.Game.MyRelationsBetweenPlayerAndBlock.Neutral)
                     {
                        neutral = true;
                     }
                  }
               }
            }
         }
         catch {
            //The list BigOwners could change while iterating -> a silent catch
            failed = (!enemies && !neutral); //Mark as fail if not any high prior ownership is allready set
         }
         if (enemies) return VRage.Game.MyRelationsBetweenPlayerAndBlock.Enemies;
         if (neutral) return VRage.Game.MyRelationsBetweenPlayerAndBlock.Neutral;
         return VRage.Game.MyRelationsBetweenPlayerAndBlock.NoOwnership;
      }

      /// <summary>
      /// Return relation between player and grid, in case of 'NoOwnership' check the grid owner.
      /// </summary>
      /// <param name="slimBlock"></param>
      /// <param name="userId"></param>
      /// <returns></returns>
      public static VRage.Game.MyRelationsBetweenPlayerAndBlock GetUserRelationToOwner(this IMySlimBlock slimBlock, long userId)
      {
         bool failed;
         return GetUserRelationToOwner(slimBlock, userId, out failed);
      }
      public static VRage.Game.MyRelationsBetweenPlayerAndBlock GetUserRelationToOwner(this IMySlimBlock slimBlock, long userId, out bool failed)
      {
         failed = false;
         if (slimBlock == null) return VRage.Game.MyRelationsBetweenPlayerAndBlock.NoOwnership;
         var fatBlock = slimBlock.FatBlock;
         if (fatBlock != null)
         {
            var relation = fatBlock.GetUserRelationToOwner(userId);
            if (relation == VRage.Game.MyRelationsBetweenPlayerAndBlock.NoOwnership)
            {
               relation = GetUserRelationToOwner(slimBlock.CubeGrid, userId, false, out failed);
               return relation;
            }
            else return relation;
         }
         else
         {
            return GetUserRelationToOwner(slimBlock.CubeGrid, userId, false, out failed);
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

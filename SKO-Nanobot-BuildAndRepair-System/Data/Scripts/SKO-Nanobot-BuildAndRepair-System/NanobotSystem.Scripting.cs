using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Utils;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Scripting.MemorySafeTypes;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem
{
    partial class NanobotSystem
    {
        public bool IsShieldProtected(IMySlimBlock slimBlock)
        {
            try
            {
                if (Mod.Settings.ShieldCheckEnabled && slimBlock != null && Mod.Shield != null && Mod.Shield.IsReady)
                {
                    if (slimBlock.CubeGrid.EntityId == Welder.CubeGrid.EntityId)
                        return false;

                    return Mod.Shield.IsBlockProtected(slimBlock);
                }
            }
            catch { }

            return false;
        }

        public bool IsWelderShielded()
        {
            try
            {
                if (Welder != null && Mod.Settings.ShieldCheckEnabled && Mod.Shield != null && Mod.Shield.IsReady)
                {
                    if (Mod.Shield.ProtectedByShield(Welder.CubeGrid))
                    {
                        return Mod.Shield.IsBlockProtected(Welder.SlimBlock);
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        // TODO: heavy calls, imrove this one.
        private bool IsRelationAllowed4Welding(IMySlimBlock block)
        {
            var relation = _Welder.OwnerId == 0 ? MyRelationsBetweenPlayerAndBlock.NoOwnership : block.GetUserRelationToOwner(_Welder.OwnerId);
            if (relation == MyRelationsBetweenPlayerAndBlock.Enemies) return false;
            if (!_Welder.HelpOthers && (relation == MyRelationsBetweenPlayerAndBlock.Neutral || relation == MyRelationsBetweenPlayerAndBlock.NoOwnership)) return false;
            return true;
        }

        private static bool IsColorNearlyEquals(uint colorA, Vector3 colorB)
        {
            return colorA == colorB.PackHSVToUint();
        }

        /// <summary>
        /// Check if block currently has been damaged by friendly(grinder)
        /// </summary>
        public bool IsFriendlyDamage(IMySlimBlock slimBlock)
        {
            return FriendlyDamage.ContainsKey(slimBlock);
        }

        /// <summary>
        /// Clear timedout friendly damaged blocks
        /// </summary>
        private void CleanupFriendlyDamage()
        {
            var playTime = MyAPIGateway.Session.ElapsedPlayTime;
            if (playTime.Subtract(_LastFriendlyDamageCleanup) > Mod.Settings.FriendlyDamageCleanup)
            {
                //Cleanup
                var timedout = new List<IMySlimBlock>();
                foreach (var entry in FriendlyDamage)
                {
                    if (entry.Value < playTime) timedout.Add(entry.Key);
                }
                for (var idx = timedout.Count - 1; idx >= 0; idx--)
                {
                    FriendlyDamage.Remove(timedout[idx]);
                }
                _LastFriendlyDamageCleanup = playTime;
            }
        }

        internal Vector3D? ComputePosition(object target)
        {
            if (target is IMySlimBlock)
            {
                Vector3D endPosition;
                ((IMySlimBlock)target).ComputeWorldCenter(out endPosition);
                return endPosition;
            }
            else if (target is IMyEntity) return ((IMyEntity)target).WorldMatrix.Translation;
            else if (target is Vector3D) return (Vector3D)target;
            return null;
        }

        /// <summary>
        /// Get a list of currently missing components (Scripting)
        /// </summary>
        /// <returns></returns>
        internal MemorySafeDictionary<MyDefinitionId, int> GetMissingComponentsDict()
        {
            var dict = new MemorySafeDictionary<MyDefinitionId, int>();
            lock (State.MissingComponents)
            {
                foreach (var item in State.MissingComponents)
                {
                    dict.Add(item.Key, item.Value);
                }
            }
            return dict;
        }

        /// <summary>
        /// Get a list of currently build/repairable blocks (Scripting)
        /// </summary>
        /// <returns></returns>
        internal MemorySafeList<VRage.Game.ModAPI.Ingame.IMySlimBlock> GetPossibleWeldTargetsList()
        {
            var list = new MemorySafeList<VRage.Game.ModAPI.Ingame.IMySlimBlock>();
            lock (State.PossibleWeldTargets)
            {
                foreach (var blockData in State.PossibleWeldTargets)
                {
                    if (!blockData.Ignore) list.Add(blockData.Block);
                }
            }
            return list;
        }

        /// <summary>
        /// Get a list of currently grind blocks (Scripting)
        /// </summary>
        /// <returns></returns>
        internal MemorySafeList<VRage.Game.ModAPI.Ingame.IMySlimBlock> GetPossibleGrindTargetsList()
        {
            var list = new MemorySafeList<VRage.Game.ModAPI.Ingame.IMySlimBlock>();
            lock (State.PossibleGrindTargets)
            {
                foreach (var blockData in State.PossibleGrindTargets)
                {
                    if (!blockData.Ignore) list.Add(blockData.Block);
                }
            }
            return list;
        }

        /// <summary>
        /// Get a list of currently collectable floating objects (Scripting)
        /// </summary>
        /// <returns></returns>
        internal MemorySafeList<VRage.Game.ModAPI.Ingame.IMyEntity> GetPossibleCollectingTargetsList()
        {
            var list = new MemorySafeList<VRage.Game.ModAPI.Ingame.IMyEntity>();
            lock (State.PossibleFloatingTargets)
            {
                foreach (var floatingData in State.PossibleFloatingTargets)
                {
                    if (!floatingData.Ignore) list.Add(floatingData.Entity);
                }
            }
            return list;
        }
    }
}

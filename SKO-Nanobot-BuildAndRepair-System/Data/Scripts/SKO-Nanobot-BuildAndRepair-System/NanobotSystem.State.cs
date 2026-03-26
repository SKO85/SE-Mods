using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Handlers;
using SKONanobotBuildAndRepairSystem.Profiling;
using SKONanobotBuildAndRepairSystem.Utils;
using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem
{
    public partial class NanobotSystem
    {
        internal bool SetSafeZoneAndShieldStates()
        {
            var profilerTs = MethodProfiler.Start();
            var safeZoneActionsState = SafeZoneHandler.GetActionsAllowedForSystem(this);

            var safezoneAllowsWelding = safeZoneActionsState.IsWeldingAllowed;
            var safeZoneAllowsBuildingProjections = safeZoneActionsState.IsBuildingProjectionsAllowed;
            var safeZoneAllowsGrinding = safeZoneActionsState.IsGrindingAllowed;
            var welderIsShielded = IsWelderShielded();

            var changed = false;

            if (State.SafeZoneAllowsWelding != safezoneAllowsWelding)
            {
                State.SafeZoneAllowsWelding = safezoneAllowsWelding;
                changed = true;
            }

            if (State.SafeZoneAllowsBuildingProjections != safeZoneAllowsBuildingProjections)
            {
                State.SafeZoneAllowsBuildingProjections = safeZoneAllowsBuildingProjections;
                changed = true;
            }

            if (State.SafeZoneAllowsGrinding != safeZoneAllowsGrinding)
            {
                State.SafeZoneAllowsGrinding = safeZoneAllowsGrinding;
                changed = true;
            }

            if (State.IsShielded != welderIsShielded)
            {
                State.IsShielded = welderIsShielded;
                changed = true;
            }

            if (!State.SafeZoneAndShieldsChecked)
            {
                State.SafeZoneAndShieldsChecked = true;
                changed = true;
            }

            MethodProfiler.StopAndLog("SetSafeZoneAndShieldStates", profilerTs, () =>
                string.Format("entityId={0};changed={1};zones={2}",
                    _Welder != null ? _Welder.EntityId : 0, changed, SafeZoneHandler.Zones.Count));
            return changed;
        }

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

        // Phase 3: Optimize — relation lookup is called per-block per-scan, consider caching per-grid.
        private bool IsRelationAllowed4Welding(IMySlimBlock block)
        {
            var relation = _Welder.OwnerId == 0 ? MyRelationsBetweenPlayerAndBlock.NoOwnership : block.GetUserRelationToOwner(_Welder.OwnerId);
            if (relation == MyRelationsBetweenPlayerAndBlock.Enemies) return false;
            if (relation == MyRelationsBetweenPlayerAndBlock.Neutral || relation == MyRelationsBetweenPlayerAndBlock.NoOwnership) return false;
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
                    TimeSpan removed;
                    FriendlyDamage.TryRemove(timedout[idx], out removed);
                }
                _LastFriendlyDamageCleanup = playTime;
            }
        }

        public WorkingState GetWorkingState()
        {
            // Not Ready.
            if (!State.Ready)
                return WorkingState.NotReady;

            // Welding — validate target is still in progress so effects don't linger
            // between staggered updates (~8s gap).
            else if (State.Welding)
            {
                var block = State.CurrentWeldingBlock;
                if (block == null || block.IsDestroyed
                    || (block.CubeGrid != null && block.CubeGrid.Closed)
                    || (!block.IsProjected() && block.IsFullIntegrity))
                    return WorkingState.NeedWelding;
                return WorkingState.Welding;
            }

            // Need welding.
            else if (State.NeedWelding)
            {
                if (State.MissingComponents.Count > 0)
                    return WorkingState.MissingComponents;

                if (State.LimitsExceeded)
                    return WorkingState.LimitsExceeded;

                return WorkingState.NeedWelding;
            }

            // Grinding — validate target is still in progress.
            else if (State.Grinding)
            {
                var block = State.CurrentGrindingBlock;
                if (block == null || block.IsDestroyed || block.IsFullyDismounted
                    || (block.CubeGrid != null && block.CubeGrid.Closed))
                    return WorkingState.NeedGrinding;
                return WorkingState.Grinding;
            }

            // Need grinding.
            else if (State.NeedGrinding)
            {
                if (State.InventoryFull)
                    return WorkingState.InventoryFull;

                return WorkingState.NeedGrinding;
            }

            // Idle.
            return WorkingState.Idle;
        }

        public string GetStateString()
        {
            if (State.Grinding)
                return "Grinding";

            if (State.Welding)
                return "Welding";

            if (State.NeedWelding && State.Transporting)
                return "Welding (Transporting)";

            if (State.NeedGrinding && State.Transporting)
                return "Grinding (Transporting)";

            if (State.NeedCollecting && State.Transporting)
                return "Collecting (Transporting)";

            if (State.Transporting)
                return "Transporting";

            return "Idle";
        }
    }
}

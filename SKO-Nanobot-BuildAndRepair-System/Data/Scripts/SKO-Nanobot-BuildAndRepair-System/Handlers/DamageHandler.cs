using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using SKONanobotBuildAndRepairSystem.Extensions;
using SKONanobotBuildAndRepairSystem.Utils;
using System;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;

namespace SKONanobotBuildAndRepairSystem.Handlers
{
    public static class DamageHandler
    {
        #region Registration

        private static bool _registered = false;

        public static void Register()
        {
            if (_registered || MyAPIGateway.Session == null)
                return;

            // Damage detection on both server and client.
            MyAPIGateway.Session.DamageSystem.RegisterBeforeDamageHandler(0, OnBeforeDamage);

            if (MyAPIGateway.Session.IsServer)
            {
                // Detect friendly damage (only needed on server)
                MyAPIGateway.Session.DamageSystem.RegisterAfterDamageHandler(100, OnAfterDamage);
            }

            _registered = true;
        }

        public static void Unregister()
        {
            if (!_registered || MyAPIGateway.Session == null)
                return;

            // SE API limitation: No unregister methods available for damage handlers.
            // RegisterBeforeDamageHandler/RegisterAfterDamageHandler are permanent for the session lifetime.
            // Setting _registered = false prevents duplicate registration on reload.

            _registered = false;
        }

        #endregion Registration

        /// <summary>
        /// Damage Handler: Prevent Damage from BuildAndRepairSystem
        /// </summary>
        public static void OnBeforeDamage(object target, ref MyDamageInformation info)
        {
            try
            {
                if (info.Type == MyDamageType.Weld)
                {
                    if (target is IMyCharacter)
                    {
                        NanobotSystem logicalComponent;
                        Mod.NanobotSystems.TryGetValue(info.AttackerId, out logicalComponent);
                        if (logicalComponent != null)
                        {
                            var terminalBlock = logicalComponent.Entity as IMyTerminalBlock;
                            info.Amount = 0;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logging.Instance.Error("BuildAndRepairSystemMod: Exception in BeforeDamageHandlerNoDamageByBuildAndRepairSystem: Source={0}, Message={1}", e.Source, e.Message);
            }
        }

        /// <summary>
        /// Damage Handler: Register friendly damage
        /// </summary>
        public static void OnAfterDamage(object target, MyDamageInformation info)
        {
            try
            {
                if (info.Type == MyDamageType.Grind && info.Amount > 0)
                {
                    var targetBlock = target as IMySlimBlock;
                    if (targetBlock != null)
                    {
                        IMyEntity attackerEntity;
                        MyAPIGateway.Entities.TryGetEntityById(info.AttackerId, out attackerEntity);

                        var attackerId = 0L;

                        var shipGrinder = attackerEntity as IMyShipGrinder;
                        if (shipGrinder != null)
                        {
                            attackerId = shipGrinder.OwnerId;
                        }
                        else
                        {
                            var characterGrinder = attackerEntity as IMyEngineerToolBase;
                            if (characterGrinder != null)
                            {
                                attackerId = characterGrinder.OwnerIdentityId;
                            }
                        }

                        if (attackerId != 0)
                        {
                            // BUG-130: write to the shared owner-keyed map. Distinct welder-owner
                            // ids only — N BaRs sharing one owner now produce 1 write, not N.
                            // The attacker may be an arbitrary player without a BaR, so the
                            // _FriendlyOwnersByOwner cache (built from BaR welder owners only) is
                            // not always populated for `attackerId`; iterate NanobotSystems here.
                            // Damage events are rare so the N-walk is acceptable.
                            var deadline = MyAPIGateway.Session.ElapsedPlayTime + Mod.Settings.FriendlyDamageTimeout;
                            var seenOwners = new System.Collections.Generic.HashSet<long>();
                            foreach (var entry in Mod.NanobotSystems)
                            {
                                var welder = entry.Value != null ? entry.Value.Welder : null;
                                if (welder == null) continue;
                                var welderOwner = welder.OwnerId;
                                if (welderOwner == 0) continue;
                                if (!seenOwners.Add(welderOwner)) continue;
                                var relation = welder.GetUserRelationToOwner(attackerId);
                                if (MyRelationsBetweenPlayerAndBlockExtensions.IsFriendly(relation))
                                {
                                    // A 'friendly' damage from grinder -> do not repair (for a while)
                                    Mod.MarkFriendlyDamage(welderOwner, targetBlock, deadline);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logging.Instance.Error("BuildAndRepairSystemMod: Exception in BeforeDamageHandlerNoDamageByBuildAndRepairSystem: Source={0}, Message={1}", e.Source, e.Message);
            }
        }
    }
}
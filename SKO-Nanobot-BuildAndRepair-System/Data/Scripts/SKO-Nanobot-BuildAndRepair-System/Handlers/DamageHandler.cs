using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using SKONanobotBuildAndRepairSystem.Utils;
using System;
using System.Collections.Generic;
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

            // No specific unregister calls available.
            // TODO: Check with Devs if there is a unregister call for this somewhere else.

            _registered = false;
        }
        #endregion

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
                        var logicalComponent = Mod.NanobotSystems.GetValueOrDefault(info.AttackerId);
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
                            foreach (var entry in Mod.NanobotSystems)
                            {
                                var relation = entry.Value.Welder.GetUserRelationToOwner(attackerId);

                                if (MyRelationsBetweenPlayerAndBlockExtensions.IsFriendly(relation))
                                {
                                    // A 'friendly' damage from grinder -> do not repair (for a while)
                                    entry.Value.FriendlyDamage[targetBlock] = MyAPIGateway.Session.ElapsedPlayTime + Mod.Settings.FriendlyDamageTimeout;
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

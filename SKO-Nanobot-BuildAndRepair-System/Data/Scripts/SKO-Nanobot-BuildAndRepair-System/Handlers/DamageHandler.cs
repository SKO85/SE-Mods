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
            // SE API limitation: No unregister methods available for damage handlers.
            // RegisterBeforeDamageHandler/RegisterAfterDamageHandler are permanent for the session lifetime.
            // Setting _registered = false prevents duplicate registration on reload.

            // BUG-260610.27: reset unconditionally. The previous early return on a
            // null Session latched _registered = true forever, so the next world's
            // Register() no-oped and BaR weld damage hurt characters.
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
                            // BUG-130: write to the shared owner-keyed map (one write per distinct owner).
                            // BUG-260610.24: consume FriendlyRelationsHandler's 15 s rebuilt
                            // cache instead of walking every BaR and querying engine faction
                            // relations on each damage event — grinders emit many per second,
                            // making the old walk hundreds of relation calls/s on busy servers.
                            List<long> friendlyOwners;
                            if (FriendlyRelationsHandler.TryGetOwnersForOwner(attackerId, out friendlyOwners))
                            {
                                // A 'friendly' damage from grinder -> do not repair (for a while)
                                var deadline = MyAPIGateway.Session.ElapsedPlayTime + Mod.Settings.FriendlyDamageTimeout;
                                for (var i = 0; i < friendlyOwners.Count; i++)
                                {
                                    Mod.MarkFriendlyDamage(friendlyOwners[i], targetBlock, deadline);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logging.Instance.Error("BuildAndRepairSystemMod: Exception in AfterDamageHandlerFriendlyGrind: Source={0}, Message={1}", e.Source, e.Message);
            }
        }
    }
}

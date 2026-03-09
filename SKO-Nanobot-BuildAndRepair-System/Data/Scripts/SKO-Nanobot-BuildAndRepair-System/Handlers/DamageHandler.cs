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

        // Reused per damage event to cache relation lookups by barOwnerId.
        // Avoids redundant GetUserRelationToOwner calls when many BaRs share the same owner.
        // OnAfterDamage is invoked on the game logic thread (single-threaded), so no lock needed.
        private static readonly Dictionary<long, bool> _eventRelationCache = new Dictionary<long, bool>();

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

        #endregion Registration

        /// <summary>
        /// Damage Handler: Prevent Damage from BuildAndRepairSystem
        /// </summary>
        public static void OnBeforeDamage(object target, ref MyDamageInformation info)
        {
            // Guard: no-op if the handler has been logically unregistered (SE has no unregister API).
            if (!_registered) return;
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
            // Guard: no-op if the handler has been logically unregistered (SE has no unregister API).
            if (!_registered) return;
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
                            var expiry = MyAPIGateway.Session.ElapsedPlayTime + Mod.Settings.FriendlyDamageTimeout;
                            _eventRelationCache.Clear();
                            foreach (var entry in Mod.NanobotSystems)
                            {
                                var barOwnerId = entry.Value.Welder.OwnerId;
                                bool isFriendly;
                                if (!_eventRelationCache.TryGetValue(barOwnerId, out isFriendly))
                                {
                                    var relation = entry.Value.Welder.GetUserRelationToOwner(attackerId);
                                    isFriendly = MyRelationsBetweenPlayerAndBlockExtensions.IsFriendly(relation);
                                    _eventRelationCache[barOwnerId] = isFriendly;
                                }

                                if (isFriendly)
                                {
                                    // A 'friendly' damage from grinder -> do not repair (for a while)
                                    entry.Value.FriendlyDamage[targetBlock] = expiry;
                                }
                            }
                            _eventRelationCache.Clear();
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logging.Instance.Error("BuildAndRepairSystemMod: Exception in OnAfterDamage: Source={0}, Message={1}", e.Source, e.Message);
            }
        }
    }
}
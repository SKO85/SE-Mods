using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using System;
using Sandbox.ModAPI.Weapons;

namespace SKONanobotBuildAndRepairSystem
{
    public static class DamageHandler
    {
        public static void Register()
        {
            if (MyAPIGateway.Session == null || MyAPIGateway.Session.DamageSystem == null)
                return;

            MyAPIGateway.Session.DamageSystem.RegisterBeforeDamageHandler(0, OnBeforeDamage);

            if (MyAPIGateway.Session.IsServer)
            {
                MyAPIGateway.Session.DamageSystem.RegisterAfterDamageHandler(100, OnAfterDamage);
            }
        }

        private static void OnBeforeDamage(object target, ref MyDamageInformation info)
        {
            try
            {
                if (info.Type != MyDamageType.Weld)
                    return;

                if (target is IMyCharacter)
                {
                    NanobotBuildAndRepairSystemBlock system;
                    if (NanobotBuildAndRepairSystemMod.BuildAndRepairSystems.TryGetValue(info.AttackerId, out system))
                    {
                        var terminalBlock = system.Entity as IMyTerminalBlock;

                        Logging.Instance?.Write(
                            Logging.Level.Communication,
                            "Prevented Weld Damage from {0}, Amount={1}",
                            terminalBlock != null ? terminalBlock.CustomName : (system.Entity != null ? system.Entity.DisplayName : "<null>"),
                            info.Amount);

                        info.Amount = 0f;
                    }
                }
            }
            catch (Exception e) 
            {
                Logging.Instance?.Error($"Exception in DamageHandler.OnBeforeDamage. Source={e.Source}, Message={e.Message}");
            }
        }

        private static void OnAfterDamage(object target, MyDamageInformation info)
        {
            if (info.Type != MyDamageType.Grind || info.Amount <= 0)
                return;

            var targetBlock = target as IMySlimBlock;
            if (targetBlock == null)
                return;

            IMyEntity attackerEntity;
            if (!MyAPIGateway.Entities.TryGetEntityById(info.AttackerId, out attackerEntity))
                return;

            var attackerId = 0L;
            var grinder = attackerEntity as IMyShipGrinder;
            if (grinder != null)
            {
                attackerId = grinder.OwnerId;
            }
            else
            {
                var tool = attackerEntity as IMyEngineerToolBase;
                if (tool != null)
                {
                    attackerId = tool.OwnerIdentityId;
                }
            }

            if (attackerId == 0)
                return;

            foreach (var system in NanobotBuildAndRepairSystemMod.BuildAndRepairSystems.Values)
            {
                var relation = system.Welder.GetUserRelationToOwner(attackerId);

                if (relation.IsFriendly())
                {
                    var timeout = MyAPIGateway.Session.ElapsedPlayTime + NanobotBuildAndRepairSystemMod.Settings.FriendlyDamageTimeout;
                    system.FriendlyDamage[targetBlock] = timeout;

                    Logging.Instance?.Write(
                        Logging.Level.Info,
                        "Recorded friendly damage: {0} from {1}, timeout set to {2}",
                        Logging.BlockName(targetBlock), 
                        Logging.BlockName(attackerEntity), 
                        timeout);
                }
            }
        }
    }
}

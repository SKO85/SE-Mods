using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace SKONanobotBuildAndRepairSystem.Utils
{
    public static class UtilsFaction
    {
        public static int DamageAmount = 5;

        public static void DamageReputationWithPlayerFaction(long sourcePlayerId, long targetPlayerId)
        {
            if (MyAPIGateway.Session == null)
                return;

            var targetFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(targetPlayerId);
            if (targetFaction != null)
            {
                var player = UtilsPlayer.GetPlayer(sourcePlayerId);
                if (player == null)
                    return;

                var myFaction = GetPlayerFaction(player.IdentityId);
                if (myFaction == null || (myFaction != null && myFaction.FactionId != targetFaction.FactionId))
                {
                    var currentReputation = MyVisualScriptLogicProvider.GetRelationBetweenPlayerAndFaction(sourcePlayerId, targetFaction.Tag);
                    if (currentReputation > -1500)
                    {
                        var newReputation = currentReputation - DamageAmount;
                        if (newReputation < -1500)
                            newReputation = -1500;

                        MyVisualScriptLogicProvider.SetRelationBetweenPlayerAndFaction(sourcePlayerId, targetFaction.Tag, newReputation);
                    }
                }
            }
        }

        public static IMyFaction GetPlayerFaction(long playerId)
        {
            return MyAPIGateway.Session.Factions.TryGetPlayerFaction(playerId);
        }
    }
}
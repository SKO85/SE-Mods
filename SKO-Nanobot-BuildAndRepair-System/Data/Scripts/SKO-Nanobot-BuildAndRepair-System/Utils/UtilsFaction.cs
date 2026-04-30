using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace SKONanobotBuildAndRepairSystem.Utils
{
    public static class UtilsFaction
    {
        public const int DamageAmount = 5;
        private const int MinFactionReputation = -1500;

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
                if (myFaction == null || myFaction.FactionId != targetFaction.FactionId)
                {
                    var currentReputation = MyVisualScriptLogicProvider.GetRelationBetweenPlayerAndFaction(sourcePlayerId, targetFaction.Tag);
                    if (currentReputation > MinFactionReputation)
                    {
                        var newReputation = currentReputation - DamageAmount;
                        if (newReputation < MinFactionReputation)
                            newReputation = MinFactionReputation;

                        MyVisualScriptLogicProvider.SetRelationBetweenPlayerAndFaction(sourcePlayerId, targetFaction.Tag, newReputation);
                    }
                }
            }
        }

        private static IMyFaction GetPlayerFaction(long playerId)
        {
            return MyAPIGateway.Session.Factions.TryGetPlayerFaction(playerId);
        }
    }
}
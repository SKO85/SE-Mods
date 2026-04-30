using Sandbox.ModAPI;
using System.Collections.Generic;
using System.Linq;
using VRage.Game.ModAPI;

namespace SKONanobotBuildAndRepairSystem.Utils
{
    public static class UtilsPlayer
    {
        public static IMyPlayer GetPlayer(long identityId)
        {
            var players = GetAllPlayers();
            var player = players.FirstOrDefault(c => c.IdentityId == identityId);
            return player;
        }

        public static List<IMyPlayer> GetAllPlayers()
        {
            var list = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(list);
            return list;
        }

        public static long GetOwner(IMyCubeGrid grid)
        {
            var gridOwnerList = grid.BigOwners;
            var ownerCnt = gridOwnerList.Count;
            var gridOwner = 0L;

            if (ownerCnt > 0 && gridOwnerList[0] != 0)
                return gridOwnerList[0];
            else if (ownerCnt > 1)
                return gridOwnerList[1];

            return gridOwner;
        }
    }
}
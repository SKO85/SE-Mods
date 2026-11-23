using Sandbox.ModAPI;
using System.Collections.Generic;
using VRage.Game.ModAPI;
using VRage.Game;

namespace SKONanobotBuildAndRepairSystem.Utils
{
    public static class UtilsPlayer
    {
        /// <summary>
        /// BUG-260502.3: canonical admin-role check. Direct enum compare avoids
        /// the `.ToString()` allocation and is rename-proof if SE renames an
        /// enum member. Mirrors the `// REF-4:` pattern in
        /// `NetworkMessagingHandler.IsRemoteAdmin`.
        /// </summary>
        public static bool IsAdminLevel(MyPromoteLevel level)
        {
            return level == MyPromoteLevel.Admin
                || level == MyPromoteLevel.SpaceMaster
                || level == MyPromoteLevel.Owner;
        }


        // FEAT-261123.5: reused list (main thread callers only) instead of a new one per lookup.
        private static readonly List<IMyPlayer> _players = new List<IMyPlayer>();

        public static IMyPlayer GetPlayer(long identityId)
        {
            if (MyAPIGateway.Players == null) return null;
            _players.Clear();
            MyAPIGateway.Players.GetPlayers(_players);
            IMyPlayer found = null;
            for (var i = 0; i < _players.Count; i++)
            {
                if (_players[i].IdentityId == identityId) { found = _players[i]; break; }
            }
            _players.Clear();
            return found;
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
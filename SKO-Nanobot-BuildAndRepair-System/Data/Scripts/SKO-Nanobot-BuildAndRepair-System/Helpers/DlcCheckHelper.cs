using Sandbox.Definitions;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Utils;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.ModAPI;

namespace SKONanobotBuildAndRepairSystem.Helpers
{
    public static class DlcCheckHelper
    {
        // Cache 1: Block DLC requirements — permanent within a session (definitions never change)
        private static readonly Dictionary<MyDefinitionId, string[]> _BlockDlcCache =
            new Dictionary<MyDefinitionId, string[]>(MyDefinitionId.Comparer);
        private static readonly object _BlockDlcLock = new object();

        // Cache 2: Owner DLC ownership — cleared periodically via CleanupOwnerCache
        private static readonly Dictionary<long, HashSet<string>> _OwnerDlcCache =
            new Dictionary<long, HashSet<string>>();
        private static readonly object _OwnerDlcLock = new object();

        /// <summary>
        /// Returns true if the owner has all DLCs required to build the given projected block.
        /// Returns true immediately when the block requires no DLC.
        /// Returns false when the owner cannot be found (NPC / offline) or is missing a required DLC.
        /// </summary>
        public static bool IsBlockDlcAvailableForOwner(IMySlimBlock block, long ownerId)
        {
            var requiredDlcs = GetRequiredDlcs(block);
            if (requiredDlcs.Length == 0)
                return true;

            var ownedDlcs = GetOwnedDlcs(ownerId);
            foreach (var dlc in requiredDlcs)
            {
                if (!ownedDlcs.Contains(dlc))
                    return false;
            }
            return true;
        }

        private static string[] GetRequiredDlcs(IMySlimBlock block)
        {
            var defId = block.BlockDefinition.Id;

            lock (_BlockDlcLock)
            {
                string[] cached;
                if (_BlockDlcCache.TryGetValue(defId, out cached))
                    return cached;
            }

            var def = MyDefinitionManager.Static.GetCubeBlockDefinition(defId);
            var dlcs = def != null && def.DLCs != null ? def.DLCs : new string[0];

            lock (_BlockDlcLock)
            {
                _BlockDlcCache[defId] = dlcs;
            }
            return dlcs;
        }

        private static HashSet<string> GetOwnedDlcs(long ownerId)
        {
            lock (_OwnerDlcLock)
            {
                HashSet<string> cached;
                if (_OwnerDlcCache.TryGetValue(ownerId, out cached))
                    return cached;
            }

            var player = UtilsPlayer.GetPlayer(ownerId);
            var owned = new HashSet<string>();

            if (player != null && !player.IsBot)
            {
                foreach (var kvp in MyAPIGateway.DLC.GetDLCs())
                {
                    
                    if (MyAPIGateway.DLC.HasDLC(kvp.Name, player.SteamUserId))
                        owned.Add(kvp.Name);
                }
            }

            lock (_OwnerDlcLock)
            {
                _OwnerDlcCache[ownerId] = owned;
            }
            return owned;
        }

        /// <summary>
        /// Clears the owner DLC ownership cache. Called periodically from the TTL cleanup task.
        /// The block DLC requirement cache is never cleared (definitions are permanent within a session).
        /// </summary>
        public static void CleanupOwnerCache()
        {
            lock (_OwnerDlcLock)
            {
                _OwnerDlcCache.Clear();
            }
        }
    }
}

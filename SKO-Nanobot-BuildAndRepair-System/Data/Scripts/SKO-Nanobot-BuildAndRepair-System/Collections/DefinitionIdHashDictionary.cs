using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Utils;
using System.Collections.Generic;
using VRage.Game;

namespace SKONanobotBuildAndRepairSystem.Collections
{
    public class DefinitionIdHashDictionary : HashDictionary<MyDefinitionId, int, SyncComponents>
    {
        public override List<SyncComponents> GetSyncList()
        {
            var result = new List<SyncComponents>();
            var idx = 0;
            foreach (var item in this)
            {
                result.Add(new SyncComponents() { Component = item.Key, Amount = item.Value });
                idx++;
                if (idx > SyncBlockState.MaxSyncItems) break;
            }
            return result;
        }

        public override void RebuildHash()
        {
            uint hash = 0;
            var idx = 0;
            lock (this)
            {
                foreach (var entry in this)
                {
                    hash ^= UtilsSynchronization.RotateLeft((uint)entry.GetHashCode(), idx + 1);
                    idx++;
                    if (idx >= SyncBlockState.MaxSyncItems) break;
                }
                CurrentCount = Count;
                CurrentHash = hash;
            }
        }
    }
}
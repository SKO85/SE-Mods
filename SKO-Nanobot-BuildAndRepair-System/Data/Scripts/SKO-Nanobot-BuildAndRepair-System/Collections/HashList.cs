using System.Collections.Generic;

namespace SKONanobotBuildAndRepairSystem.Collections
{
    public abstract class HashList<T, ST> : List<T>
    {
        private long _CurrentHash;
        private long _LastHash;
        private int _CurrentCount;

        public long CurrentHash
        { get { return _CurrentHash; } protected set { _CurrentHash = value; } }

        public long LastHash
        { get { return _LastHash; } set { _LastHash = value; } }

        public int CurrentCount
        { get { return _CurrentCount; } protected set { _CurrentCount = value; } }

        public abstract void RebuildHash();

        public abstract List<ST> GetSyncList();

        public void ChangeHash()
        {
            CurrentHash++;
        }
    }
}
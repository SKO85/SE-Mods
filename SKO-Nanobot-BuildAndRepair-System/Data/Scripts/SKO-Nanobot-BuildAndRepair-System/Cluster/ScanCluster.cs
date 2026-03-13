using System.Collections.Generic;

namespace SKONanobotBuildAndRepairSystem.Cluster
{
    /// <summary>
    /// Represents a group of BaR systems with identical scan configurations.
    /// One member (the coordinator) performs the expensive scan; others apply
    /// their own range/distance filtering to the shared result.
    /// </summary>
    public class ScanCluster
    {
        public string ClusterKey;
        public List<NanobotSystem> Members;
        public NanobotSystem Coordinator;
        public bool ScanInProgress;

        /// <summary>
        /// Shared result published by the coordinator. Read by members via atomic reference read.
        /// </summary>
        private ScanClusterResult _sharedResult;

        public ScanCluster(string clusterKey)
        {
            ClusterKey = clusterKey;
            Members = new List<NanobotSystem>();
        }

        /// <summary>
        /// Elects the coordinator as the member with the lowest Welder EntityId.
        /// Keeps the current coordinator if still a valid member.
        /// </summary>
        public void ElectCoordinator()
        {
            if (Members.Count == 0)
            {
                Coordinator = null;
                return;
            }

            NanobotSystem best = Members[0];
            for (int i = 1; i < Members.Count; i++)
            {
                if (Members[i].Welder.EntityId < best.Welder.EntityId)
                {
                    best = Members[i];
                }
            }
            Coordinator = best;
        }

        public bool IsCoordinator(NanobotSystem system)
        {
            return Coordinator == system;
        }

        /// <summary>
        /// Atomic reference assign — called by the coordinator after building the result.
        /// </summary>
        public void SetResult(ScanClusterResult result)
        {
            _sharedResult = result;
        }

        /// <summary>
        /// Atomic reference read — called by members to get the shared result.
        /// </summary>
        public ScanClusterResult GetResult()
        {
            return _sharedResult;
        }
    }
}

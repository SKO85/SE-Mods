using System;
using System.Collections.Generic;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Cluster
{
    /// <summary>
    /// A block candidate that passed all position-INDEPENDENT checks
    /// (ownership, color, safe zone, shield, priority enabled, needs repair, etc.)
    /// but NOT IsInRange — each cluster member applies its own range filter.
    /// </summary>
    public class ClusterTargetCandidate
    {
        public IMySlimBlock Block;
        public Models.TargetBlockData.AttributeFlags Attributes;

        public ClusterTargetCandidate(IMySlimBlock block, Models.TargetBlockData.AttributeFlags attributes)
        {
            Block = block;
            Attributes = attributes;
        }
    }

    /// <summary>
    /// A floating object / character / inventory bag candidate for cluster sharing.
    /// </summary>
    public class ClusterFloatingCandidate
    {
        public IMyEntity Entity;
        public Vector3D WorldPosition;

        public ClusterFloatingCandidate(IMyEntity entity, Vector3D worldPosition)
        {
            Entity = entity;
            WorldPosition = worldPosition;
        }
    }

    /// <summary>
    /// Shared immutable scan output produced by the cluster coordinator.
    /// Created fresh each scan cycle, populated by the coordinator, then published
    /// via atomic reference swap. Members only read — no mutation after publication.
    /// </summary>
    public class ScanClusterResult
    {
        public List<ClusterTargetCandidate> WeldCandidates;
        public List<ClusterTargetCandidate> GrindCandidates;
        public List<ClusterFloatingCandidate> FloatingCandidates;

        public List<IMyInventory> Sources;
        public List<IMyInventory> PushTargets;
        public bool SourcesUpdated;

        public TimeSpan Timestamp;

        public ScanClusterResult()
        {
            WeldCandidates = new List<ClusterTargetCandidate>();
            GrindCandidates = new List<ClusterTargetCandidate>();
            FloatingCandidates = new List<ClusterFloatingCandidate>();
            Sources = new List<IMyInventory>();
            PushTargets = new List<IMyInventory>();
        }
    }
}

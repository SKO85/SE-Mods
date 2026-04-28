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
    ///
    /// BUG-111: struct (was class). 100-1000 candidates are constructed per cluster
    /// scan; as a class each `new ClusterTargetCandidate(...)` was a heap allocation
    /// pressuring gen-1 GC. Struct stores items inline in the backing List, eliminating
    /// per-candidate heap allocs. Audited for in-place mutation patterns
    /// (`var x = list[i]; x.Field = ...`) — none found, so the conversion is safe.
    /// Sort + QuickSelect work identically because they swap whole elements.
    /// </summary>
    public struct ClusterTargetCandidate
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
    /// BUG-111: struct (was class) for the same reason as ClusterTargetCandidate.
    /// </summary>
    public struct ClusterFloatingCandidate
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
        public bool PreSorted;

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

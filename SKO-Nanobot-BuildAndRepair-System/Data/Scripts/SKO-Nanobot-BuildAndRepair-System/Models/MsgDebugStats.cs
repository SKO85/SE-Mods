using ProtoBuf;

namespace SKONanobotBuildAndRepairSystem.Models
{
    [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
    public class MsgDebugStats
    {
        // Systems
        [ProtoMember(1)]
        public int TotalSystems { get; set; }
        [ProtoMember(2)]
        public int Active { get; set; }
        [ProtoMember(3)]
        public int Welding { get; set; }
        [ProtoMember(4)]
        public int Grinding { get; set; }
        [ProtoMember(5)]
        public int Collecting { get; set; }
        [ProtoMember(6)]
        public int Transporting { get; set; }
        [ProtoMember(7)]
        public int InventoryFull { get; set; }
        [ProtoMember(8)]
        public int ComponentStarved { get; set; }
        [ProtoMember(9)]
        public int SafeZoneBlocked { get; set; }

        // Work modes
        [ProtoMember(10)]
        public int ModeWeldBefore { get; set; }
        [ProtoMember(11)]
        public int ModeGrindBefore { get; set; }
        [ProtoMember(12)]
        public int ModeStuck { get; set; }
        [ProtoMember(13)]
        public int ModeWeldOnly { get; set; }
        [ProtoMember(14)]
        public int ModeGrindOnly { get; set; }
        [ProtoMember(15)]
        public int SearchGrids { get; set; }
        [ProtoMember(16)]
        public int SearchBBox { get; set; }

        // Targets (unique per cluster)
        [ProtoMember(20)]
        public int WeldTargets { get; set; }
        [ProtoMember(21)]
        public int GrindTargets { get; set; }
        [ProtoMember(22)]
        public int FloatTargets { get; set; }

        // Performance
        [ProtoMember(30)]
        public int Clusters { get; set; }
        [ProtoMember(31)]
        public int Stagger { get; set; }
        [ProtoMember(32)]
        public int GrindBudgetMax { get; set; }
        [ProtoMember(33)]
        public int GrindBudgetPeak { get; set; }
        [ProtoMember(34)]
        public float SimSpeed { get; set; }
        [ProtoMember(35)]
        public int BgTasksEnqueued { get; set; }
        [ProtoMember(36)]
        public int BgTasksPeakRunning { get; set; }
        [ProtoMember(37)]
        public float OldestScanAgeSec { get; set; }
        [ProtoMember(38)]
        public int EmptyGridSkip { get; set; }

        // Assignments
        [ProtoMember(40)]
        public int BlockAssignments { get; set; }
        [ProtoMember(41)]
        public int MaxSysPerGrid { get; set; }

        // Caches
        [ProtoMember(50)]
        public int SafeZoneCount { get; set; }
        [ProtoMember(51)]
        public int SafeZoneGridCache { get; set; }
        [ProtoMember(52)]
        public int SafeZoneBlockCache { get; set; }
        [ProtoMember(53)]
        public int SafeZoneGrindCache { get; set; }
        [ProtoMember(54)]
        public int OwnershipCache { get; set; }
        [ProtoMember(55)]
        public int BlockPriorityCache { get; set; }

        // Server info
        [ProtoMember(60)]
        public int PlayerCount { get; set; }
        [ProtoMember(61)]
        public float TickCostAvgMs { get; set; }
        [ProtoMember(62)]
        public float TickCostPeakMs { get; set; }
        [ProtoMember(63)]
        public int SyncSent { get; set; }
        [ProtoMember(64)]
        public int SyncSkipped { get; set; }

        // Profiling
        [ProtoMember(70)]
        public bool ProfilingActive { get; set; }
        [ProtoMember(71)]
        public float ProfilingElapsed { get; set; }
        [ProtoMember(72)]
        public float ProfilingTotal { get; set; }
        [ProtoMember(73)]
        public int ProfilingMinDuration { get; set; }
    }
}

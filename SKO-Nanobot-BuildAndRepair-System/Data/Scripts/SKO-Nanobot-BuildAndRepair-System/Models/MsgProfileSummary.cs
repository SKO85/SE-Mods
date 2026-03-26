using ProtoBuf;
using System.Collections.Generic;

namespace SKONanobotBuildAndRepairSystem.Models
{
    [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
    public class MsgProfileSummary
    {
        [ProtoMember(1)]
        public bool IsRunning { get; set; }
        [ProtoMember(2)]
        public float ElapsedSeconds { get; set; }
        [ProtoMember(3)]
        public int MethodCount { get; set; }
        [ProtoMember(4)]
        public float SimSpeedMin { get; set; }
        [ProtoMember(5)]
        public float SimSpeedAvg { get; set; }
        [ProtoMember(6)]
        public string SessionName { get; set; }

        [ProtoMember(10)]
        public List<ProfileDomainEntry> Domains { get; set; }

        [ProtoMember(20)]
        public List<ProfileMethodEntry> TopMethods { get; set; }

        [ProtoMember(30)]
        public List<ProfileGridEntry> TopGrids { get; set; }
    }

    [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
    public class ProfileDomainEntry
    {
        [ProtoMember(1)]
        public string Name { get; set; }
        [ProtoMember(2)]
        public long Calls { get; set; }
        [ProtoMember(3)]
        public float TotalMs { get; set; }
        [ProtoMember(4)]
        public float AvgMs { get; set; }
        [ProtoMember(5)]
        public float MaxMs { get; set; }
    }

    [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
    public class ProfileMethodEntry
    {
        [ProtoMember(1)]
        public string Name { get; set; }
        [ProtoMember(2)]
        public long Calls { get; set; }
        [ProtoMember(3)]
        public float TotalMs { get; set; }
        [ProtoMember(4)]
        public float AvgMs { get; set; }
        [ProtoMember(5)]
        public float MinMs { get; set; }
        [ProtoMember(6)]
        public float MaxMs { get; set; }
    }

    [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
    public class ProfileGridEntry
    {
        [ProtoMember(1)]
        public string Name { get; set; }
        [ProtoMember(2)]
        public long Calls { get; set; }
        [ProtoMember(3)]
        public float TotalMs { get; set; }
        [ProtoMember(4)]
        public string OwnerName { get; set; }
    }
}

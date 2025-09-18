using ProtoBuf;

namespace SKONanobotBuildAndRepairSystem.Models
{
    [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
    public class MsgBlockState
    {
        [ProtoMember(1)]
        public long EntityId { get; set; }

        [ProtoMember(2)]
        public SyncBlockState State { get; set; }
    }
}
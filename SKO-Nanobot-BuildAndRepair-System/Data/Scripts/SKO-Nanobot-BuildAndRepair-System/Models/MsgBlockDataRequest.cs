using ProtoBuf;

namespace SKONanobotBuildAndRepairSystem.Models
{
    [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
    public class MsgBlockDataRequest
    {
        [ProtoMember(1)]
        public ulong SteamId { get; set; }

        [ProtoMember(2)]
        public long EntityId { get; set; }
    }
}
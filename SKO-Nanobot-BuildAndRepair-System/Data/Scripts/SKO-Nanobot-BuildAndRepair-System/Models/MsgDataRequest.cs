using ProtoBuf;

namespace SKONanobotBuildAndRepairSystem.Models
{
    [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
    public class MsgModDataRequest
    {
        [ProtoMember(1)]
        public ulong SteamId { get; set; }
    }
}
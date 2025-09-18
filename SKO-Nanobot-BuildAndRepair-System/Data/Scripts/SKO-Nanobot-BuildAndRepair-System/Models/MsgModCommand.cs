using ProtoBuf;

namespace SKONanobotBuildAndRepairSystem.Models
{
    [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
    public class MsgModCommand
    {
        [ProtoMember(1)]
        public ulong SteamId { get; set; }

        [ProtoMember(2)]
        public string Command { get; set; }
    }
}
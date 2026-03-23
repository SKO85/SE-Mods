using ProtoBuf;

namespace SKONanobotBuildAndRepairSystem.Models
{
    [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
    public class MsgModCommandResponse
    {
        [ProtoMember(1)]
        public string Message { get; set; }

        [ProtoMember(2)]
        public bool IsError { get; set; }

        [ProtoMember(3)]
        public bool UseMissionScreen { get; set; }

        [ProtoMember(4)]
        public string ScreenTitle { get; set; }

        [ProtoMember(5)]
        public string ScreenSubtitle { get; set; }
    }
}

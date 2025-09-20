using ProtoBuf;
using VRage.ObjectBuilders;

namespace SKONanobotBuildAndRepairSystem.Models
{
    [ProtoContract(UseProtoMembersOnly = true)]
    public class SyncComponents
    {
        [ProtoMember(1)]
        public SerializableDefinitionId Component { get; set; }

        [ProtoMember(2)]
        public int Amount { get; set; }
    }
}
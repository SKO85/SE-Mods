using System;
using VRage.Game.ModAPI;

namespace SKONanobotBuildAndRepairSystem.Models
{
    public class TargetBlockData : TargetEntityData
    {
        [Flags]
        public enum AttributeFlags
        {
            Projected = 0x0001,
            Autogrind = 0x0100
        }

        public IMySlimBlock Block { get; internal set; }
        public AttributeFlags Attributes { get; internal set; }

        public TargetBlockData(IMySlimBlock block, double distance, AttributeFlags attributes) : base(block != null ? block.FatBlock : null, distance)
        {
            Block = block;
            Attributes = attributes;
        }
    }
}
using VRage.ModAPI;

namespace SKONanobotBuildAndRepairSystem.Models
{
    public class TargetEntityData
    {
        public IMyEntity Entity { get; internal set; }
        public double Distance { get; internal set; }
        public bool Ignore { get; set; }

        public TargetEntityData(IMyEntity entity, double distance)
        {
            Entity = entity;
            Distance = distance;
            Ignore = false;
        }
    }
}
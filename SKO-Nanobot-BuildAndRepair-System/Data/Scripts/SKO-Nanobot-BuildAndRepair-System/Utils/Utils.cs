using System;

namespace SKONanobotBuildAndRepairSystem.Utils
{
    /// <summary>
    /// Small numeric utilities. Block- and inventory-specific helpers were moved
    /// into Extensions/SlimBlockExtensions.cs, Utils/UtilsInventory.cs, and
    /// Utils/UtilsPlayer.cs.
    /// </summary>
    public static class Utils
    {
        public static VRage.MyFixedPoint AsFloorMyFixedPoint(this double value)
        {
            return new VRage.MyFixedPoint() { RawValue = (long)(value * 1000000L) };
        }

        public static VRage.MyFixedPoint AsFloorMyFixedPoint(this float value)
        {
            return new VRage.MyFixedPoint() { RawValue = (long)(value * 1000000L) };
        }

        public static int CompareDistance(double a, double b)
        {
            var diff = a - b;
            return Math.Abs(diff) < 0.00001 ? 0 : (diff > 0 ? 1 : -1);
        }
    }
}

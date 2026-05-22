using System;

namespace SKONanobotBuildAndRepairSystem.Utils
{
    public static class UtilsMath
    {
        /// <summary>
        /// Three-way comparison for double distances with a small epsilon. Returns
        /// 0 when |a-b| &lt; 1e-5 (treated equal for sort stability), 1 when a > b, -1 otherwise.
        /// </summary>
        public static int CompareDistance(double a, double b)
        {
            var diff = a - b;
            return Math.Abs(diff) < 0.00001 ? 0 : (diff > 0 ? 1 : -1);
        }
    }
}

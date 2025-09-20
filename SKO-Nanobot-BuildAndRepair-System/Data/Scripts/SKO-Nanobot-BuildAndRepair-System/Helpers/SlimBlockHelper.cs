using VRage.Game.ModAPI;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Helpers
{
    public static class SlimBlockHelper
    {
        /// <summary>
        /// Checks if block is inside the given BoundingBox
        /// </summary>
        /// <param name="block"></param>
        /// <param name="areaBox"></param>
        /// <returns></returns>
        public static bool IsInRange(this IMySlimBlock block, ref MyOrientedBoundingBoxD areaBox, out double distance)
        {
            Vector3 halfExtents;
            block.ComputeScaledHalfExtents(out halfExtents);

            var matrix = block.CubeGrid.WorldMatrix;
            matrix.Translation = block.CubeGrid.GridIntegerToWorld(block.Position);

            var box = new MyOrientedBoundingBoxD(new BoundingBoxD(-(halfExtents), (halfExtents)), matrix);
            var inRange = areaBox.Intersects(ref box);

            distance = inRange ? (areaBox.Center - box.Center).Length() : 0;
            return inRange;
        }
    }
}
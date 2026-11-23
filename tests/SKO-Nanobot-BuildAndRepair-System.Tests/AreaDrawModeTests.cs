using System.Reflection;
using VRageMath;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-BUDGET-007 — BUG-261122.8: ShowArea box drawn solid from outside, edges only from inside (full-screen
    // overdraw per box), nothing beyond AreaDrawCullDistance from the box surface. Rotated / offset areas included.
    public class AreaDrawModeTests
    {
        private static readonly BoundingBoxD Box = new BoundingBoxD(new Vector3D(-50, -10, -10), new Vector3D(50, 10, 10));

        private static string Mode(Vector3D camera, MatrixD matrix, BoundingBoxD box)
        {
            var method = typeof(NanobotSystem).GetMethod("GetAreaDrawMode", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            return method.Invoke(null, new object[] { camera, matrix, box }).ToString();
        }

        private static string Mode(Vector3D camera)
        {
            return Mode(camera, MatrixD.Identity, Box);
        }

        [Fact]
        public void CameraInside_DrawsEdgesOnly()
        {
            Assert.Equal("Edges", Mode(Vector3D.Zero));
            Assert.Equal("Edges", Mode(new Vector3D(49, 9, -9)));
        }

        [Fact]
        public void CameraOutside_DrawsSolid()
        {
            Assert.Equal("Solid", Mode(new Vector3D(51, 0, 0)));
            Assert.Equal("Solid", Mode(new Vector3D(0, 0, 300)));
        }

        [Fact]
        public void CullDistance_IsMeasuredFromTheBoxSurface()
        {
            Assert.Equal("Solid", Mode(new Vector3D(50 + 999, 0, 0)));
            Assert.Equal("None", Mode(new Vector3D(50 + 1001, 0, 0)));
        }

        [Fact]
        public void RotatedAndOffsetArea_UsesTheBoxFrame()
        {
            // Area 100 m long along its local X; block turned 90 degrees so local X points along world Z, moved to (1000, 0, 0).
            var matrix = MatrixD.CreateWorld(new Vector3D(1000, 0, 0), Vector3D.Right, Vector3D.Up);
            Assert.Equal("Edges", Mode(new Vector3D(1000, 0, -40), matrix, Box));
            Assert.Equal("Edges", Mode(new Vector3D(1000, 0, 40), matrix, Box));
            Assert.Equal("Solid", Mode(new Vector3D(1040, 0, 0), matrix, Box)); // inside only if the rotation were ignored
        }
    }
}

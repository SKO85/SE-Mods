namespace SKONanobotBuildAndRepairSystem.Utils
{
    public static class UtilsSynchronization
    {
        public static uint RotateLeft(uint x, int n)
        {
            return (x << n) | (x >> (32 - n));
        }
    }
}
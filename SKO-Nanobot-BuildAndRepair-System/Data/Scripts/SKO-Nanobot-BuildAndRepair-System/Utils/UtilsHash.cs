namespace SKONanobotBuildAndRepairSystem.Utils
{
    /// <summary>
    /// Bit primitives used for hash mixing in collection list-hash computations.
    /// </summary>
    public static class UtilsHash
    {
        public static uint RotateLeft(uint x, int n)
        {
            return (x << n) | (x >> (32 - n));
        }
    }
}

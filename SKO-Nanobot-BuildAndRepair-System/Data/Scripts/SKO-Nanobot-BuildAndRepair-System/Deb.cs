using Sandbox.ModAPI;
using VRage.Utils;

namespace SKONanobotBuildAndRepairSystem
{
    internal class Deb
    {
        private static readonly bool EnableDebug = false;

        public static void Write(string msg)
        {
            if (EnableDebug)
            {
                MyAPIGateway.Utilities.ShowMessage("Nanobot Debug", msg);
                MyLog.Default.WriteLineAndConsole($"Nanobot Debug: {msg}");
            }
        }
    }
}

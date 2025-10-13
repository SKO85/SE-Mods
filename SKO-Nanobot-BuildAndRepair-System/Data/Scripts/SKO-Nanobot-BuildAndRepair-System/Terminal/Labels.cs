using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;

namespace SKONanobotBuildAndRepairSystem.Terminal
{
    public static class Labels
    {
        public static IMyTerminalControlLabel Create(string id, MyStringId label)
        {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlLabel, IMyShipWelder>(id);
            control.Label = label;

            NanobotTerminal.CustomControls.Add(control);
            return control;
        }
    }
}
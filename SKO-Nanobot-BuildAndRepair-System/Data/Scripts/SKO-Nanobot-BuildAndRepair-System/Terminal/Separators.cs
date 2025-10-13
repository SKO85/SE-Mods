using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using System;

namespace SKONanobotBuildAndRepairSystem.Terminal
{
    public static class Separators
    {
        public static IMyTerminalControlSeparator Create(string id, Func<IMyTerminalBlock, bool> isVisible)
        {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyShipWelder>(id);
            control.Visible = isVisible;

            NanobotTerminal.CustomControls.Add(control);
            return control;
        }
    }
}
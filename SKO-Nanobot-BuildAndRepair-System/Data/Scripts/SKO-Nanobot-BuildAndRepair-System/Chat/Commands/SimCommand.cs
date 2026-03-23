using Sandbox.ModAPI;

namespace SKONanobotBuildAndRepairSystem.Chat.Commands
{
    public static class SimCommand
    {
        public static ChatCommandResult Execute(string[] args)
        {
            // args[0] = "sim"
            if (args.Length < 2)
            {
                if (Mod.SimSpeedOverride.HasValue)
                    return ChatCommandResult.Success(string.Format("Sim-speed override: {0:F2} (real: {1:F2})",
                        Mod.SimSpeedOverride.Value,
                        MyAPIGateway.Physics != null ? MyAPIGateway.Physics.ServerSimulationRatio : 1.0f));
                else
                    return ChatCommandResult.Success(string.Format("Sim-speed override: off (real: {0:F2})",
                        MyAPIGateway.Physics != null ? MyAPIGateway.Physics.ServerSimulationRatio : 1.0f));
            }

            if (args[1] == "reset")
            {
                Mod.SimSpeedOverride = null;
                return ChatCommandResult.Success("Sim-speed override removed. Using server sim-speed.");
            }

            float simValue;
            if (!float.TryParse(args[1], out simValue))
                return ChatCommandResult.Error("Invalid value. Usage: /nanobars sim <0.1-1.0|reset>");

            if (simValue < 0.1f || simValue > 1.0f)
                return ChatCommandResult.Error("Value must be between 0.1 and 1.0");

            Mod.SimSpeedOverride = simValue;
            return ChatCommandResult.Success(string.Format("Sim-speed override set to {0:F2}", simValue));
        }
    }
}

using Sandbox.Game.EntityComponents;

namespace SKONanobotBuildAndRepairSystem.Helpers
{
    public static class PowerHelper
    {
        public static float GetAvailablePower(this NanobotSystem system)
        {
            var distributor = system.Welder.CubeGrid.ResourceDistributor as MyResourceDistributorComponent;
            if (distributor != null)
            {
                return distributor.MaxAvailableResourceByType(MyResourceDistributorComponent.ElectricityId);
            }

            return 0f;
        }

        public static bool HasRequiredElectricPower(this NanobotSystem system)
        {
            if (system.Welder == null) return false;

            var required = ComputeRequiredElectricPower(system);
            var maxAvailable = GetAvailablePower(system); ;

            if (maxAvailable >= required)
            {
                return true;
            }

            return false;
        }

        public static float ComputeRequiredElectricPower(this NanobotSystem system)
        {
            if (system.Welder == null)
                return 0f;

            if (!system.Welder.Enabled || !system.Welder.IsWorking)
                return 0f;

            // Standby Power.
            float required = system.Settings.MaximumRequiredElectricPowerStandby;

            // Welding Power.
            required += system.State.Welding ? system.Settings.MaximumRequiredElectricPowerWelding - system.Settings.MaximumRequiredElectricPowerStandby : 0f;

            // Grinding Power.
            required += system.State.Grinding ? system.Settings.MaximumRequiredElectricPowerGrinding - system.Settings.MaximumRequiredElectricPowerStandby : 0f;

            // Transport Power.
            required += system.State.Transporting
                ? (system.Settings.SearchMode == SearchModes.Grids
                    ? (system.Settings.MaximumRequiredElectricPowerTransport - system.Settings.MaximumRequiredElectricPowerStandby) / 10
                    : (system.Settings.MaximumRequiredElectricPowerTransport - system.Settings.MaximumRequiredElectricPowerStandby))
                : 0f;

            return required;
        }
    }
}
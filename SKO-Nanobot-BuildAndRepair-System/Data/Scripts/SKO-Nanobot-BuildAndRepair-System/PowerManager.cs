using Sandbox.Game.EntityComponents;

namespace SKONanobotBuildAndRepairSystem
{
    public static class PowerManager
    {
        public static float GetAvailablePower(NanobotBuildAndRepairSystemBlock block)
        {
            var distributor = block.Welder.CubeGrid.ResourceDistributor as MyResourceDistributorComponent;
            if (distributor != null)
            {
                return distributor.MaxAvailableResourceByType(MyResourceDistributorComponent.ElectricityId);
            }

            return 0f;
        }

        public static bool HasRequiredElectricPower(NanobotBuildAndRepairSystemBlock block)
        {
            if (block.Welder == null) return false;
            if (block._CreativeModeActive) return true;

            var required = ComputeRequiredElectricPower(block);
            var maxAvailable = GetAvailablePower(block); ;

            if (maxAvailable >= required)
            {
                return true;
            }

            return false;
        }

        public static float ComputeRequiredElectricPower(NanobotBuildAndRepairSystemBlock block)
        {
            if (block.Welder == null)
                return 0f;

            // Standby Power.
            float required = block.Settings.MaximumRequiredElectricPowerStandby;

            // Welding Power.
            required += block.State.Welding ? block.Settings.MaximumRequiredElectricPowerWelding - block.Settings.MaximumRequiredElectricPowerStandby : 0f;

            // Grinding Power.
            required += block.State.Grinding ? block.Settings.MaximumRequiredElectricPowerGrinding - block.Settings.MaximumRequiredElectricPowerStandby : 0f;

            // Transport Power.
            required += block.State.Transporting
                ? (block.Settings.SearchMode == SearchModes.Grids
                    ? (block.Settings.MaximumRequiredElectricPowerTransport - block.Settings.MaximumRequiredElectricPowerStandby) / 10
                    : (block.Settings.MaximumRequiredElectricPowerTransport - block.Settings.MaximumRequiredElectricPowerStandby))
                : 0f;

            return required;
        }
    }
}
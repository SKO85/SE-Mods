namespace SKONanobotBuildAndRepairSystem
{
    public static class TerminalControlManager
    {
        private static bool _initialized = false;

        public static void InitControls()
        {
            if (_initialized || !NanobotBuildAndRepairSystemMod.SettingsValid)
                return;

            if (!NanobotBuildAndRepairSystemTerminal.CustomControlsInit && NanobotBuildAndRepairSystemMod.BuildAndRepairSystems.Count > 0)
            {
                NanobotBuildAndRepairSystemTerminal.InitializeControls();
                _initialized = true;

                Logging.Instance?.Write(Logging.Level.Info, "Terminal controls initialized.");
            }
        }
    }
}

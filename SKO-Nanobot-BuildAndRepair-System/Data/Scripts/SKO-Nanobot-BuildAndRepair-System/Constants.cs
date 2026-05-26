namespace SKONanobotBuildAndRepairSystem
{
    public static class Constants
    {
        public const string ModVersion = "2.5.5";

        // BuildId — bumped before every build. Format: "YYMMDD.N" where YYMMDD is
        // today's date and N is an auto-incrementing sequence for that day, starting
        // at 1. If today's date matches the current value, increment N; otherwise
        // reset to today's date with N=1. Surfaced in the debug HUD, /nanobars
        // version, /nanobars -help, and the profiler summary header so we can
        // confirm which build produced a given diagnostic.
        public const string BuildId = "260526.3";
    }
}

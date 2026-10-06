using System;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>Ordered scenario list (no reflection — SE sandbox). Add new scenarios here.</summary>
    internal static class ScenarioRegistry
    {
        public static readonly ITestScenario[] All =
        {
            new S01_RepairStocked(),
            new S02_RepairEmptyCargo(),
            new S04_JanitorGrind(),
            new S05_NoDoubleAssign(),
            new S06_MaxSystemsPerGrid(),
            new S07_ClusterOneScanner()
        };

        public static ITestScenario Find(string id)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (string.Equals(All[i].Id, id, StringComparison.OrdinalIgnoreCase)) return All[i];
            }
            return null;
        }
    }
}

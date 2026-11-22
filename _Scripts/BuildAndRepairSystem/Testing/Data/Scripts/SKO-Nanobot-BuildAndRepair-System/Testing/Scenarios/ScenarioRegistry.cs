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
            new S03_ProjectedBuild("S03", ProjectionMode.Build),
            new S04_JanitorGrind(),
            new S05_NoDoubleAssign(),
            new S06_MaxSystemsPerGrid(),
            new S07_ClusterOneScanner(),
            new S08_LargeGridPerf(),
            new S10_CollectFloating(),
            new S11_GrindOrder("S11", GrindOrderMode.FarthestFirst),
            new S11_GrindOrder("S12", GrindOrderMode.NearestFirst),
            new S11_GrindOrder("S13", GrindOrderMode.SmallestGridFirst),
            new S14_WeldPriority("S14", false),
            new S14_WeldPriority("S15", true),
            new S16_GrindClassDisabled(),
            new S17_OwnershipMatrix(),
            new S18_MechanicalSubgrid("S18", "Rotor", RigBlockKind.Stator, "AddRotorTopPart"),
            new S19_Power("S19", false),
            new S19_Power("S20", true),
            new S21_WeldOptions("S21", AutoWeldOptions.WeldFunctional),
            new S21_WeldOptions("S22", AutoWeldOptions.WeldSkeleton),
            new S23_Colors("S23", false),
            new S23_Colors("S24", true),
            new S25_RangeLimit(),
            new S26_WeldOnlyNoGrind(),
            new S03_ProjectedBuild("S27", ProjectionMode.AllowBuildOff),
            new S28_GridStateShowArea("S28", RigGridState.Station, false),
            new S28_GridStateShowArea("S29", RigGridState.Station, true),
            new S28_GridStateShowArea("S30", RigGridState.ShipStill, false),
            new S28_GridStateShowArea("S31", RigGridState.ShipStill, true),
            new S28_GridStateShowArea("S32", RigGridState.ShipMoving, false),
            new S28_GridStateShowArea("S33", RigGridState.ShipMoving, true),
            new S18_MechanicalSubgrid("S34", "Advanced rotor", RigBlockKind.AdvancedStator, "AddRotorTopPart"),
            new S18_MechanicalSubgrid("S35", "Hinge", RigBlockKind.Hinge, "AddHingeTopPart"),
            new S18_MechanicalSubgrid("S36", "Piston", RigBlockKind.Piston, "AddPistonHead"),
            new S18_MechanicalSubgrid("S37", "Wheel", RigBlockKind.Suspension, "AddWheel"),
            new S38_ConnectorDocked(),
            new S39_SafeZone("S39", SafeZoneMode.WeldForbidden),
            new S39_SafeZone("S40", SafeZoneMode.AllAllowed),
            new S39_SafeZone("S41", SafeZoneMode.GrindForbiddenAtTarget),
            new S03_ProjectedBuild("S42", ProjectionMode.ZoneForbids),
            new S43_VeryLargeShip(),
            new S44_ShipsInFormation("S44", false),
            new S44_ShipsInFormation("S45", true),
            new S03_ProjectedBuild("S46", ProjectionMode.AdminForbids),
            new S47_ConfigRestriction("S47", RestrictionMode.JanitorRelation),
            new S47_ConfigRestriction("S48", RestrictionMode.WorkMode),
            new S47_ConfigRestriction("S49", RestrictionMode.SearchMode),
            new S50_ConfigSwitches("S50", ConfigSwitch.LimitDisabled),
            new S50_ConfigSwitches("S51", ConfigSwitch.AssignmentDisabled),
            new S50_ConfigSwitches("S52", ConfigSwitch.WeldBudgetOne),
            new S39_SafeZone("S53", SafeZoneMode.CheckDisabled)
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

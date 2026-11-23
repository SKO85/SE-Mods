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
            new S09_PbApi(),
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
            new S18_MechanicalSubgrid("S36", "Piston", RigBlockKind.Piston, "Add Top Part"),
            new S18_MechanicalSubgrid("S37", "Wheel", RigBlockKind.Suspension, "Add Top Part"),
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
            new S39_SafeZone("S53", SafeZoneMode.CheckDisabled),
            new S54_LimitLowered(),
            new S55_Claims("S55", false),
            new S55_Claims("S56", true),
            new S57_Redamage(),
            new S58_Shields("S58", ShieldMode.EnemyShieldUp),
            new S58_Shields("S59", ShieldMode.EnemyShieldOff),
            new S58_Shields("S60", ShieldMode.CheckDisabled),
            new S58_Shields("S61", ShieldMode.CheckOnMidGrind),
            new S58_Shields("S62", ShieldMode.OwnShieldWeld),
            new S58_Shields("S63", ShieldMode.FriendlyShieldWeld),
            new S64_Load("S64", "large clusters", LoadWork.Weld, 2, 10, 42),
            new S64_Load("S65", "small clusters", LoadWork.Weld, 10, 2, 43),
            new S64_Load("S66", "large clusters", LoadWork.Grind, 2, 10, 27),
            new S64_Load("S67", "small clusters", LoadWork.Grind, 10, 2, 26),
            new S64_Load("S68", "large clusters", LoadWork.Mixed, 2, 10),
            new S64_Load("S69", "small clusters", LoadWork.Mixed, 10, 2),
            new S64_Load("S70", "stress", LoadWork.Mixed, 4, 10),
            new S71_CompanionScript(),
            new S72_PlacedFrame(),
            new S73_ProtectedGrid("S73", ProtectionMode.Indestructible),
            new S73_ProtectedGrid("S74", ProtectionMode.DamageHandler),
            new S75_LeavingTarget("S75", false),
            new S75_LeavingTarget("S76", true),
            new S77_WorkModeOrder("S77", WorkModes.WeldBeforeGrind),
            new S77_WorkModeOrder("S78", WorkModes.GrindBeforeWeld),
            new S79_SpeedRatio("S79", false),
            new S79_SpeedRatio("S80", true),
            new S81_CompanionGroups("S81", CompanionGroupsMode.TwoGroups),
            new S81_CompanionGroups("S82", CompanionGroupsMode.TwoAssemblers),
            new S81_CompanionGroups("S83", CompanionGroupsMode.StockInCargo),
            new S84_SorterBlockedStock(),
            new S03_ProjectedBuild("S85", ProjectionMode.ZoneForbidsWelding),
            new S86_StarvedNeighbour(),
            new S87_Sources("S87", SourceKind.DockedShipCargo),
            new S87_Sources("S88", SourceKind.AssemblerOutput),
            new S89_PushTargetsFull(),
            new S90_PushImmediately("S90", PushFlag.None),
            new S90_PushImmediately("S91", PushFlag.IngotOre),
            new S90_PushImmediately("S92", PushFlag.Components),
            new S90_PushImmediately("S93", PushFlag.Items),
            new S94_ClaimRobustness("S94", ClaimCase.WeldFocus),
            new S94_ClaimRobustness("S95", ClaimCase.GrindFocus),
            new S94_ClaimRobustness("S96", ClaimCase.WelderDestroyed),
            new S94_ClaimRobustness("S97", ClaimCase.WelderUnpowered),
            new S94_ClaimRobustness("S98", ClaimCase.GrinderStopped),
            new S99_ScanCost("S99", 1000),
            new S99_ScanCost("S100", 2000),
            new S99_ScanCost("S101", 5000),
            new S99_ScanCost("S102", 10000),
            new S103_ComponentLogistics("S103", 10),
            new S103_ComponentLogistics("S104", 20),
            new S103_ComponentLogistics("S105", 50),
            new S103_ComponentLogistics("S106", 100),
            new S103_ComponentLogistics("S107", 10, true),
            new S103_ComponentLogistics("S108", 20, true),
            new S103_ComponentLogistics("S109", 50, true),
            new S103_ComponentLogistics("S110", 100, true)
        };

        /// <summary>
        /// Named sets for `test run @name` (a scenario can be in several). S00 (warm-up) runs first in every session and
        /// is in none. Every other scenario must be in at least one group (ScenarioGroupsTests); keep harness.md in sync.
        /// </summary>
        public static readonly ScenarioGroup[] Groups =
        {
            new ScenarioGroup("core", "Repair, damage detection, range, work modes, power, speeds", "S01,S19,S20,S25,S57,S72,S75,S76,S77,S78,S79,S80"),
            new ScenarioGroup("weld", "Welding: repair, weld options, colours, docked ships, sources", "S01,S02,S03,S18,S21,S22,S23,S26,S27,S34,S35,S36,S37,S38,S42,S44,S46,S72,S84,S86,S87,S88"),
            new ScenarioGroup("projection", "Projector building, incl. admin and safe-zone rules", "S03,S27,S42,S46,S85"),
            new ScenarioGroup("subgrids", "Rotors, pistons, hinges, wheels, connectors, ships in flight", "S18,S34,S35,S36,S37,S38,S44,S45"),
            new ScenarioGroup("grind", "Grinding: janitor, order, colours, protection, leaving targets, full storage", "S04,S11,S12,S13,S16,S17,S24,S45,S73,S74,S76,S89"),
            new ScenarioGroup("zone", "Safe zones", "S39,S40,S41,S42,S53,S85"),
            new ScenarioGroup("shields", "DefenseShields (skipped without the mod)", "S58,S59,S60,S61,S62,S63"),
            new ScenarioGroup("inventory", "Component sources, sorter, empty cargo, push targets, push options, logistics at scale", "S02,S84,S87,S88,S89,S90,S91,S92,S93,S103,S104,S105,S106,S107,S108,S109,S110"),
            new ScenarioGroup("claims", "Block claims, per-grid limit, BaRs sharing work, takeovers", "S05,S06,S50,S51,S54,S55,S56,S86,S94,S95,S96,S97,S98"),
            new ScenarioGroup("cluster", "Cluster coordination and the per-grid limit", "S06,S07,S50,S54"),
            new ScenarioGroup("perf", "Load, large grids, scan cost by grid size, ShowArea and budget cost (long)", "S08,S28,S29,S30,S31,S32,S33,S43,S52,S64,S65,S66,S67,S68,S69,S70,S99,S100,S101,S102,S103,S104,S105,S106,S107,S108,S109,S110"),
            new ScenarioGroup("companion", "PB API and the companion script", "S09,S71,S81,S82,S83"),
            new ScenarioGroup("config", "Admin settings (Fixed / Default, limits, checks)", "S46,S47,S48,S49,S50,S51,S52,S53"),
            new ScenarioGroup("priority", "Weld / grind priority lists", "S14,S15,S16"),
            new ScenarioGroup("collect", "Collecting floating objects", "S10")
        };

        public static ITestScenario Find(string id)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (string.Equals(All[i].Id, id, StringComparison.OrdinalIgnoreCase)) return All[i];
            }
            return null;
        }

        /// <summary>"weld" or "@weld"; null when unknown.</summary>
        public static ScenarioGroup FindGroup(string name)
        {
            if (name == null) return null;
            if (name.StartsWith("@")) name = name.Substring(1);
            for (int i = 0; i < Groups.Length; i++)
            {
                if (string.Equals(Groups[i].Name, name, StringComparison.OrdinalIgnoreCase)) return Groups[i];
            }
            return null;
        }

        /// <summary>Comma-separated group names of a scenario ("" when none).</summary>
        public static string GroupsOf(string id)
        {
            var result = "";
            for (int i = 0; i < Groups.Length; i++)
            {
                if (Array.IndexOf(Groups[i].Ids, id) >= 0) result += (result.Length > 0 ? ", " : "") + Groups[i].Name;
            }
            return result;
        }
    }

    internal sealed class ScenarioGroup
    {
        public readonly string Name;
        public readonly string Description;
        public readonly string[] Ids;

        public ScenarioGroup(string name, string description, string ids)
        {
            Name = name;
            Description = description;
            Ids = ids.Split(',');
        }
    }
}

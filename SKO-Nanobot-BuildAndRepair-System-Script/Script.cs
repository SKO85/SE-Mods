// Version: v1.11 - 05.03.2026 12:00:00

static double UpdateIntervalAssemblerQueues = 0.1; //Update every x seconds (0=as fast as possible, 0.5=every 500ms, ..) 
static double UpdateIntervalGrinding = 0.1; //Update every x seconds (0=as fast as possible, 0.5=every 500ms, ..) 

/// <summary> 
/// Configure the groups with their block names and/or group names.
/// You can access screens from TextSurfaceProvider like Cockpits with their name followed by [screenindex] e.g. "Cockpit[0]" 
/// </summary> 
static BuildAndRepairSystemQueuingGroup[] BuildAndRepairSystemQueuingGroups = {
   new BuildAndRepairSystemQueuingGroup() {
      BuildAndRepairSystemGroupName = "BuildAndRepairGroup1",
      AssemblerGroupName = "AssemblerGroup1",
      Displays = new [] {
         new DisplayDefinition {
            DisplayNames = new [] { "BuildAndRepairGroup1StatusPanel", "Cockpit[0]" },
            DisplayKinds = new [] { DisplayKind.ShortStatus, DisplayKind.Status, DisplayKind.WeldTargets, DisplayKind.GrindTargets, DisplayKind.CollectTargets, DisplayKind.MissingItems, DisplayKind.BlockWeldPriority, DisplayKind.BlockGrindPriority },
            DisplayMaxLines = 19,
            DisplaySwitchTime = 4
         }
      }
   }
};

/* Complex Example with multiple groups, displays, .. 
static BuildAndRepairSystemQueuingGroup[] BuildAndRepairSystemQueuingGroups = { 
   new BuildAndRepairSystemQueuingGroup() { 
      Name = "Hangar BaR Group1", 
      BuildAndRepairSystemNames = new [] { "Hangar1BaRSystem1", "Hangar1BaRSystem2" }, 
      AssemblerNames = new[] { "Hangar1Assembler1", "Hangar1Assembler2", "Hangar1Assembler3" }, 
      Displays = new[] { 
         new DisplayDefinition { 
            DisplayNames = new [] { "BaRStatusPanel" }, 
            DisplayKinds = new [] { DisplayKind.Status, DisplayKind.MissingItems, DisplayKind.WeldTargets, DisplayKind.GrindTargets, DisplayKind.CollectTargets }, 
            DisplayMaxLines = 19, 
            DisplaySwitchTime = 4 
         } 
      } 
   }, 
   new BuildAndRepairSystemQueuingGroup() { 
      Name = "Hangar BaR Group2", 
      BuildAndRepairSystemNames = new[] { "Hangar1BaRSystem1", "Hangar1BaRSystem2" }, 
      AssemblerNames = new[] { "Hangar1Assembler1", "Hangar1Assembler2", "Hangar1Assembler3" }, 
      Displays = new [] { 
         new DisplayDefinition { 
            DisplayNames = new [] { "Hangar1BaRSystemStatusPanel1", "Hangar1BaRSystemStatusPanel2" }, 
            DisplayKinds = new [] { DisplayKind.Status }, 
            DisplayMaxLines = 19, 
            DisplaySwitchTime = 0 
         }, 
         new DisplayDefinition { 
            DisplayNames = new [] { "Hangar1BaRSystemStatusPanel3", "Hangar1BaRSystemStatusPanel4" }, 
            DisplayKinds = new [] { DisplayKind.Status, DisplayKind.MissingItems, DisplayKind.WeldTargets, DisplayKind.GrindTargets, DisplayKind.CollectTargets }, 
            DisplayMaxLines = 10, 
            DisplaySwitchTime = 4 
         } 
      } 
   }, 
   new BuildAndRepairSystemQueuingGroup() { 
      Name = "Hangar BaR Group3", 
      BuildAndRepairSystemNames = new[] { "BuildAndRepair1.1", "BuildAndRepair1.2" }, 
      AssemblerNames = new[] { "Assembler1.1", "Assembler1.2", "Assembler1.3" } 
   }, 
   new BuildAndRepairSystemQueuingGroup() { 
      Name = "Hangar BaR Group4", 
      BuildAndRepairSystemGroupName = "BuildAndRepairGroup1", 
      AssemblerNames = new[] { "Assembler1.1", "Assembler1.2", "Assembler1.3" } 
   }, 
   new BuildAndRepairSystemQueuingGroup() { 
      Name = "Hangar BaR Group5", 
      BuildAndRepairSystemGroupName = "BuildAndRepairGroup1", 
      AssemblerNames = new[] { "Assembler1.1", "Assembler1.2", "Assembler1.3" } 
   }, 
   new BuildAndRepairSystemQueuingGroup() { 
      Name = "Hangar BaR Group6", 
      BuildAndRepairSystemGroupName = "BuildAndRepairGroup1", 
      AssemblerGroupName = "AssemblerGroup1" 
   } 
}; 
*/

// No user changeable settings behind this point 

/// <summary> 
/// Kind of Display 
/// </summary> 
public enum DisplayKind
{
    ShortStatus,
    Status,
    WeldTargets,
    GrindTargets,
    CollectTargets,
    MissingItems,
    BlockWeldPriority,
    BlockGrindPriority
}

static BuildAndRepairAutoQueuing _AutoQueuing;

public Program()
{
    Runtime.UpdateFrequency = UpdateFrequency.Update100;
    _AutoQueuing = new BuildAndRepairAutoQueuing(this);
}

void Main(string arg)
{
    var infoOnly = arg != null && arg.Trim().Equals("info-only", StringComparison.OrdinalIgnoreCase);
    _AutoQueuing.SetInfoOnly(infoOnly);
    _AutoQueuing.Handle();
}

/// <summary> 
/// Group configuration. 
/// Grouped Systems/Assembler could be defined either by list 
/// their Names (BuildAndRepairSystemNames\AssemblerNames) and or by giving 
/// a group name (BuildAndRepairSystemGroupName\AssemblerGroupName) 
/// </summary> 
public class BuildAndRepairSystemQueuingGroup
{
    public string Name { get; set; }
    public string[] BuildAndRepairSystemNames { get; set; }
    public string BuildAndRepairSystemGroupName { get; set; }

    public string[] AssemblerNames { get; set; }
    public string AssemblerGroupName { get; set; }

    public DisplayDefinition[] Displays { get; set; }
}

/// <summary>
/// Definition for multiple displays
/// </summary> 
public class DisplayDefinition
{
    /// <summary> 
    /// List of Displaynames 
    /// </summary> 
    public string[] DisplayNames { get; set; }

    /// <summary> 
    /// You can choose the display pages you need from enum DisplayKind. They will be switched every DisplaySwitchTime seconds 
    /// </summary> 
    public DisplayKind[] DisplayKinds { get; set; } = new[] { DisplayKind.Status };

    /// <summary> 
    /// The maximum of lines that should be displayed in case of list items (Blocks to build, grind, missing, ..) 
    /// </summary> 
    public int DisplayMaxLines { get; set; } = 19;

    /// <summary> 
    /// Autoswitch time [s] 
    /// </summary> 
    public double DisplaySwitchTime { get; set; } = 5;
}

/// <summary> 
/// Build and repair system automatic queuing of missing components 
/// </summary> 
public class BuildAndRepairAutoQueuing
{
    private Program _Program;
    private bool _IsInit;
    private double _ElapsedTime;
    private double _ReInit;
    private double _NextUpdateAssemblerQueues;
    private double _NextUpdateGrinding;
    private BuildAndRepairSystemQueuingGroupData[] _GroupData;

    public string InitializationResultMessage { get; private set; }

    private bool _InfoOnly = false;
    public BuildAndRepairAutoQueuing(Program program)
    {
        _Program = program;
    }

    public void SetInfoOnly(bool infoOnly)
    {
        _InfoOnly = infoOnly;
    }

    /// <summary> 
    /// Autorepair 
    /// </summary> 
    public void Handle()
    {
        _ElapsedTime += _Program.Runtime.TimeSinceLastRun.TotalSeconds;
        if (!_IsInit)
        {
            Initialize();
            _ReInit = _ElapsedTime + 120; //Refresh every 2 Minutes 
            _NextUpdateAssemblerQueues = _ElapsedTime - 1; //Next refesh now 
            _NextUpdateGrinding = _NextUpdateAssemblerQueues;
            if (!string.IsNullOrWhiteSpace(InitializationResultMessage))
            {
                _Program.Echo(InitializationResultMessage);
            }
        }
        if (_IsInit)
        {
            if (_ElapsedTime > _NextUpdateGrinding)
            {
                ScriptControlledGrinding();
                _NextUpdateGrinding = _ElapsedTime + UpdateIntervalGrinding;
            }
            if (!_InfoOnly && _ElapsedTime > _NextUpdateAssemblerQueues)
            {
                CheckAssemblerQueues();
                _NextUpdateAssemblerQueues = _ElapsedTime + UpdateIntervalAssemblerQueues;
            }
            RefreshDisplays();

            if (_ElapsedTime > _ReInit)
            {
                _IsInit = false; //Refresh 
            }
        }
    }

    /// <summary> 
    /// Initialize lists with blocks to manage 
    /// </summary> 
    private void Initialize()
    {
        _IsInit = false;
        InitializationResultMessage = string.Empty;

        if (BuildAndRepairSystemQueuingGroups == null)
        {
            _GroupData = new BuildAndRepairSystemQueuingGroupData[0];
            _IsInit = true;
            return;
        }

        _GroupData = new BuildAndRepairSystemQueuingGroupData[BuildAndRepairSystemQueuingGroups.Length];

        var idx = 0;
        foreach (var queuingGroup in BuildAndRepairSystemQueuingGroups)
        {
            var displays = (queuingGroup != null && queuingGroup.Displays != null) ? queuingGroup.Displays : new DisplayDefinition[0];
            _GroupData[idx] = new BuildAndRepairSystemQueuingGroupData(displays.Length);
            _GroupData[idx].Settings = queuingGroup;
            _GroupData[idx].RepairSystems = InitHandler<RepairSystemHandler>(queuingGroup);
            if (_GroupData[idx].RepairSystems != null)
            {
                _GroupData[idx].RepairSystems.SetProgram(_Program);
            }
            _GroupData[idx].Assemblers = InitAssemblerList(queuingGroup);
            _GroupData[idx].StatusDisplays = new List<StatusAndLogDisplay>();
            for (int d = 0; d < displays.Length; d++)
            {
                var displayDef = displays[d];
                var statusDisplay = new StatusAndLogDisplay(_Program, string.IsNullOrEmpty(queuingGroup != null ? queuingGroup.Name : null) ? "BaR Group " + idx : queuingGroup.Name, displayDef != null ? displayDef.DisplayNames : null, null);
                _GroupData[idx].StatusDisplays.Add(statusDisplay);
                statusDisplay.Clear();
                statusDisplay.UpdateDisplay();
            }
            idx++;
        }

        _IsInit = true;
    }

    /// <summary> 
    /// Init the group/list of items handler 
    /// </summary> 
    private T InitHandler<T>(BuildAndRepairSystemQueuingGroup queuingGroup) where T : EntityHandler, new()
    {
        T handler = null;
        if (queuingGroup != null && !string.IsNullOrWhiteSpace(queuingGroup.BuildAndRepairSystemGroupName))
        {
            var group = _Program.GridTerminalSystem.GetBlockGroupWithName(queuingGroup.BuildAndRepairSystemGroupName);
            if (group != null)
            {
                handler = new T();
                handler.Init(group);
            }
        }

        if (queuingGroup != null && queuingGroup.BuildAndRepairSystemNames != null)
        {
            foreach (var name in queuingGroup.BuildAndRepairSystemNames)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var entity = _Program.GridTerminalSystem.GetBlockWithName(name);
                    if (entity != null)
                    {
                        if (handler == null) handler = new T();
                        handler.Init(entity);
                    }
                }
            }
        }

        // Fallback: auto-detect Build&Repair blocks if none found by explicit group/names
        if (handler == null || handler.Count == 0)
        {
            try
            {
                // Only supported for RepairSystemHandler (the only T used by this script)
                if (typeof(T) == typeof(RepairSystemHandler) && _Program != null && _Program.GridTerminalSystem != null)
                {
                    var auto = new RepairSystemHandler();
                    auto.Init(_Program.GridTerminalSystem, (IMyShipWelder blk) =>
                    {
                        try
                        {
                            // Test for known BuildAndRepair properties; an exception likely means not our block
                            var _ = blk.GetValueBool("BuildAndRepair.ScriptControlled");
                            return true;
                        }
                        catch { }
                        try
                        {
                            var _ = blk.GetValue<long>("BuildAndRepair.Mode");
                            return true;
                        }
                        catch { }
                        return false;
                    });

                    if (auto.Count > 0)
                    {
                        handler = auto as T;
                        InitializationResultMessage += string.Format("\nInfo: Auto-detected {0} Build&Repair systems.", auto.Count);
                    }
                }
            }
            catch { }
        }

        if (handler == null || handler.Count == 0)
        {
            InitializationResultMessage += "\nWarning: Group Repairsystems group empty/wrong types!";
            handler = null;
        }

        return handler;
    }

    /// <summary> 
    /// Build list of assemblers 
    /// </summary> 
    /// <returns></returns> 
    private List<long> InitAssemblerList(BuildAndRepairSystemQueuingGroup queuingGroup)
    {
        List<long> assemblers = null;

        if (queuingGroup != null && !string.IsNullOrWhiteSpace(queuingGroup.AssemblerGroupName))
        {
            var group = _Program.GridTerminalSystem.GetBlockGroupWithName(queuingGroup.AssemblerGroupName);
            if (group != null)
            {
                assemblers = new List<long>();
                var entities = new List<IMyAssembler>();
                group.GetBlocksOfType(entities);
                foreach (var entity in entities)
                {
                    if (entity != null) assemblers.Add(entity.EntityId);
                }
            }
        }

        if (queuingGroup != null && queuingGroup.AssemblerNames != null)
        {
            foreach (var name in queuingGroup.AssemblerNames)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var entity = _Program.GridTerminalSystem.GetBlockWithName(name);
                    if (entity != null && entity is IMyAssembler)
                    {
                        if (assemblers == null) assemblers = new List<long>();
                        assemblers.Add(entity.EntityId);
                    }
                }
            }
        }

        if (assemblers == null || assemblers.Count == 0)
        {
            InitializationResultMessage += "\nWarning: Group Assemblers group empty/wrong types!";
            assemblers = null;
        }

        return assemblers;
    }

    private void RefreshDisplays()
    {
        if (_GroupData == null || _GroupData.Length == 0) return;
        foreach (var groupData in _GroupData)
        {
            if (groupData != null)
            {
                groupData.RefreshDisplay(_ElapsedTime);
            }
        }
    }

    /// <summary> 
    /// This the basic algorithm and spread the items over the list of assemblers. 
    /// </summary> 
    private void CheckAssemblerQueues()
    {
        if (_GroupData == null || _GroupData.Length == 0) return;
        foreach (var groupData in _GroupData)
        {
            if (groupData != null)
            {
                groupData.CheckAssemblerQueues();
            }
        }
    }

    /// <summary> 
    /// Place your code here to handle specialized Grind handling 
    /// </summary> 
    private void ScriptControlledGrinding()
    {
        //Simple example of Script controlled grind handling 
        //foreach (var groupData in _GroupData) 
        //{ 
        //   groupData.RepairSystems.ScriptControlled = true; 
        //   var listGrindable = groupData.RepairSystems.PossibleGrindTargets(); 
        //   //If nothing to grind or current grinding object no longer in list (allready grinded) 
        //   if (groupData.RepairSystems.CurrentPickedGrindTarget == null || listGrindable.IndexOf(groupData.RepairSystems.CurrentPickedGrindTarget) < 0) 
        //   { 
        //      foreach (var entry in listGrindable) 
        //      { 
        //         var antenna = entry.FatBlock as IMyRadioAntenna; 
        //         if (antenna != null) 
        //         { 
        //            groupData.RepairSystems.CurrentPickedGrindTarget = entry; 
        //            break; 
        //         } 
        //         var reactor = entry.FatBlock as IMyReactor; 
        //         if (reactor != null) 
        //         { 
        //            groupData.RepairSystems.CurrentPickedGrindTarget = entry; 
        //            break; 
        //         } 
        //         var guns = entry.FatBlock as IMyUserControllableGun; 
        //         if (guns != null) 
        //         { 
        //            groupData.RepairSystems.CurrentPickedGrindTarget = entry; 
        //            break; 
        //         } 
        //      } 
        //   } 
        //} 
    }
}

public class BuildAndRepairSystemQueuingGroupData
{
    public BuildAndRepairSystemQueuingGroup Settings { get; set; }
    public RepairSystemHandler RepairSystems { get; set; }
    public List<long> Assemblers { get; set; }
    public List<StatusAndLogDisplay> StatusDisplays { get; set; }
    private int[] DisplayKindIdx { get; set; }
    private double[] NextSwitchTime { get; set; }

    public BuildAndRepairSystemQueuingGroupData(int count)
    {
        DisplayKindIdx = new int[count];
        NextSwitchTime = new double[count];
    }

    public void CheckAssemblerQueues()
    {
        if (RepairSystems != null && Assemblers != null)
        {
            var missingItems = RepairSystems.MissingComponents();
            foreach (var item in missingItems)
            {
                if (item.Value > 0)
                {
                    RepairSystems.EnsureQueued(Assemblers, item.Key, item.Value);
                }
            }
        }
    }

    /// <summary> 
    /// Refresh the status display 
    /// </summary> 
    public void RefreshDisplay(double elapsedTime)
    {
        if (StatusDisplays == null || Settings == null || Settings.Displays == null) return;
        var count = StatusDisplays.Count;
        if (count > Settings.Displays.Length) count = Settings.Displays.Length;
        for (var idx = 0; idx < count; idx++)
        {
            var display = StatusDisplays[idx];
            var settings = Settings.Displays[idx];
            if (display != null && settings != null)
            {
                display.Clear();
                if (settings.DisplayKinds != null && RepairSystems != null)
                {
                    if (elapsedTime > NextSwitchTime[idx])
                    {
                        DisplayKindIdx[idx] = (DisplayKindIdx[idx] + 1) % settings.DisplayKinds.Length;
                        NextSwitchTime[idx] = elapsedTime + settings.DisplaySwitchTime;
                    }
                    switch (settings.DisplayKinds[DisplayKindIdx[idx]])
                    {
                        case DisplayKind.Status:
                            DisplayStatus(settings, display);
                            break;
                        case DisplayKind.ShortStatus:
                            DisplayShortStatus(settings, display);
                            break;
                        case DisplayKind.WeldTargets:
                            DisplayWeldTargets(settings, display);
                            break;
                        case DisplayKind.GrindTargets:
                            DisplayGrindTargets(settings, display);
                            break;
                        case DisplayKind.CollectTargets:
                            DisplayCollectTargets(settings, display);
                            break;
                        case DisplayKind.MissingItems:
                            DisplayMissingItems(settings, display);
                            break;
                        case DisplayKind.BlockWeldPriority:
                            DisplayBlockWeldPriorityList(settings, display);
                            break;
                        case DisplayKind.BlockGrindPriority:
                            DisplayBlockGrindPriorityList(settings, display);
                            break;
                    }

                    display.UpdateDisplay();
                }
            }
        }
    }

    /// <summary> 
    /// Show the short status of the BaR-System 
    /// </summary> 
    private void DisplayShortStatus(DisplayDefinition settings, StatusAndLogDisplay display)
    {
        display.AddStatus(string.Format("Online            : {0}", RepairSystems.CountOfWorking > 0));
        display.AddStatus(string.Format("CurrentWelding    : {0}", StatusAndLogDisplay.BlockName(RepairSystems.CurrentTarget)));
        var listB = RepairSystems.PossibleTargets();
        display.AddStatus(string.Format("Blocks to weld    : {0}", listB != null ? listB.Count : 0));
        display.AddStatus(string.Format("CurrentGrinding   : {0}", StatusAndLogDisplay.BlockName(RepairSystems.CurrentGrindTarget)));
        listB = RepairSystems.PossibleGrindTargets();
        display.AddStatus(string.Format("Blocks to grind   : {0}", listB != null ? listB.Count : 0));
        var listF = RepairSystems.PossibleCollectTargets();
        display.AddStatus(string.Format("Floating items    : {0}", listF != null ? listF.Count : 0));
        display.AddStatus(string.Format("Missing item kinds: {0}", RepairSystems.MissingComponents().Count));
    }

    /// <summary> 
    /// Show the detailed status of the BaR-System 
    /// </summary> 
    private void DisplayStatus(DisplayDefinition settings, StatusAndLogDisplay display)
    {
        DisplayShortStatus(settings, display);
        display.AddStatus(string.Format("Search mode       : {0}", RepairSystems.SearchMode));
        display.AddStatus(string.Format("Work mode         : {0}", RepairSystems.WorkMode));
        display.AddStatus(string.Format("Build projected   : {0}", RepairSystems.AllowBuild));
        display.AddStatus(string.Format("Weld mode         : {0}", RepairSystems.WeldMode));
        display.AddStatus(string.Format("UseIgnoreColor    : {0}", RepairSystems.UseIgnoreColor));
        display.AddStatus(string.Format("Script Controlled : {0}", RepairSystems.ScriptControlled));
    }

    /// <summary> 
    /// Show the List of blocks to weld 
    /// </summary> 
    private void DisplayWeldTargets(DisplayDefinition settings, StatusAndLogDisplay display)
    {
        var list = RepairSystems.PossibleTargets();
        display.AddStatus(string.Format("Weld Targets: Count {0}", list != null ? list.Count : 0));
        if (list == null) return;
        var iI = 2;
        foreach (var entry in list)
        {
            if (iI >= settings.DisplayMaxLines)
            {
                display.AddStatus(" ..");
                break;
            }
            display.AddStatus(string.Format(" {0}", StatusAndLogDisplay.BlockName(entry)));
            iI++;
        }
    }

    /// <summary> 
    /// Show the List of blocks to grind 
    /// </summary> 
    private void DisplayGrindTargets(DisplayDefinition settings, StatusAndLogDisplay display)
    {
        var list = RepairSystems.PossibleGrindTargets();
        display.AddStatus(string.Format("Grind Targets: Count {0}", list != null ? list.Count : 0));
        if (list == null) return;
        var iI = 2;
        foreach (var entry in list)
        {
            if (iI >= settings.DisplayMaxLines)
            {
                display.AddStatus(" ..");
                break;
            }
            display.AddStatus(string.Format(" {0}", StatusAndLogDisplay.BlockName(entry)));
            iI++;
        }
    }

    /// <summary> 
    /// Show the List of collectable floating objects 
    /// </summary> 
    private void DisplayCollectTargets(DisplayDefinition settings, StatusAndLogDisplay display)
    {
        var list = RepairSystems.PossibleCollectTargets();
        display.AddStatus(string.Format("Collect Targets: Count {0}", list != null ? list.Count : 0));
        if (list == null) return;
        var iI = 2;
        foreach (var entry in list)
        {
            if (iI >= settings.DisplayMaxLines)
            {
                display.AddStatus(" ..");
                break;
            }
            display.AddStatus(string.Format(" {0}", StatusAndLogDisplay.BlockName(entry)));
            iI++;
        }
    }

    /// <summary> 
    /// Show the List of missing materials 
    /// </summary> 
    private void DisplayMissingItems(DisplayDefinition settings, StatusAndLogDisplay display)
    {
        var list = RepairSystems.MissingComponents();
        display.AddStatus(string.Format("Missing Items: Count {0}", list != null ? list.Count : 0));
        if (list == null) return;
        var iI = 2;
        foreach (var entry in list)
        {
            if (iI >= settings.DisplayMaxLines)
            {
                display.AddStatus(" ..");
                break;
            }
            display.AddStatus(string.Format(" {0}: Amount={1}", entry.Key.SubtypeName, entry.Value));
            iI++;
        }
    }

    /// <summary> 
    /// Show the List of block classes and there enabled state 
    /// </summary> 
    private void DisplayBlockWeldPriorityList(DisplayDefinition settings, StatusAndLogDisplay display)
    {
        display.AddStatus("Weld Priority:");
        var list = RepairSystems.WeldPriorityList();
        if (list == null) return;
        foreach (var entry in list)
        {
            display.AddStatus(string.Format("  {0}/{1}", entry.ItemClass, entry.Enabled));
        }
    }

    /// <summary> 
    /// Show the List of block classes and there enabled state 
    /// </summary> 
    private void DisplayBlockGrindPriorityList(DisplayDefinition settings, StatusAndLogDisplay display)
    {
        display.AddStatus("Grind Priority:");
        var list = RepairSystems.GrindPriorityList();
        if (list == null) return;
        foreach (var entry in list)
        {
            display.AddStatus(string.Format("  {0}/{1}", entry.ItemClass, entry.Enabled));
        }
    }

}

/// <summary> 
///    Class to handle the RepairSystems 
/// </summary> 
public class RepairSystemHandler : EntityHandler<IMyShipWelder>
{
    private Program _Program;
    public void SetProgram(Program program)
    {
        _Program = program;
    }


    private Func<IEnumerable<long>, VRage.Game.MyDefinitionId, int, int> _EnsureQueued;
    private Func<IMyProjector, Dictionary<VRage.Game.MyDefinitionId, VRage.MyFixedPoint>, int> _NeededComponents4Blueprint;
    /// <summary> 
    /// The block classes the system distinguish 
    /// </summary> 
    public enum BlockClass
    {
        AutoRepairSystem = 1,
        ShipController,
        Thruster,
        Gyroscope,
        CargoContainer,
        Conveyor,
        ControllableGun,
        PowerBlock,
        ProgrammableBlock,
        Projector,
        FunctionalBlock,
        ProductionBlock,
        Door,
        ArmorBlock,
        DisplayPanel,
        Lighting,
        SensorDevice,
        CommunicationBlock
    }

    /// <summary> 
    /// The componet classes the system distinguish 
    /// </summary> 
    public enum ComponentClass
    {
        Material = 1,
        Ingot,
        Ore,
        Stone,
        Gravel
    }

    /// <summary> 
    /// The search modes supported by the block 
    /// </summary> 
    public enum SearchModes
    {
        Grids = 0x0001,
        BoundingBox = 0x0002
    }

    /// <summary> 
    /// The work modes supported by the block 
    /// </summary> 
    public enum WorkModes
    {
        /// <summary> 
        /// Grind only if nothing to weld 
        /// </summary> 
        WeldBeforeGrind = 0x0001,

        /// <summary> 
        /// Weld only if nothing to grind 
        /// </summary> 
        GrindBeforeWeld = 0x0002,

        /// <summary> 
        /// Grind only if nothing to weld or 
        /// build waiting for missing items 
        /// </summary> 
        GrindIfWeldGetStuck = 0x0004,

        /// <summary> 
        /// Only welding is allowed 
        /// </summary> 
        WeldOnly = 0x0008,

        /// <summary> 
        /// Only grinding is allowed 
        /// </summary> 
        GrindOnly = 0x0010
    }

    /// <summary> 
    /// The weld modes (how far blocks are welded) 
    /// </summary> 
    public enum AutoWeldOptions
    {
        /// <summary>Weld blocks to 100% integrity (default).</summary>
        WeldFull = 0,
        /// <summary>Weld only to functional threshold (CriticalIntegrityRatio). Was 'WeldOptionFunctionalOnly'.</summary>
        WeldFunctional = 1,
        /// <summary>Only place projected blocks; never repair or continue welding existing blocks.</summary>
        WeldSkeleton = 2
    }

    /// <summary> 
    /// Block/Component class and it's state 
    /// </summary> 
    public class ClassState<T> where T : struct
    {
        public T ItemClass { get; }
        public bool Enabled { get; }
        public ClassState(T itemClass, bool enabled)
        {
            ItemClass = itemClass;
            Enabled = enabled;
        }
    }

    /// <summary> 
    /// Set the Help Others state 
    /// </summary> 
    public bool HelpOther
    {
        get
        {
            return _Entities.Count > 0 ? _Entities[0].HelpOthers : false;
        }
        set
        {
            foreach (var entity in _Entities) entity.HelpOthers = value;
        }
    }

    /// <summary> 
    /// Set AllowBuild (projected blocks) 
    /// </summary> 
    public bool AllowBuild
    {
        get
        {
            return _Entities.Count > 0 ? _Entities[0].GetValueBool("BuildAndRepair.AllowBuild") : false;
        }
        set
        {
            foreach (var entity in _Entities) entity.SetValueBool("BuildAndRepair.AllowBuild", value);
        }
    }

    /// <summary> 
    /// Set the search mode of the block 
    /// </summary> 
    public SearchModes SearchMode
    {
        get
        {
            return _Entities.Count > 0 ? (SearchModes)GetValue<long>("BuildAndRepair.Mode") : SearchModes.Grids;
        }
        set
        {
            foreach (var entity in _Entities) entity.SetValue<long>("BuildAndRepair.Mode", (long)value);
        }
    }

    /// <summary>
    /// Set the work mode of the block
    /// </summary>
    public WorkModes WorkMode
    {
        get
        {
            return _Entities.Count > 0 ? (WorkModes)GetValue<long>("BuildAndRepair.WorkMode") : WorkModes.WeldBeforeGrind;
        }
        set
        {
            foreach (var entity in _Entities) entity.SetValue<long>("BuildAndRepair.WorkMode", (long)value);
        }
    }

    /// <summary> 
    /// Enable/Disable the use of the Ignore Color 
    /// If enabled block's with color 'IgnoreColor' 
    /// will be ignored. 
    /// You could use this do have intentionally unweldet block's 
    /// and still use autorepair of the rest. 
    /// </summary> 
    public bool UseIgnoreColor
    {
        get
        {
            return _Entities.Count > 0 ? _Entities[0].GetValueBool("BuildAndRepair.UseIgnoreColor") : false;
        }
        set
        {
            foreach (var entity in _Entities) entity.SetValueBool("BuildAndRepair.UseIgnoreColor", value);
        }
    }

    /// <summary> 
    /// Set the ignore color 
    /// X=Hue         0 .. 1 -> * 360 -> Displayed value 
    /// Y=Saturation -1 .. 1 -> * 100 -> Displayed value 
    /// Z=Value      -1 .. 1 -> * 100 -> Displayed value 
    /// </summary> 
    public Vector3 IgnoreColor
    {
        get
        {
            return _Entities.Count > 0 ? GetValue<Vector3>("BuildAndRepair.IgnoreColor") : Vector3.Zero;
        }
        set
        {
            foreach (var entity in _Entities) entity.SetValue<Vector3>("BuildAndRepair.IgnoreColor", value);
        }
    }

    /// <summary> 
    /// Enable/Disable the use of the Grind Color 
    /// If enabled block's with color 'GrindColor' 
    /// will be grinded. 
    /// </summary> 
    public bool UseGrindColor
    {
        get
        {
            return _Entities.Count > 0 ? _Entities[0].GetValueBool("BuildAndRepair.UseGrindColor") : false;
        }
        set
        {
            foreach (var entity in _Entities) entity.SetValueBool("BuildAndRepair.UseGrindColor", value);
        }
    }

    /// <summary> 
    /// Set the grind color 
    /// X=Hue         0 .. 1 -> * 360 -> Displayed value 
    /// Y=Saturation -1 .. 1 -> * 100 -> Displayed value 
    /// Z=Value      -1 .. 1 -> * 100 -> Displayed value 
    /// </summary> 
    public Vector3 GrindColor
    {
        get
        {
            return _Entities.Count > 0 ? GetValue<Vector3>("BuildAndRepair.GrindColor") : Vector3.Zero;
        }
        set
        {
            foreach (var entity in _Entities) entity.SetValue<Vector3>("BuildAndRepair.GrindColor", value);
        }
    }

    /// <summary> 
    /// If set autogrind enemy blocks in range 
    /// </summary> 
    public bool GrindJanitorEnemies
    {
        get
        {
            return _Entities.Count > 0 ? _Entities[0].GetValueBool("BuildAndRepair.GrindJanitorEnemies") : false;
        }
        set
        {
            foreach (var entity in _Entities) entity.SetValueBool("BuildAndRepair.GrindJanitorEnemies", value);
        }
    }

    /// <summary> 
    /// If set autogrind not owned blocks in range 
    /// </summary> 
    public bool GrindJanitorNotOwned
    {
        get
        {
            return _Entities.Count > 0 ? _Entities[0].GetValueBool("BuildAndRepair.GrindJanitorNotOwned") : false;
        }
        set
        {
            foreach (var entity in _Entities) entity.SetValueBool("BuildAndRepair.GrindJanitorNotOwned", value);
        }
    }

    /// <summary> 
    /// If set autogrind blocks owned by neutrals in range 
    /// </summary> 
    public bool GrindJanitorNeutrals
    {
        get
        {
            return _Entities.Count > 0 ? _Entities[0].GetValueBool("BuildAndRepair.GrindJanitorNeutrals") : false;
        }
        set
        {
            foreach (var entity in _Entities) entity.SetValueBool("BuildAndRepair.GrindJanitorNeutrals", value);
        }
    }

    /// <summary> 
    /// If set autogrind grinds blocks only down to the 'Out of order' level 
    /// </summary> 
    public bool GrindJanitorOptionDisableOnly
    {
        get
        {
            return _Entities.Count > 0 ? _Entities[0].GetValueBool("BuildAndRepair.GrindJanitorOptionDisableOnly") : false;
        }
        set
        {
            foreach (var entity in _Entities) entity.SetValueBool("BuildAndRepair.GrindJanitorOptionDisableOnly", value);
        }
    }

    /// <summary> 
    /// If set autogrind grinds blocks only down to the 'Hack' level 
    /// </summary> 
    public bool GrindJanitorOptionHackOnly
    {
        get
        {
            return _Entities.Count > 0 ? _Entities[0].GetValueBool("BuildAndRepair.GrindJanitorOptionHackOnly") : false;
        }
        set
        {
            foreach (var entity in _Entities) entity.SetValueBool("BuildAndRepair.GrindJanitorOptionHackOnly", value);
        }
    }

    /// <summary> 
    /// Controls how far blocks are welded: WeldFull (100%), WeldFunctional (functional threshold), or WeldSkeleton (place only). 
    /// </summary> 
    public AutoWeldOptions WeldMode
    {
        get
        {
            return _Entities.Count > 0 ? (AutoWeldOptions)GetValue<long>("BuildAndRepair.WeldMode") : AutoWeldOptions.WeldFull;
        }
        set
        {
            foreach (var entity in _Entities) entity.SetValue<long>("BuildAndRepair.WeldMode", (long)value);
        }
    }


    /// <summary> 
    /// Set the width of the working area 
    /// </summary> 
    public float AreaWidth
    {
        get
        {
            return _Entities.Count > 0 ? _Entities[0].GetValueFloat("BuildAndRepair.AreaWidth") : 0;
        }
        set
        {
            foreach (var entity in _Entities) entity.SetValueFloat("BuildAndRepair.AreaWidth", value);
        }
    }

    /// <summary> 
    /// Set the left/right offset of the working area from block center 
    /// </summary> 
    public float AreaOffsetLeftRight
    {
        get
        {
            return _Entities.Count > 0 ? _Entities[0].GetValueFloat("BuildAndRepair.AreaOffsetLeftRight") : 0;
        }
        set
        {
            foreach (var entity in _Entities) entity.SetValueFloat("BuildAndRepair.AreaOffsetLeftRight", value);
        }
    }

    /// <summary> 
    /// Set the height of the working area 
    /// </summary> 
    public float AreaHeight
    {
        get
        {
            return _Entities.Count > 0 ? _Entities[0].GetValueFloat("BuildAndRepair.AreaHeight") : 0;
        }
        set
        {
            foreach (var entity in _Entities) entity.SetValueFloat("BuildAndRepair.AreaHeight", value);
        }
    }

    /// <summary> 
    /// Set the up/down offset of the working area from block center 
    /// </summary> 
    public float AreaOffsetUpDown
    {
        get
        {
            return _Entities.Count > 0 ? _Entities[0].GetValueFloat("BuildAndRepair.AreaOffsetUpDown") : 0;
        }
        set
        {
            foreach (var entity in _Entities) entity.SetValueFloat("BuildAndRepair.AreaOffsetUpDown", value);
        }
    }

    /// <summary> 
    /// Set the depth of the working area 
    /// </summary> 
    public float AreaDepth
    {
        get
        {
            return _Entities.Count > 0 ? _Entities[0].GetValueFloat("BuildAndRepair.AreaDepth") : 0;
        }
        set
        {
            foreach (var entity in _Entities) entity.SetValueFloat("BuildAndRepair.AreaDepth", value);
        }
    }

    /// <summary>
    /// Set the front/back offset of the working area from block center
    /// </summary>
    public float AreaOffsetFrontBack
    {
        get
        {
            return _Entities.Count > 0 ? _Entities[0].GetValueFloat("BuildAndRepair.AreaOffsetFrontBack") : 0;
        }
        set
        {
            foreach (var entity in _Entities) entity.SetValueFloat("BuildAndRepair.AreaOffsetFrontBack", value);
        }
    }

    /// <summary> 
    /// Get a list with all known block classes and there 
    /// weld enabled state in descending order of priority. 
    /// </summary> 
    public MemorySafeList<ClassState<BlockClass>> WeldPriorityList()
    {
        if (_Entities.Count > 0)
        {
            var list = GetValue<MemorySafeList<string>>("BuildAndRepair.WeldPriorityList");
            var blockList = new MemorySafeList<ClassState<BlockClass>>();
            foreach (var item in list)
            {
                var values = item.Split(';');
                BlockClass blockClass;
                bool enabled;
                if (Enum.TryParse<BlockClass>(values[0], out blockClass) &&
                   bool.TryParse(values[1], out enabled))
                {
                    blockList.Add(new ClassState<BlockClass>(blockClass, enabled));
                }
            }
            return blockList;
        }
        return null;
    }

    /// <summary> 
    /// Get the weld priority of the given block class 
    /// </summary> 
    public int GetWeldPriority(BlockClass blockClass)
    {
        if (_Entities.Count > 0)
        {
            var getPriority = GetValue<Func<int, int>>("BuildAndRepair.GetWeldPriority");
            return getPriority((int)blockClass);
        }
        else return int.MaxValue;
    }

    /// <summary> 
    /// Set the weld priority of the given block class 
    /// (lower number higher priority) 
    /// </summary> 
    public void SetWeldPriority(BlockClass blockClass, int prio)
    {
        foreach (var entity in _Entities)
        {
            var setPriority = entity.GetValue<Action<int, int>>("BuildAndRepair.SetWeldPriority");
            setPriority((int)blockClass, prio);
        }
    }

    /// <summary> 
    /// Get the weld enabled state of the given block class 
    /// Enabled=True Block of that class will be repaired/build 
    /// Enabled=False Block's of that class will be ignored 
    /// </summary> 
    public bool GetWeldEnabled(BlockClass blockClass)
    {
        if (_Entities.Count > 0)
        {
            var getEnabled = GetValue<Func<int, bool>>("BuildAndRepair.GetWeldEnabled");
            return getEnabled((int)blockClass);
        }
        else return false;
    }

    /// <summary> 
    /// Set the weld enabled state of the given block class 
    /// (see GetEnabled) 
    /// </summary> 
    public void SetWeldEnabled(BlockClass blockClass, bool enabled)
    {
        foreach (var entity in _Entities)
        {
            var setEnabled = entity.GetValue<Action<int, bool>>("BuildAndRepair.SetWeldEnabled");
            setEnabled((int)blockClass, enabled);
        }
    }

    /// <summary> 
    /// Get a list with all known block classes and there 
    /// grind enabled state in descending order of priority. 
    /// </summary> 
    public MemorySafeList<ClassState<BlockClass>> GrindPriorityList()
    {
        if (_Entities.Count > 0)
        {
            var list = GetValue<MemorySafeList<string>>("BuildAndRepair.GrindPriorityList");
            var blockList = new MemorySafeList<ClassState<BlockClass>>();
            foreach (var item in list)
            {
                var values = item.Split(';');
                BlockClass blockClass;
                bool enabled;
                if (Enum.TryParse<BlockClass>(values[0], out blockClass) &&
                   bool.TryParse(values[1], out enabled))
                {
                    blockList.Add(new ClassState<BlockClass>(blockClass, enabled));
                }
            }
            return blockList;
        }
        return null;
    }

    /// <summary> 
    /// Get the grind priority of the given block class 
    /// </summary> 
    public int GetGrindPriority(BlockClass blockClass)
    {
        if (_Entities.Count > 0)
        {
            var getPriority = GetValue<Func<int, int>>("BuildAndRepair.GetGrindPriority");
            return getPriority((int)blockClass);
        }
        else return int.MaxValue;
    }

    /// <summary> 
    /// Set the grind priority of the given block class 
    /// (lower number higher priority) 
    /// </summary> 
    public void SetGrindPriority(BlockClass blockClass, int prio)
    {
        foreach (var entity in _Entities)
        {
            var setPriority = entity.GetValue<Action<int, int>>("BuildAndRepair.SetGrindPriority");
            setPriority((int)blockClass, prio);
        }
    }

    /// <summary> 
    /// Get the grind enabled state of the given block class 
    /// Enabled=True Block of that class will be grinded 
    /// Enabled=False Block's of that class will be ignored 
    /// </summary> 
    public bool GetGrindEnabled(BlockClass blockClass)
    {
        if (_Entities.Count > 0)
        {
            var getEnabled = GetValue<Func<int, bool>>("BuildAndRepair.GetGrindEnabled");
            return getEnabled((int)blockClass);
        }
        else return false;
    }

    /// <summary> 
    /// Set the grind enabled state of the given block class 
    /// (see GetEnabled) 
    /// </summary> 
    public void SetGrindEnabled(BlockClass blockClass, bool enabled)
    {
        foreach (var entity in _Entities)
        {
            var setEnabled = entity.GetValue<Action<int, bool>>("BuildAndRepair.SetGrindEnabled");
            setEnabled((int)blockClass, enabled);
        }
    }

    /// <summary> 
    /// Get a list with all known component classes and their
    /// enabled state in descending order of priority. 
    /// </summary> 
    public MemorySafeList<ClassState<ComponentClass>> ComponentClassList()
    {
        if (_Entities.Count > 0)
        {
            var list = GetValue<MemorySafeList<string>>("BuildAndRepair.ComponentClassList");
            var compList = new MemorySafeList<ClassState<ComponentClass>>();
            foreach (var item in list)
            {
                var values = item.Split(';');
                ComponentClass compClass;
                bool enabled;
                if (Enum.TryParse<ComponentClass>(values[0], out compClass) &&
                   bool.TryParse(values[1], out enabled))
                {
                    compList.Add(new ClassState<ComponentClass>(compClass, enabled));
                }
            }
            return compList;
        }
        return null;
    }

    /// <summary> 
    /// Get the priority of the given component class 
    /// </summary> 
    public int GetCollectPriority(ComponentClass compClass)
    {
        if (_Entities.Count > 0)
        {
            var getPriority = GetValue<Func<int, int>>("BuildAndRepair.GetCollectPriority");
            return getPriority((int)compClass);
        }
        else return int.MaxValue;
    }

    /// <summary> 
    /// Set the priority of the given component class 
    /// (lower number higher priority) 
    /// </summary> 
    public void SetCollectPriority(ComponentClass compClass, int prio)
    {
        foreach (var entity in _Entities)
        {
            var setPriority = entity.GetValue<Action<int, int>>("BuildAndRepair.SetCollectPriority");
            setPriority((int)compClass, prio);
        }
    }

    /// <summary> 
    /// Get the enabled state of the given component class 
    /// Enabled=True Component of that class will be collected 
    /// Enabled=False Component's of that class will be ignored 
    /// </summary> 
    public bool GetCollectEnabled(ComponentClass compClass)
    {
        if (_Entities.Count > 0)
        {
            var getEnabled = GetValue<Func<int, bool>>("BuildAndRepair.GetCollectEnabled");
            return getEnabled((int)compClass);
        }
        else return false;
    }

    /// <summary> 
    /// Set the enabled state of the given component class 
    /// (see GetEnabled) 
    /// </summary> 
    public void SetCollectEnabled(ComponentClass compClass, bool enabled)
    {
        foreach (var entity in _Entities)
        {
            var setEnabled = entity.GetValue<Action<int, bool>>("BuildAndRepair.SetCollectEnabled");
            setEnabled((int)compClass, enabled);
        }
    }

    /// <summary> 
    /// Set if the Block should only collect floating items (ore/ingot/material) 
    /// if nothing else to do (no welding, no grinding, no material for welding) 
    /// </summary> 
    public bool CollectIfIdle
    {
        get
        {
            return _Entities.Count > 0 ? _Entities[0].GetValueBool("BuildAndRepair.CollectIfIdle") : false;
        }
        set
        {
            foreach (var entity in _Entities) entity.SetValueBool("BuildAndRepair.CollectIfIdle", value);
        }
    }

    /// <summary> 
    /// Set if the Block should push all ore/ingot immediately out of its inventory, 
    /// else this will happen only if no more room to store the next items to be picked. 
    /// </summary> 
    public bool PushIngotOreImmediately
    {
        get
        {
            return _Entities.Count > 0 ? _Entities[0].GetValueBool("BuildAndRepair.PushIngotOreImmediately") : false;
        }
        set
        {
            foreach (var entity in _Entities) entity.SetValueBool("BuildAndRepair.PushIngotOreImmediately", value);
        }
    }

    /// <summary> 
    /// Get the block that is currently being repaired/build. 
    /// </summary> 
    public IMySlimBlock CurrentTarget
    {
        get
        {
            return _Entities.Count > 0 ? GetValue<IMySlimBlock>("BuildAndRepair.CurrentTarget") : null;
        }
    }

    /// <summary> 
    /// Get the block that is currently being grinded. 
    /// </summary> 
    public IMySlimBlock CurrentGrindTarget
    {
        get
        {
            return _Entities.Count > 0 ? GetValue<IMySlimBlock>("BuildAndRepair.CurrentGrindTarget") : null;
        }
    }

    /// <summary> 
    /// Set if the Block is controlled by script. 
    /// (If controlled by script use PossibleTargets and CurrentPickedTarget  
    /// to set the block that should be build/repaired) 
    /// </summary> 
    public bool ScriptControlled
    {
        get
        {
            return _Entities.Count > 0 ? _Entities[0].GetValueBool("BuildAndRepair.ScriptControlled") : false;
        }
        set
        {
            foreach (var entity in _Entities) entity.SetValueBool("BuildAndRepair.ScriptControlled", value);
        }
    }

    /// <summary> 
    /// Get a list of missing components. 
    /// </summary> 
    public MemorySafeDictionary<VRage.Game.MyDefinitionId, int> MissingComponents()
    {
        var missingItems = new MemorySafeDictionary<VRage.Game.MyDefinitionId, int>();
        foreach (var entity in _Entities)
        {
            var dict = entity.GetValue<MemorySafeDictionary<VRage.Game.MyDefinitionId, int>>("BuildAndRepair.MissingComponents");
            //Merge dictionaries but only first report of an item or higher amount 
            //(do not add up the missings, as overlapping systems report same missing items) 
            if (dict != null && dict.Count > 0)
            {
                int value;
                foreach (var newItem in dict)
                {
                    if (missingItems.TryGetValue(newItem.Key, out value))
                    {
                        if (newItem.Value > value) missingItems[newItem.Key] = newItem.Value;
                    }
                    else
                    {
                        missingItems.Add(newItem.Key, newItem.Value);
                    }
                }
            }
        }
        return missingItems;
    }

    /// <summary> 
    /// Get a list of possible repair/build targets. 
    /// (Contains only damaged/deformed/new block's in range of the system) 
    /// </summary> 
    public MemorySafeList<IMySlimBlock> PossibleTargets()
    {
        if (_Entities.Count > 0)
        {
            return GetValue<MemorySafeList<IMySlimBlock>>("BuildAndRepair.PossibleTargets");
        }
        return null;
    }

    public T GetValue<T>(string propertyName)
    {
        if (_Entities.Count > 0)
        {
            try
            {
                return _Entities[0].GetValue<T>(propertyName);
            }
            catch (Exception)
            {
                // ignored
            }
        }
        return default(T);
    }

    /// <summary> 
    /// Get the block that should currently be repaired/built. 
    /// In order to build the given block the property 'ScriptControlled' has to be true and 
    /// the block has to be in the list of 'PossibleTargets'. 
    /// If 'ScriptControlled' is true and the block is not in the 'PossibleTargets' 
    /// the system will do nothing. 
    /// </summary> 
    public IMySlimBlock CurrentPickedTarget
    {
        get
        {
            return _Entities.Count > 0 ? GetValue<IMySlimBlock>("BuildAndRepair.CurrentPickedTarget") : null;
        }
        set
        {
            foreach (var entity in _Entities) entity.SetValue("BuildAndRepair.CurrentPickedTarget", value);
        }
    }

    /// <summary> 
    /// Get a list of possible grind targets. 
    /// </summary> 
    public MemorySafeList<IMySlimBlock> PossibleGrindTargets()
    {
        if (_Entities.Count > 0)
        {
            return GetValue<MemorySafeList<IMySlimBlock>>("BuildAndRepair.PossibleGrindTargets");
        }
        return null;
    }

    /// <summary> 
    /// Get the block that should currently be grinded. 
    /// In order to grind the given block the property 'ScriptControlled' has to be true and 
    /// the block has to be in the list of 'PossibleGrindTargets'. 
    /// If 'ScriptControlled' is true and the block is not in the 'PossibleGrindTargets' 
    /// the system will do nothing. 
    /// </summary> 
    public IMySlimBlock CurrentPickedGrindTarget
    {
        get
        {
            return _Entities.Count > 0 ? GetValue<IMySlimBlock>("BuildAndRepair.CurrentPickedGrindTarget") : null;
        }
        set
        {
            foreach (var entity in _Entities) entity.SetValue("BuildAndRepair.CurrentPickedGrindTarget", value);
        }
    }

    /// <summary>
    /// Get a list of possible collect targets.
    /// </summary>
    public MemorySafeList<IMyEntity> PossibleCollectTargets()
    {
        if (_Entities.Count > 0)
        {
            return GetValue<MemorySafeList<IMyEntity>>("BuildAndRepair.PossibleCollectTargets");
        }
        return null;
    }

    /// <summary> 
    /// Ensures that the given amount is either in inventory or the production 
    /// queue of the given production blocks 
    /// </summary> 
    public int EnsureQueued(IEnumerable<long> productionBlockIds, VRage.Game.MyDefinitionId materialId, int amount)
    {
        if (_Entities.Count > 0)
        {
            if (_EnsureQueued == null)
            {
                _EnsureQueued = GetValue<Func<IEnumerable<long>, VRage.Game.MyDefinitionId, int, int>>("BuildAndRepair.ProductionBlock.EnsureQueued");
            }

            if (_EnsureQueued != null)
            {
                return _EnsureQueued(productionBlockIds, materialId, amount);
            }
            return -3;
        }
        return -2;
    }

    /// <summary> 
    /// Retrieve the total components amount needed to build the projected 
    /// blueprint 
    /// </summary> 
    /// <param name="projector"></param> 
    /// <param name="componentList"></param> 
    /// <returns></returns> 
    public int NeededComponents4Blueprint(IMyProjector projector, Dictionary<VRage.Game.MyDefinitionId, VRage.MyFixedPoint> componentList)
    {
        if (_Entities.Count > 0)
        {
            if (_NeededComponents4Blueprint == null)
            {
                _NeededComponents4Blueprint = GetValue<Func<IMyProjector, Dictionary<VRage.Game.MyDefinitionId, VRage.MyFixedPoint>, int>>("BuildAndRepair.Inventory.NeededComponents4Blueprint");
            }

            if (_NeededComponents4Blueprint != null)
            {
                return _NeededComponents4Blueprint(projector, componentList);
            }
            return -3;
        }
        return -2;
    }
}
// NOTE: Rely on mod-provided MemorySafeList/MemorySafeDictionary types exposed to PB; do not redefine here to avoid conflicts.

/// <summary> 
///    Class to handle Entities 
/// </summary> 
public class EntityHandler<T> : EntityHandler where T : class, IMyTerminalBlock
{
    protected readonly List<T> _Entities = new List<T>();
    protected readonly HashSet<MyDefinitionId> _DefinitionIdsInclude = new HashSet<MyDefinitionId>();
    protected readonly HashSet<MyDefinitionId> _DefinitionIdsExclude = new HashSet<MyDefinitionId>();

    public IEnumerable<T> Entities
    {
        get
        {
            return _Entities;
        }
    }

    public HashSet<MyDefinitionId> DefinitionIdsInclude
    {
        get
        {
            return _DefinitionIdsInclude;
        }
    }

    public HashSet<MyDefinitionId> DefinitionIdsExclude
    {
        get
        {
            return _DefinitionIdsExclude;
        }
    }

    public bool AreEnabled { get; private set; }

    /// <summary> 
    /// Count of Working Entities (on and functional) 
    /// </summary> 
    public int CountOfWorking
    {
        get
        {
            var res = 0;
            foreach (var entity in _Entities) if (entity.IsWorking && entity.IsFunctional) res++;
            return res;
        }
    }

    /// <summary> 
    /// Get total count 
    /// </summary> 
    protected override int GetCount()
    {
        return _Entities.Count;
    }

    /// <summary> 
    /// Load entities from group 
    /// </summary> 
    public override void Init(IMyBlockGroup group, bool add = false)
    {
        if (!add) { _Entities.Clear(); AreEnabled = false; }
        var entities = new List<T>();
        group.GetBlocksOfType(entities);
        foreach (var entity in entities)
        {
            AddEntity(entity);
        }
        CheckEnabled();
    }

    /// <summary>
    /// Load entity by name
    /// </summary>
    public override void Init(VRage.Game.ModAPI.Ingame.IMyEntity newEntity, bool add = false)
    {
        if (!add) { _Entities.Clear(); AreEnabled = false; }
        var entity = newEntity as T;
        if (AddEntity(entity))
        {
            CheckEnabled();
        }
    }

    /// <summary>
    /// Load entity filtered by given collect function
    /// </summary>
    public void Init(IMyGridTerminalSystem gridTerminalSystem, Func<T, bool> collect = null, bool add = false)
    {
        if (!add) { _Entities.Clear(); AreEnabled = false; }
        if (gridTerminalSystem != null)
        {
            var entities = new List<T>();
            gridTerminalSystem.GetBlocksOfType<T>(entities, collect);
            foreach (var entity in entities)
            {
                AddEntity(entity);
            }
            CheckEnabled();
        }
    }

    /// <summary>
    /// Add an entity to the handler's tracked list
    /// </summary>
    protected virtual bool AddEntity(T entity)
    {
        if (entity == null || _Entities.IndexOf(entity) >= 0) return false;
        var newDefId = entity.BlockDefinition;
        var allowed = DefinitionIdsInclude.Count <= 0;
        foreach (var defId in DefinitionIdsInclude)
        {
            if (defId.TypeId == newDefId.TypeId && (string.IsNullOrEmpty(defId.SubtypeName) || defId.SubtypeName.Equals(newDefId.SubtypeName)))
            {
                allowed = true;
                break;
            }
        }
        if (!allowed) return false;

        foreach (var defId in DefinitionIdsExclude)
        {
            if (defId.TypeId == newDefId.TypeId && (string.IsNullOrEmpty(defId.SubtypeName) || defId.SubtypeName.Equals(newDefId.SubtypeName)))
            {
                return false;
            }
        }

        _Entities.Add(entity);
        return true;
    }

    /// <summary>
    /// Enable or disable all tracked entities
    /// </summary>
    public void Enabled(bool enabled)
    {
        foreach (var entity in _Entities)
        {
            var funcBlock = entity as IMyFunctionalBlock;
            if (funcBlock != null && funcBlock.Enabled != enabled) funcBlock.Enabled = enabled;
        }
        AreEnabled = enabled;
    }

    private void CheckEnabled()
    {
        foreach (var entity in _Entities)
        {
            if (entity.IsWorking && entity.IsFunctional)
            {
                AreEnabled = true;
                break;
            }
        }
    }
}
public abstract class EntityHandler
{
    public int Count { get { return GetCount(); } }
    public abstract void Init(IMyBlockGroup group, bool add = false);
    public abstract void Init(VRage.Game.ModAPI.Ingame.IMyEntity entity, bool add = false);
    protected abstract int GetCount();

    public static string GetCustomData(string customData, string startTag, string endTag)
    {
        var start = customData.IndexOf(startTag);
        var end = customData.LastIndexOf(endTag);
        if (start < 0 || end < 0 || end < start) return null;
        return customData.Substring(start + startTag.Length, end - start - startTag.Length);
    }

    public static string GetCustomValue(string customData, string name)
    {
        var tag = "<" + name + "=";
        var start = customData.IndexOf(tag);
        if (start < 0) return null;
        var end = customData.IndexOf("/>", start + tag.Length);
        if (end < 0) return null;
        return customData.Substring(start + tag.Length, end - start - tag.Length);
    }
}
/// <summary> 
/// Status and Log functions 
/// </summary> 
public class StatusAndLogDisplay
{
    private readonly MyGridProgram _Program;
    private readonly List<IMyTextSurface> _StatusPanels = new List<IMyTextSurface>();
    private readonly List<IMyTextSurface> _LogPanels = new List<IMyTextSurface>();
    private readonly string _AIName;
    private string _LogText = "";
    private string _StatusText = "";
    private string _ErrorText = "";
    private int _RefreshDelay;

    private readonly string[] _LcdStatusPanels;
    private readonly string[] _LcdLogPanels;

    /// <summary> 
    /// Count of Lines in Log Display 
    /// </summary> 
    public int MaxLogLines { get; set; }
    public bool ShowHeader { get; set; }

    public StatusAndLogDisplay(MyGridProgram caller, string name, string[] lcdStatusPanels, string[] lcdLogPanels)
    {
        ShowHeader = true;
        _Program = caller;
        _AIName = name;
        _LcdStatusPanels = lcdStatusPanels;
        _LcdLogPanels = lcdLogPanels;
        MaxLogLines = 20; //Default 
        ReloadDisplays();
    }

    /// <summary> 
    /// Reload the displays (after renaming, adding) 
    /// </summary> 
    public string ReloadDisplays()
    {
        var res = FindPanels(_Program, _LcdStatusPanels, _StatusPanels);
        res += FindPanels(_Program, _LcdLogPanels, _LogPanels);
        return res;
    }

    /// <summary> 
    /// Cyclic tries to reload the DisplayPanels (so the LCD could be added dynamically) 
    /// </summary> 
    public void CyclicReloadDisplays()
    {
        _RefreshDelay--;
        if (_RefreshDelay > 0) return;
        try { ReloadDisplays(); }
        catch { }
        _RefreshDelay = 20;
    }

    /// <summary> 
    /// Write a Log text 
    /// </summary> 
    public void Log(string msg)
    {
        var useHeadline = !string.IsNullOrEmpty(_AIName);
        var maxlines = MaxLogLines + (useHeadline ? 0 : 1);
        if (!string.IsNullOrEmpty(msg))
        {
            _LogText += "\n" + msg;
            var lines = _LogText.Split('\n');
            if (lines.Length >= maxlines)
            {
                _LogText = "";
                for (var a = maxlines; a > 0; a--) _LogText += "\n" + lines[lines.Length - a];
            }
        }
    }

    /// <summary> 
    /// Clears Status und Error 
    /// </summary> 
    public void Clear()
    {
        _StatusText = "";
        _ErrorText = "";
    }

    /// <summary> 
    ///  
    /// </summary> 
    internal void AddStatus(string line)
    {
        _StatusText += line + "\n";
    }

    /// <summary> 
    ///  
    /// </summary> 
    internal void AddError(string line)
    {
        _ErrorText = line + "\n";
    }

    /// <summary> 
    /// Write Status, Error, Log to the configured panels 
    /// </summary> 
    public void UpdateDisplay()
    {
        var text = string.Empty;
        if (ShowHeader) text = (_AIName ?? "") + " (" + DateTime.Now + "):\n";
        if (!string.IsNullOrEmpty(_ErrorText)) text += _ErrorText;
        if (!string.IsNullOrEmpty(_StatusText)) text += _StatusText;

        foreach (var panel in _StatusPanels) if (panel != null) SetPanelText(panel, text);
        if (_Program != null) _Program.Echo(!string.IsNullOrEmpty(_ErrorText) ? _ErrorText : text);

        text = !string.IsNullOrEmpty(_AIName) ? _AIName + _LogText : _LogText;
        foreach (var panel in _LogPanels) if (panel != null) SetPanelText(panel, text);
    }

    /// <summary> 
    /// Finds TextPanels with the given names 
    /// </summary> 
    private static string FindPanels(MyGridProgram caller, IReadOnlyList<string> names, ICollection<IMyTextSurface> list)
    {
        string res = string.Empty;
        if (caller == null)
        {
            return res;
        }
        if (names != null && names.Count > 0)
        {
            foreach (var name in names)
            {
                string blockName;
                int index;
                GetNameAndIndex(name, out blockName, out index);

                var block = caller.GridTerminalSystem != null ? caller.GridTerminalSystem.GetBlockWithName(blockName) : null;
                if (block == null)
                {
                    res += string.Format("LCD {0} not found\n", blockName);
                    continue;
                }

                var textSurface = block as IMyTextSurface;
                if (textSurface != null)
                {
                    list.Add(textSurface);
                    continue;
                }

                var textSurfaceProvider = block as IMyTextSurfaceProvider;
                if (textSurfaceProvider != null)
                {
                    if (textSurfaceProvider.SurfaceCount > index)
                    {
                        list.Add(textSurfaceProvider.GetSurface(index));
                        continue;
                    }
                    res += string.Format("LCD {0} index {1} out of range (allowed 0..{2})\n", blockName, index, textSurfaceProvider.SurfaceCount - 1);
                    continue;
                }

                res += string.Format("{0} is not an LCD.\n", blockName);
            }
        }

        if (!string.IsNullOrEmpty(res) && caller != null) caller.Echo(res);
        return res;
    }

    private static void GetNameAndIndex(string name, out string blockName, out int index)
    {
        index = 0;
        var idxStart = name.LastIndexOf('[');
        if (idxStart >= 0)
        {
            var idxEnd = name.LastIndexOf(']');
            if (idxEnd >= 0 && idxEnd > idxStart)
            {
                if (int.TryParse(name.Substring(idxStart + 1, idxEnd - idxStart - 1), out index))
                {
                    blockName = name.Substring(0, idxStart);
                }
                else blockName = name;
            }
            else blockName = name;
        }
        else blockName = name;
    }

    /// <summary> 
    /// Sets panel text if its title is either default or our name.  
    /// </summary> 
    public static void SetPanelText(IMyTextSurface panel, string text)
    {
        panel.ContentType = ContentType.TEXT_AND_IMAGE;
        panel.WriteText(text, false);
    }

    /// <summary> 
    /// Convert displayed values (Terminal) with correct units -> MW 
    /// </summary> 
    public static float PowerUnitMultiple(string unit)
    {
        if (unit.StartsWith("W")) return 0.000001f;
        if (unit.StartsWith("kW")) return 0.001f;
        if (unit.StartsWith("MW")) return 1f;
        return unit.StartsWith("GW") ? 1000f : 1f;
    }

    public static string DisplayPowerValueUnit(float value)
    {
        if (Math.Abs(value) < 0.001) return Math.Round(value * 1000000f) + "W";
        if (Math.Abs(value) < 1) return Math.Round(value * 1000f) + "kW";
        if (Math.Abs(value) < 1000) return Math.Round(value) + "MW";
        return Math.Round(value / 1000f) + "GW";
    }

    public static string DisplayPowerRate(float current, float max, string ext = "")
    {
        return string.Format("{0:0.00}% {1}{3}/{2}{3}", max > 0 ? current * 100 / max : 0, DisplayPowerValueUnit(current), DisplayPowerValueUnit(max), ext);
    }

    public static double ToDegree(double rad)
    {
        return rad * 180 / Math.PI;
    }

    /// <summary> 
    /// Get Name of Block 
    /// </summary> 
    /// <param name="block"></param> 
    /// <returns></returns> 
    public static string BlockName(object block, bool includeGrid = false)
    {
        var inventory = block as IMyInventory;
        if (inventory != null)
        {
            block = inventory.Owner;
        }

        var slimBlock = block as IMySlimBlock;
        if (slimBlock != null)
        {
            if (slimBlock.FatBlock != null) block = slimBlock.FatBlock;
            else
            {
                if (includeGrid) return string.Format("{0}.{1}", slimBlock.CubeGrid != null ? slimBlock.CubeGrid.DisplayName : "Unknown Grid", slimBlock.BlockDefinition.SubtypeName);
                return slimBlock.BlockDefinition.SubtypeName;
            }
        }

        var terminalBlock = block as IMyTerminalBlock;
        if (terminalBlock != null)
        {
            if (includeGrid) return string.Format("{0}.{1}", terminalBlock.CubeGrid != null ? terminalBlock.CubeGrid.DisplayName : "Unknown Grid", terminalBlock.CustomName);
            return terminalBlock.CustomName;
        }

        var cubeBlock = block as IMyCubeBlock;
        if (cubeBlock != null)
        {
            if (includeGrid) return string.Format("{0} {1}/{2}", cubeBlock.CubeGrid != null ? cubeBlock.CubeGrid.DisplayName : "Unknown Grid", cubeBlock.BlockDefinition.TypeIdString, cubeBlock.BlockDefinition.SubtypeName);
            return cubeBlock.BlockDefinition.SubtypeName;
        }

        var entity = block as IMyEntity;
        if (entity != null)
        {
            return string.Format("{0} ({1})", entity.DisplayName, entity.EntityId);
        }

        var cubeGrid = block as IMyCubeGrid;
        if (cubeGrid != null) return cubeGrid.DisplayName;

        return block != null ? block.ToString() : "NULL";
    }
}
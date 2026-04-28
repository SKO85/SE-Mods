// Version: v2.5.3 - 16.04.2026 -
// Compatible with: SKO Nanobot Build and Repair System (Maintained) v2.5.0+
// This script only works with SKO's maintained versions of the mod.
// It will NOT work with the original mod by Dummy08.

static double UpdateIntervalAssemblerQueues = 0.1; //Update every x seconds (0=as fast as possible, 0.5=every 500ms, ..) 
static double UpdateIntervalGrinding = 0.1; //Update every x seconds (0=as fast as possible, 0.5=every 500ms, ..) 

// Configure groups below. Full docs:
// https://sko85.github.io/SE-Mods/pages/Build-and-Repair-System/Companion-Script/
//
// LCDs can also be auto-attached via a [BaR:group] tag in the block name, e.g.
//   "Hangar LCD [BaR:1]"           -> group 1
//   "Cockpit [BaR:1@0,1,2]"        -> surfaces 0,1,2 of the cockpit -> group 1
// group = group Name or 1-based index into BuildAndRepairSystemQueuingGroups.
//
// Per-LCD overrides go in Custom Data:
//   @BaR                   (unscoped; cockpit-wide base)
//   Kinds=Status,WeldTargets,MissingItems
//   MaxLines=15
//   SwitchTime=4
//   FontSize=auto          (number or 'auto' to fit surface)
//   @/BaR
//   @BaR@0 ... @/BaR       (scoped: overrides for surface 0 only)
//
// Valid Kinds: ShortStatus, Status, WeldTargets, GrindTargets, CollectTargets,
//              MissingItems, BlockWeldPriority, BlockGrindPriority
// Tag and Custom Data reload every ~30 s; recompile the PB for immediate effect.
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

// No user changeable settings behind this point

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

public class BuildAndRepairSystemQueuingGroup
{
    public string Name { get; set; }
    public string[] BuildAndRepairSystemNames { get; set; }
    public string BuildAndRepairSystemGroupName { get; set; }

    public string[] AssemblerNames { get; set; }
    public string AssemblerGroupName { get; set; }

    public DisplayDefinition[] Displays { get; set; }
}

public class DisplayDefinition
{
    public string[] DisplayNames { get; set; }
    public DisplayKind[] DisplayKinds { get; set; } = new[] { DisplayKind.Status };
    public int DisplayMaxLines { get; set; } = 19;
    public double DisplaySwitchTime { get; set; } = 5;
    public float FontSize { get; set; } = 0f;
    public bool AutoFitFontSize { get; set; } = false;
}

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

    // Seeded into empty Custom Data of tagged LCDs so users can discover the options in the UI.
    private const string DefaultCustomDataTemplate =
        "@BaR\n" +
        "# BaR display config for this LCD. Remove or comment a line to fall back to the script default.\n" +
        "# Kinds: comma-separated list of pages to cycle through.\n" +
        "#   Valid: ShortStatus, Status, WeldTargets, GrindTargets, CollectTargets, MissingItems, BlockWeldPriority, BlockGrindPriority\n" +
        "Kinds=Status,WeldTargets,GrindTargets,MissingItems\n" +
        "# MaxLines: line cap for list pages (positive integer).\n" +
        "MaxLines=19\n" +
        "# SwitchTime: seconds between page switches. 0 = no rotation.\n" +
        "SwitchTime=5\n" +
        "# FontSize: explicit font size (e.g. 1.2) or 'auto' to fit the surface.\n" +
        "#FontSize=auto\n" +
        "@/BaR\n" +
        "\n" +
        "# For cockpits with multiple surfaces you can add per-surface blocks like:\n" +
        "#@BaR@0\n" +
        "#Kinds=Status\n" +
        "#@/BaR\n" +
        "#@BaR@1\n" +
        "#Kinds=WeldTargets,MissingItems\n" +
        "#@/BaR\n";

    public BuildAndRepairAutoQueuing(Program program)
    {
        _Program = program;
    }

    public void SetInfoOnly(bool infoOnly)
    {
        _InfoOnly = infoOnly;
    }

    public void Handle()
    {
        _ElapsedTime += _Program.Runtime.TimeSinceLastRun.TotalSeconds;
        if (!_IsInit)
        {
            Initialize();
            _ReInit = _ElapsedTime + 30;
            _NextUpdateAssemblerQueues = _ElapsedTime - 1;
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

            if (_ElapsedTime > _ReInit) _IsInit = false;
        }
    }

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

        // Tagged panels win over explicit DisplayNames referencing the same surface.
        var taggedByGroup = ScanTaggedPanels();
        var taggedSurfaces = new HashSet<IMyTextSurface>();
        if (taggedByGroup != null)
        {
            for (var g = 0; g < taggedByGroup.Length; g++)
            {
                if (taggedByGroup[g] == null) continue;
                foreach (var tp in taggedByGroup[g]) if (tp.Surface != null) taggedSurfaces.Add(tp.Surface);
            }
        }

        var idx = 0;
        foreach (var queuingGroup in BuildAndRepairSystemQueuingGroups)
        {
            var displays = (queuingGroup != null && queuingGroup.Displays != null) ? queuingGroup.Displays : new DisplayDefinition[0];
            _GroupData[idx] = new BuildAndRepairSystemQueuingGroupData();
            _GroupData[idx].Settings = queuingGroup;
            _GroupData[idx].RepairSystems = InitHandler<RepairSystemHandler>(queuingGroup);
            if (_GroupData[idx].RepairSystems != null)
            {
                _GroupData[idx].RepairSystems.SetProgram(_Program);
            }
            _GroupData[idx].Assemblers = InitAssemblerList(queuingGroup);
            _GroupData[idx].DisplayEntries = new List<DisplayEntry>();
            var groupLabel = string.IsNullOrEmpty(queuingGroup != null ? queuingGroup.Name : null) ? "BaR Group " + idx : queuingGroup.Name;

            for (int d = 0; d < displays.Length; d++)
            {
                var displayDef = displays[d];
                var names = displayDef != null ? displayDef.DisplayNames : null;
                if (names == null || names.Length == 0)
                {
                    var empty = new StatusAndLogDisplay(_Program, groupLabel, null, null);
                    empty.Clear();
                    empty.UpdateDisplay();
                    _GroupData[idx].DisplayEntries.Add(new DisplayEntry { EffectiveSettings = displayDef, Display = empty });
                    continue;
                }

                foreach (var name in names)
                {
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var resolved = ResolveSurfaceByName(name);
                    if (resolved != null && taggedSurfaces.Contains(resolved)) continue;

                    var effective = BuildEffectiveDisplayDefinition(displayDef, name);
                    var statusDisplay = new StatusAndLogDisplay(_Program, groupLabel, new[] { name }, null);
                    ApplyDisplayDefinitionToStatusDisplay(statusDisplay, effective);
                    statusDisplay.Clear();
                    statusDisplay.UpdateDisplay();
                    _GroupData[idx].DisplayEntries.Add(new DisplayEntry { EffectiveSettings = effective, Display = statusDisplay });
                }
            }

            if (taggedByGroup != null && idx < taggedByGroup.Length && taggedByGroup[idx] != null)
            {
                var baseDef = (displays.Length > 0 && displays[0] != null) ? displays[0] : MakeDefaultDisplayDefinition();
                foreach (var tp in taggedByGroup[idx])
                {
                    if (tp == null || tp.Surface == null) continue;
                    var effective = BuildEffectiveDisplayDefinitionFromBlock(baseDef, tp.Block, tp.SurfaceIndex);
                    var statusDisplay = new StatusAndLogDisplay(_Program, groupLabel, tp.Surface);
                    ApplyDisplayDefinitionToStatusDisplay(statusDisplay, effective);
                    statusDisplay.Clear();
                    statusDisplay.UpdateDisplay();
                    _GroupData[idx].DisplayEntries.Add(new DisplayEntry { EffectiveSettings = effective, Display = statusDisplay });
                }
            }

            idx++;
        }

        _IsInit = true;
    }

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
                        handler.Init(entity, handler.Count > 0);
                    }
                }
            }
        }

        // Fallback: auto-detect BaR blocks by probing for known properties.
        if (handler == null || handler.Count == 0)
        {
            try
            {
                if (typeof(T) == typeof(RepairSystemHandler) && _Program != null && _Program.GridTerminalSystem != null)
                {
                    var auto = new RepairSystemHandler();
                    auto.Init(_Program.GridTerminalSystem, (IMyShipWelder blk) =>
                    {
                        try
                        {
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
                groupData.InfoOnly = _InfoOnly;
                groupData.RefreshDisplay(_ElapsedTime);
            }
        }
    }

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

    private DisplayDefinition BuildEffectiveDisplayDefinition(DisplayDefinition baseDef, string displayName)
    {
        if (_Program == null || _Program.GridTerminalSystem == null || string.IsNullOrWhiteSpace(displayName)) return baseDef;
        string blockName;
        int surfaceIdx;
        ParseDisplayName(displayName, out blockName, out surfaceIdx);
        var block = _Program.GridTerminalSystem.GetBlockWithName(blockName) as IMyTerminalBlock;
        return BuildEffectiveDisplayDefinitionFromBlock(baseDef, block, surfaceIdx);
    }

    // Applies unscoped @BaR first, then @BaR@<surfaceIdx> scoped block on top.
    private DisplayDefinition BuildEffectiveDisplayDefinitionFromBlock(DisplayDefinition baseDef, IMyTerminalBlock block, int surfaceIdx)
    {
        if (baseDef == null) baseDef = MakeDefaultDisplayDefinition();
        if (block == null) return baseDef;
        var customData = block.CustomData;
        if (string.IsNullOrEmpty(customData)) return baseDef;

        var blocks = ParseAllBaRBlocks(customData);
        if (blocks.Count == 0) return baseDef;

        var result = baseDef;
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].Key == -1) result = ApplyCustomDataBody(result, blocks[i].Value);
        }
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].Key == surfaceIdx) result = ApplyCustomDataBody(result, blocks[i].Value);
        }
        return result;
    }

    // Returns each @BaR block in Custom Data as (surfaceIdx or -1, body).
    private static List<KeyValuePair<int, string>> ParseAllBaRBlocks(string customData)
    {
        var result = new List<KeyValuePair<int, string>>();
        if (string.IsNullOrEmpty(customData)) return result;
        var pos = 0;
        while (pos < customData.Length)
        {
            var start = customData.IndexOf("@BaR", pos, StringComparison.Ordinal);
            if (start < 0) break;
            var after = start + 4;
            var surfaceIdx = -1;
            if (after < customData.Length && customData[after] == '@')
            {
                var numStart = after + 1;
                var numEnd = numStart;
                while (numEnd < customData.Length && customData[numEnd] >= '0' && customData[numEnd] <= '9') numEnd++;
                if (numEnd > numStart)
                {
                    int parsed;
                    if (int.TryParse(customData.Substring(numStart, numEnd - numStart), out parsed) && parsed >= 0)
                    {
                        surfaceIdx = parsed;
                        after = numEnd;
                    }
                }
            }
            var end = customData.IndexOf("@/BaR", after, StringComparison.Ordinal);
            if (end < 0) break;
            result.Add(new KeyValuePair<int, string>(surfaceIdx, customData.Substring(after, end - after)));
            pos = end + 5;
        }
        return result;
    }

    private static DisplayDefinition ApplyCustomDataBody(DisplayDefinition baseDef, string body)
    {
        var result = new DisplayDefinition
        {
            DisplayNames = baseDef.DisplayNames,
            DisplayKinds = baseDef.DisplayKinds,
            DisplayMaxLines = baseDef.DisplayMaxLines,
            DisplaySwitchTime = baseDef.DisplaySwitchTime,
            FontSize = baseDef.FontSize,
            AutoFitFontSize = baseDef.AutoFitFontSize
        };

        var lines = body.Split('\n');
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line.Substring(0, eq).Trim();
            var value = line.Substring(eq + 1).Trim();

            if (key.Equals("Kinds", StringComparison.OrdinalIgnoreCase) || key.Equals("DisplayKinds", StringComparison.OrdinalIgnoreCase))
            {
                var parts = value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                var kinds = new List<DisplayKind>();
                foreach (var p in parts)
                {
                    DisplayKind kind;
                    if (TryParseDisplayKind(p.Trim(), out kind)) kinds.Add(kind);
                }
                if (kinds.Count > 0) result.DisplayKinds = kinds.ToArray();
            }
            else if (key.Equals("MaxLines", StringComparison.OrdinalIgnoreCase) || key.Equals("DisplayMaxLines", StringComparison.OrdinalIgnoreCase))
            {
                int n;
                if (int.TryParse(value, out n) && n > 0) result.DisplayMaxLines = n;
            }
            else if (key.Equals("SwitchTime", StringComparison.OrdinalIgnoreCase) || key.Equals("DisplaySwitchTime", StringComparison.OrdinalIgnoreCase))
            {
                double d;
                if (double.TryParse(value, out d) && d >= 0) result.DisplaySwitchTime = d;
            }
            else if (key.Equals("FontSize", StringComparison.OrdinalIgnoreCase))
            {
                if (value.Equals("auto", StringComparison.OrdinalIgnoreCase) || value.Equals("fit", StringComparison.OrdinalIgnoreCase))
                {
                    result.AutoFitFontSize = true;
                    result.FontSize = 0f;
                }
                else
                {
                    float f;
                    if (float.TryParse(value, out f) && f > 0f)
                    {
                        result.FontSize = f;
                        result.AutoFitFontSize = false;
                    }
                }
            }
        }
        return result;
    }

    private static DisplayDefinition MakeDefaultDisplayDefinition()
    {
        return new DisplayDefinition
        {
            DisplayKinds = new[] { DisplayKind.Status },
            DisplayMaxLines = 19,
            DisplaySwitchTime = 5
        };
    }

    private static void ApplyDisplayDefinitionToStatusDisplay(StatusAndLogDisplay display, DisplayDefinition def)
    {
        if (display == null || def == null) return;
        display.OverrideFontSize = def.FontSize;
        display.AutoFitFontSize = def.AutoFitFontSize;
    }

    private IMyTextSurface ResolveSurfaceByName(string displayName)
    {
        if (_Program == null || _Program.GridTerminalSystem == null || string.IsNullOrWhiteSpace(displayName)) return null;
        string blockName;
        int surfaceIdx;
        ParseDisplayName(displayName, out blockName, out surfaceIdx);
        var block = _Program.GridTerminalSystem.GetBlockWithName(blockName);
        if (block == null) return null;
        var surface = block as IMyTextSurface;
        if (surface != null) return surface;
        var provider = block as IMyTextSurfaceProvider;
        if (provider != null && provider.SurfaceCount > surfaceIdx) return provider.GetSurface(surfaceIdx);
        return null;
    }

    private List<TaggedPanel>[] ScanTaggedPanels()
    {
        var result = new List<TaggedPanel>[BuildAndRepairSystemQueuingGroups.Length];
        if (_Program == null || _Program.GridTerminalSystem == null) return result;

        var blocks = new List<IMyTerminalBlock>();
        _Program.GridTerminalSystem.GetBlocks(blocks);
        foreach (var block in blocks)
        {
            if (block == null) continue;
            string groupId;
            int[] surfaceIndices;
            if (!TryParseBaRTag(block.CustomName, out groupId, out surfaceIndices)) continue;

            int groupIndex;
            if (!MatchGroupId(groupId, out groupIndex)) continue;

            if (string.IsNullOrWhiteSpace(block.CustomData))
            {
                try { block.CustomData = DefaultCustomDataTemplate; } catch { }
            }

            var ts = block as IMyTextSurface;
            var tsp = block as IMyTextSurfaceProvider;

            foreach (var surfaceIdx in surfaceIndices)
            {
                IMyTextSurface surface = null;
                if (ts != null)
                {
                    surface = ts;
                }
                else if (tsp != null)
                {
                    if (tsp.SurfaceCount > surfaceIdx) surface = tsp.GetSurface(surfaceIdx);
                }

                if (surface == null)
                {
                    InitializationResultMessage += string.Format("\nInfo: [BaR:{0}@{1}] on '{2}' could not be resolved to a text surface.", groupId, surfaceIdx, block.CustomName);
                    continue;
                }

                if (result[groupIndex] == null) result[groupIndex] = new List<TaggedPanel>();
                result[groupIndex].Add(new TaggedPanel { Block = block, Surface = surface, SurfaceIndex = surfaceIdx });
            }
        }
        return result;
    }

    private static bool TryParseBaRTag(string customName, out string groupId, out int[] surfaceIndices)
    {
        groupId = null;
        surfaceIndices = null;
        if (string.IsNullOrEmpty(customName)) return false;
        var start = customName.IndexOf("[BaR:", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return false;
        var end = customName.IndexOf(']', start);
        if (end < 0) return false;
        var inside = customName.Substring(start + 5, end - start - 5).Trim();
        if (inside.Length == 0) return false;

        var at = inside.LastIndexOf('@');
        if (at >= 0)
        {
            var idxPart = inside.Substring(at + 1).Trim();
            if (idxPart.Length == 0) return false;
            var parts = idxPart.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            var indices = new List<int>();
            foreach (var p in parts)
            {
                int parsed;
                if (!int.TryParse(p.Trim(), out parsed) || parsed < 0) return false;
                if (!indices.Contains(parsed)) indices.Add(parsed);
            }
            if (indices.Count == 0) return false;
            surfaceIndices = indices.ToArray();
            groupId = inside.Substring(0, at).Trim();
        }
        else
        {
            surfaceIndices = new[] { 0 };
            groupId = inside;
        }
        return groupId.Length > 0;
    }

    private static bool MatchGroupId(string groupId, out int groupIndex)
    {
        groupIndex = -1;
        if (string.IsNullOrEmpty(groupId) || BuildAndRepairSystemQueuingGroups == null) return false;

        int oneBased;
        if (int.TryParse(groupId, out oneBased))
        {
            var zero = oneBased - 1;
            if (zero >= 0 && zero < BuildAndRepairSystemQueuingGroups.Length)
            {
                groupIndex = zero;
                return true;
            }
            return false;
        }

        for (var i = 0; i < BuildAndRepairSystemQueuingGroups.Length; i++)
        {
            var g = BuildAndRepairSystemQueuingGroups[i];
            if (g != null && !string.IsNullOrEmpty(g.Name) && g.Name.Equals(groupId, StringComparison.OrdinalIgnoreCase))
            {
                groupIndex = i;
                return true;
            }
        }
        return false;
    }

    private class TaggedPanel
    {
        public IMyTerminalBlock Block;
        public IMyTextSurface Surface;
        public int SurfaceIndex;
    }

    private static void ParseDisplayName(string name, out string blockName, out int index)
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
                    return;
                }
            }
        }
        blockName = name;
    }

    private static bool TryParseDisplayKind(string s, out DisplayKind kind)
    {
        kind = DisplayKind.Status;
        if (string.IsNullOrEmpty(s)) return false;
        switch (s.ToLowerInvariant())
        {
            case "status": kind = DisplayKind.Status; return true;
            case "shortstatus": case "short": kind = DisplayKind.ShortStatus; return true;
            case "weldtargets": case "weld": kind = DisplayKind.WeldTargets; return true;
            case "grindtargets": case "grind": kind = DisplayKind.GrindTargets; return true;
            case "collecttargets": case "collect": kind = DisplayKind.CollectTargets; return true;
            case "missingitems": case "missing": kind = DisplayKind.MissingItems; return true;
            case "blockweldpriority": case "weldpriority": kind = DisplayKind.BlockWeldPriority; return true;
            case "blockgrindpriority": case "grindpriority": kind = DisplayKind.BlockGrindPriority; return true;
        }
        return false;
    }

    // Hook for user-supplied script-controlled grind logic. Empty by default.
    private void ScriptControlledGrinding()
    {
    }
}

public class DisplayEntry
{
    public DisplayDefinition EffectiveSettings;
    public StatusAndLogDisplay Display;
    public int DisplayKindIdx;
    public double NextSwitchTime;
}

public class BuildAndRepairSystemQueuingGroupData
{
    public BuildAndRepairSystemQueuingGroup Settings { get; set; }
    public RepairSystemHandler RepairSystems { get; set; }
    public List<long> Assemblers { get; set; }
    public List<DisplayEntry> DisplayEntries { get; set; }
    public bool InfoOnly { get; set; }

    public BuildAndRepairSystemQueuingGroupData()
    {
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

    public void RefreshDisplay(double elapsedTime)
    {
        if (DisplayEntries == null || DisplayEntries.Count == 0) return;
        for (var idx = 0; idx < DisplayEntries.Count; idx++)
        {
            var entry = DisplayEntries[idx];
            if (entry == null) continue;
            var display = entry.Display;
            var settings = entry.EffectiveSettings;
            if (display == null || settings == null) continue;

            display.Clear();
            if (settings.DisplayKinds == null || settings.DisplayKinds.Length == 0 || RepairSystems == null) continue;

            if (elapsedTime > entry.NextSwitchTime)
            {
                entry.DisplayKindIdx = (entry.DisplayKindIdx + 1) % settings.DisplayKinds.Length;
                entry.NextSwitchTime = elapsedTime + settings.DisplaySwitchTime;
            }
            switch (settings.DisplayKinds[entry.DisplayKindIdx])
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

    private void DisplayStatus(DisplayDefinition settings, StatusAndLogDisplay display)
    {
        DisplayShortStatus(settings, display);
        display.AddStatus(string.Format("Search mode       : {0}", RepairSystems.SearchMode));
        display.AddStatus(string.Format("Work mode         : {0}", RepairSystems.WorkMode));
        display.AddStatus(string.Format("Build projected   : {0}", RepairSystems.AllowBuild));
        display.AddStatus(string.Format("UseIgnoreColor    : {0}", RepairSystems.UseIgnoreColor));
        display.AddStatus(string.Format("Script Controlled : {0}", RepairSystems.ScriptControlled));
        display.AddStatus(string.Format("Auto-queuing      : {0}", AutoQueuingStateText()));
    }

    private string AutoQueuingStateText()
    {
        if (InfoOnly) return "Disabled (info-only)";
        if (Assemblers == null || Assemblers.Count == 0) return "Disabled (no assemblers)";
        return string.Format("Enabled ({0} assembler{1})", Assemblers.Count, Assemblers.Count == 1 ? "" : "s");
    }

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

public class RepairSystemHandler : EntityHandler<IMyShipWelder>
{
    private Program _Program;
    public void SetProgram(Program program)
    {
        _Program = program;
    }


    private Func<IEnumerable<long>, VRage.Game.MyDefinitionId, int, int> _EnsureQueued;
    private Func<IMyProjector, Dictionary<VRage.Game.MyDefinitionId, VRage.MyFixedPoint>, int> _NeededComponents4Blueprint;

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
        CommunicationBlock,
        Connector,
        MergeBlock
    }

    public enum ComponentClass
    {
        Material = 1,
        Ingot,
        Ore,
        Stone,
        Gravel
    }

    public enum SearchModes
    {
        Grids = 0x0001,
        BoundingBox = 0x0002
    }

    public enum WorkModes
    {
        WeldBeforeGrind = 0x0001,
        GrindBeforeWeld = 0x0002,
        // DEPRECATED: removed from the BaR UI in v2.5.4. Migrated to WeldBeforeGrind on the mod side.
        // Value preserved so existing programmable-block scripts that reference it still compile.
        // Do not reuse 0x0004.
        GrindIfWeldGetStuck = 0x0004,
        WeldOnly = 0x0008,
        GrindOnly = 0x0010
    }

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

    // Deprecated: no-op, kept for backward compatibility.
    public bool HelpOther
    {
        get { return false; }
        set { }
    }

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

    // 0 = WeldFull, 1 = WeldFunctional, 2 = WeldSkeleton
    public long WeldMode
    {
        get
        {
            return _Entities.Count > 0 ? _Entities[0].GetValue<long>("BuildAndRepair.WeldMode") : 0;
        }
        set
        {
            foreach (var entity in _Entities) entity.SetValue<long>("BuildAndRepair.WeldMode", value);
        }
    }


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
                if (values.Length >= 2 &&
                   Enum.TryParse<BlockClass>(values[0], out blockClass) &&
                   bool.TryParse(values[1], out enabled))
                {
                    blockList.Add(new ClassState<BlockClass>(blockClass, enabled));
                }
            }
            return blockList;
        }
        return null;
    }

    public int GetWeldPriority(BlockClass blockClass)
    {
        if (_Entities.Count > 0)
        {
            var getPriority = GetValue<Func<int, int>>("BuildAndRepair.GetWeldPriority");
            return getPriority((int)blockClass);
        }
        else return int.MaxValue;
    }

    public void SetWeldPriority(BlockClass blockClass, int prio)
    {
        foreach (var entity in _Entities)
        {
            var setPriority = entity.GetValue<Action<int, int>>("BuildAndRepair.SetWeldPriority");
            setPriority((int)blockClass, prio);
        }
    }

    public bool GetWeldEnabled(BlockClass blockClass)
    {
        if (_Entities.Count > 0)
        {
            var getEnabled = GetValue<Func<int, bool>>("BuildAndRepair.GetWeldEnabled");
            return getEnabled((int)blockClass);
        }
        else return false;
    }

    public void SetWeldEnabled(BlockClass blockClass, bool enabled)
    {
        foreach (var entity in _Entities)
        {
            var setEnabled = entity.GetValue<Action<int, bool>>("BuildAndRepair.SetWeldEnabled");
            setEnabled((int)blockClass, enabled);
        }
    }

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
                if (values.Length >= 2 &&
                   Enum.TryParse<BlockClass>(values[0], out blockClass) &&
                   bool.TryParse(values[1], out enabled))
                {
                    blockList.Add(new ClassState<BlockClass>(blockClass, enabled));
                }
            }
            return blockList;
        }
        return null;
    }

    public int GetGrindPriority(BlockClass blockClass)
    {
        if (_Entities.Count > 0)
        {
            var getPriority = GetValue<Func<int, int>>("BuildAndRepair.GetGrindPriority");
            return getPriority((int)blockClass);
        }
        else return int.MaxValue;
    }

    public void SetGrindPriority(BlockClass blockClass, int prio)
    {
        foreach (var entity in _Entities)
        {
            var setPriority = entity.GetValue<Action<int, int>>("BuildAndRepair.SetGrindPriority");
            setPriority((int)blockClass, prio);
        }
    }

    public bool GetGrindEnabled(BlockClass blockClass)
    {
        if (_Entities.Count > 0)
        {
            var getEnabled = GetValue<Func<int, bool>>("BuildAndRepair.GetGrindEnabled");
            return getEnabled((int)blockClass);
        }
        else return false;
    }

    public void SetGrindEnabled(BlockClass blockClass, bool enabled)
    {
        foreach (var entity in _Entities)
        {
            var setEnabled = entity.GetValue<Action<int, bool>>("BuildAndRepair.SetGrindEnabled");
            setEnabled((int)blockClass, enabled);
        }
    }

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
                if (values.Length >= 2 &&
                   Enum.TryParse<ComponentClass>(values[0], out compClass) &&
                   bool.TryParse(values[1], out enabled))
                {
                    compList.Add(new ClassState<ComponentClass>(compClass, enabled));
                }
            }
            return compList;
        }
        return null;
    }

    public int GetCollectPriority(ComponentClass compClass)
    {
        if (_Entities.Count > 0)
        {
            var getPriority = GetValue<Func<int, int>>("BuildAndRepair.GetCollectPriority");
            return getPriority((int)compClass);
        }
        else return int.MaxValue;
    }

    public void SetCollectPriority(ComponentClass compClass, int prio)
    {
        foreach (var entity in _Entities)
        {
            var setPriority = entity.GetValue<Action<int, int>>("BuildAndRepair.SetCollectPriority");
            setPriority((int)compClass, prio);
        }
    }

    public bool GetCollectEnabled(ComponentClass compClass)
    {
        if (_Entities.Count > 0)
        {
            var getEnabled = GetValue<Func<int, bool>>("BuildAndRepair.GetCollectEnabled");
            return getEnabled((int)compClass);
        }
        else return false;
    }

    public void SetCollectEnabled(ComponentClass compClass, bool enabled)
    {
        foreach (var entity in _Entities)
        {
            var setEnabled = entity.GetValue<Action<int, bool>>("BuildAndRepair.SetCollectEnabled");
            setEnabled((int)compClass, enabled);
        }
    }

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

    public IMySlimBlock CurrentTarget
    {
        get
        {
            return _Entities.Count > 0 ? GetValue<IMySlimBlock>("BuildAndRepair.CurrentTarget") : null;
        }
    }

    public IMySlimBlock CurrentGrindTarget
    {
        get
        {
            return _Entities.Count > 0 ? GetValue<IMySlimBlock>("BuildAndRepair.CurrentGrindTarget") : null;
        }
    }

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

    public MemorySafeDictionary<VRage.Game.MyDefinitionId, int> MissingComponents()
    {
        var missingItems = new MemorySafeDictionary<VRage.Game.MyDefinitionId, int>();
        foreach (var entity in _Entities)
        {
            var dict = entity.GetValue<MemorySafeDictionary<VRage.Game.MyDefinitionId, int>>("BuildAndRepair.MissingComponents");
            // Take max across overlapping systems, don't sum (they report the same items).
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
            try { return _Entities[0].GetValue<T>(propertyName); }
            catch (Exception) { }
        }
        return default(T);
    }

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

    public MemorySafeList<IMySlimBlock> PossibleGrindTargets()
    {
        if (_Entities.Count > 0)
        {
            return GetValue<MemorySafeList<IMySlimBlock>>("BuildAndRepair.PossibleGrindTargets");
        }
        return null;
    }

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

    public MemorySafeList<IMyEntity> PossibleCollectTargets()
    {
        if (_Entities.Count > 0)
        {
            return GetValue<MemorySafeList<IMyEntity>>("BuildAndRepair.PossibleCollectTargets");
        }
        return null;
    }

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

    public int CountOfWorking
    {
        get
        {
            var res = 0;
            foreach (var entity in _Entities) if (entity.IsWorking && entity.IsFunctional) res++;
            return res;
        }
    }

    protected override int GetCount()
    {
        return _Entities.Count;
    }

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

    public override void Init(VRage.Game.ModAPI.Ingame.IMyEntity newEntity, bool add = false)
    {
        if (!add) { _Entities.Clear(); AreEnabled = false; }
        var entity = newEntity as T;
        if (AddEntity(entity))
        {
            CheckEnabled();
        }
    }

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

    public int MaxLogLines { get; set; }
    public bool ShowHeader { get; set; }
    public float OverrideFontSize { get; set; }
    public bool AutoFitFontSize { get; set; }

    private readonly HashSet<IMyTextSurface> _FontApplied = new HashSet<IMyTextSurface>();

    public StatusAndLogDisplay(MyGridProgram caller, string name, string[] lcdStatusPanels, string[] lcdLogPanels)
    {
        ShowHeader = true;
        _Program = caller;
        _AIName = name;
        _LcdStatusPanels = lcdStatusPanels;
        _LcdLogPanels = lcdLogPanels;
        MaxLogLines = 20;
        ReloadDisplays();
    }

    // Direct-surface ctor for tagged LCDs; no name resolution needed.
    public StatusAndLogDisplay(MyGridProgram caller, string name, IMyTextSurface surface)
    {
        ShowHeader = true;
        _Program = caller;
        _AIName = name;
        _LcdStatusPanels = null;
        _LcdLogPanels = null;
        MaxLogLines = 20;
        if (surface != null) _StatusPanels.Add(surface);
    }

    public string ReloadDisplays()
    {
        var res = FindPanels(_Program, _LcdStatusPanels, _StatusPanels);
        res += FindPanels(_Program, _LcdLogPanels, _LogPanels);
        return res;
    }

    public void CyclicReloadDisplays()
    {
        _RefreshDelay--;
        if (_RefreshDelay > 0) return;
        try { ReloadDisplays(); }
        catch { }
        _RefreshDelay = 20;
    }

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

    public void Clear()
    {
        _StatusText = "";
        _ErrorText = "";
    }

    internal void AddStatus(string line)
    {
        _StatusText += line + "\n";
    }

    internal void AddError(string line)
    {
        _ErrorText = line + "\n";
    }

    public void UpdateDisplay()
    {
        var text = string.Empty;
        if (ShowHeader) text = (_AIName ?? "") + " (" + DateTime.Now + "):\n";
        if (!string.IsNullOrEmpty(_ErrorText)) text += _ErrorText;
        if (!string.IsNullOrEmpty(_StatusText)) text += _StatusText;

        foreach (var panel in _StatusPanels)
        {
            if (panel == null) continue;
            EnsureFontApplied(panel, text);
            SetPanelText(panel, text);
        }
        if (_Program != null) _Program.Echo(!string.IsNullOrEmpty(_ErrorText) ? _ErrorText : text);

        var logText = !string.IsNullOrEmpty(_AIName) ? _AIName + _LogText : _LogText;
        foreach (var panel in _LogPanels)
        {
            if (panel == null) continue;
            EnsureFontApplied(panel, logText);
            SetPanelText(panel, logText);
        }
    }

    // Applies font size once per panel. Auto-fit waits for non-empty text.
    private void EnsureFontApplied(IMyTextSurface panel, string text)
    {
        if (_FontApplied.Contains(panel)) return;
        if (OverrideFontSize <= 0f && !AutoFitFontSize)
        {
            _FontApplied.Add(panel);
            return;
        }
        if (OverrideFontSize > 0f)
        {
            try { panel.FontSize = OverrideFontSize; } catch { }
            _FontApplied.Add(panel);
            return;
        }
        if (string.IsNullOrEmpty(text) || text.IndexOf('\n') < 0) return;
        try
        {
            panel.ContentType = ContentType.TEXT_AND_IMAGE;
            if (panel.Font != "Monospace") panel.Font = "Monospace";
            var sb = new StringBuilder(text);
            var measured = panel.MeasureStringInPixels(sb, panel.Font, 1f);
            var surf = panel.SurfaceSize;
            var padPct = panel.TextPadding / 100f;
            var availW = surf.X * (1f - padPct * 2f);
            var availH = surf.Y * (1f - padPct * 2f);
            if (measured.X > 0 && measured.Y > 0 && availW > 0 && availH > 0)
            {
                var scale = Math.Min(availW / measured.X, availH / measured.Y) * 0.98f;
                if (scale < 0.1f) scale = 0.1f;
                if (scale > 10f) scale = 10f;
                panel.FontSize = scale;
            }
            _FontApplied.Add(panel);
        }
        catch { _FontApplied.Add(panel); }
    }

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

    // Forces Monospace so column-aligned status lines render correctly.
    public static void SetPanelText(IMyTextSurface panel, string text)
    {
        panel.ContentType = ContentType.TEXT_AND_IMAGE;
        if (panel.Font != "Monospace") panel.Font = "Monospace";
        panel.WriteText(text, false);
    }

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

        var cubeGrid = block as IMyCubeGrid;
        if (cubeGrid != null) return cubeGrid.DisplayName;

        var entity = block as IMyEntity;
        if (entity != null)
        {
            return string.Format("{0} ({1})", entity.DisplayName, entity.EntityId);
        }

        return block != null ? block.ToString() : "NULL";
    }
}
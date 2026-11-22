using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NSubstitute;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using VRage.Game;
using VRage.Game.ModAPI.Ingame;
using VRage.ObjectBuilders;
using VRage.Scripting.MemorySafeTypes;
using VRageMath;
using IMyCubeBlock = VRage.Game.ModAPI.Ingame.IMyCubeBlock;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    /// <summary>
    /// Compiles the companion PB script like the game does (C# 6, wrapped in a MyGridProgram, default PB usings)
    /// and runs it against fake blocks: the program is created uninitialised, Runtime / GridTerminalSystem / Me /
    /// Storage / Echo are set through IMyGridProgram, then the constructor runs — the game's own order.
    /// The script keeps static state (its group config), so behaviour tests share one compiled assembly and run
    /// in one test class (sequential).
    /// </summary>
    internal static class CompanionScriptHost
    {
        private const string PbUsings =
            "using System; using System.Collections; using System.Collections.Generic; using System.Linq; using System.Text;\n" +
            "using Sandbox.ModAPI.Ingame; using Sandbox.ModAPI.Interfaces; using SpaceEngineers.Game.ModAPI.Ingame;\n" +
            "using VRage; using VRage.Collections; using VRage.Game; using VRage.Game.Components; using VRage.Game.GUI.TextPanel;\n" +
            "using VRage.Game.ModAPI.Ingame; using VRage.Game.ModAPI.Ingame.Utilities; using VRage.Game.ObjectBuilders.Definitions;\n" +
            "using VRageMath; using Sandbox.Game.EntityComponents; using VRage.Scripting.MemorySafeTypes;\n";

        private static readonly string[] SeAssemblies =
        {
            "Sandbox.Common.dll", "Sandbox.Game.dll", "SpaceEngineers.Game.dll", "SpaceEngineers.ObjectBuilders.dll",
            "VRage.dll", "VRage.Game.dll", "VRage.Library.dll", "VRage.Math.dll", "VRage.Scripting.dll"
        };

        public static string ScriptPath { get { return Path.Combine(TestEnvironment.RepoRoot, "SKO-Nanobot-BuildAndRepair-System-Script", "Script.cs"); } }

        public static CSharpCompilation Compile()
        {
            var script = File.ReadAllText(ScriptPath);
            var source = PbUsings + "public sealed class Program : MyGridProgram {\n" + script + "\n}\n";
            var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp6));
            var frameworkDir = RuntimeEnvironment.GetRuntimeDirectory();
            var references = SeAssemblies.Select(a => Path.Combine(TestEnvironment.SpaceEngineersBin, a))
                .Concat(new[] { "mscorlib.dll", "System.dll", "System.Core.dll" }.Select(a => Path.Combine(frameworkDir, a)))
                .Concat(new[] { "netstandard.dll", "System.Runtime.dll" }.Select(a => Path.Combine(frameworkDir, a)).Where(File.Exists))
                .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
                .ToList();
            return CSharpCompilation.Create("CompanionScript", new[] { tree }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }

        private static readonly Lazy<Type> _programType = new Lazy<Type>(() =>
        {
            using (var ms = new MemoryStream())
            {
                var emit = Compile().Emit(ms);
                if (!emit.Success)
                    throw new InvalidOperationException("Script does not compile: " +
                        string.Join(Environment.NewLine, emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(10)));
                return Assembly.Load(ms.ToArray()).GetType("Program", true);
            }
        });

        private static object _defaultGroups;

        public static Type ProgramType { get { return _programType.Value; } }

        public static Type Nested(string name) { return ProgramType.GetNestedType(name, BindingFlags.Public | BindingFlags.NonPublic); }

        private static FieldInfo GroupsField { get { return ProgramType.GetField("BuildAndRepairSystemQueuingGroups", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public); } }

        /// <summary>Restores the script's shipped group config (statics survive between tests).</summary>
        public static void ResetConfig()
        {
            if (_defaultGroups == null) _defaultGroups = GroupsField.GetValue(null);
            GroupsField.SetValue(null, _defaultGroups);
        }

        /// <summary>Replaces the script's group config: one queuing group per spec.</summary>
        public static void SetGroups(params GroupSpec[] groups)
        {
            if (_defaultGroups == null) _defaultGroups = GroupsField.GetValue(null);
            var groupType = Nested("BuildAndRepairSystemQueuingGroup");
            var displayType = Nested("DisplayDefinition");
            var kindType = Nested("DisplayKind");
            var array = Array.CreateInstance(groupType, groups.Length);
            for (int i = 0; i < groups.Length; i++)
            {
                var g = groups[i];
                var group = Activator.CreateInstance(groupType);
                groupType.GetProperty("Name").SetValue(group, g.Name);
                groupType.GetProperty("BuildAndRepairSystemGroupName").SetValue(group, g.BarGroup);
                groupType.GetProperty("BuildAndRepairSystemNames").SetValue(group, g.BarNames);
                groupType.GetProperty("AssemblerGroupName").SetValue(group, g.AssemblerGroup);
                groupType.GetProperty("AssemblerNames").SetValue(group, g.AssemblerNames);
                var displays = Array.CreateInstance(displayType, g.Displays.Length);
                for (int d = 0; d < g.Displays.Length; d++)
                {
                    var spec = g.Displays[d];
                    var def = Activator.CreateInstance(displayType);
                    displayType.GetProperty("DisplayNames").SetValue(def, spec.Names);
                    var kinds = Array.CreateInstance(kindType, spec.Kinds.Length);
                    for (int k = 0; k < spec.Kinds.Length; k++) kinds.SetValue(Enum.Parse(kindType, spec.Kinds[k]), k);
                    displayType.GetProperty("DisplayKinds").SetValue(def, kinds);
                    displayType.GetProperty("DisplayMaxLines").SetValue(def, spec.MaxLines);
                    displayType.GetProperty("DisplaySwitchTime").SetValue(def, spec.SwitchTime);
                    displays.SetValue(def, d);
                }
                groupType.GetProperty("Displays").SetValue(group, displays);
                array.SetValue(group, i);
            }
            GroupsField.SetValue(null, array);
        }
    }

    internal sealed class GroupSpec
    {
        public string Name;
        public string BarGroup;
        public string[] BarNames;
        public string AssemblerGroup;
        public string[] AssemblerNames;
        public DisplaySpec[] Displays = new DisplaySpec[0];
    }

    internal sealed class DisplaySpec
    {
        public string[] Names;
        public string[] Kinds = { "Status" };
        public int MaxLines = 19;
        public double SwitchTime = 5;
    }

    /// <summary>One running instance of the script plus its fake world.</summary>
    internal sealed class ScriptRun
    {
        public readonly FakeTerminalSystem Gts = new FakeTerminalSystem();
        public readonly List<string> Echoes = new List<string>();
        public readonly IMyGridProgramRuntimeInfo Runtime = Substitute.For<IMyGridProgramRuntimeInfo>();
        public double SecondsPerRun = 1.6667; // Update100
        private object _program;

        public ScriptRun()
        {
            Runtime.TimeSinceLastRun.Returns(_ => TimeSpan.FromSeconds(SecondsPerRun));
        }

        public string Storage
        {
            get { return _program != null ? ((Sandbox.ModAPI.IMyGridProgram)_program).Storage : null; }
        }

        public string EchoText { get { return string.Join("\n", Echoes); } }

        /// <summary>Creates the program the way the PB does (storage = what the PB saved before).</summary>
        public ScriptRun Start(string storage = "")
        {
            var type = CompanionScriptHost.ProgramType;
            _program = FormatterServices.GetUninitializedObject(type);
            var gp = (Sandbox.ModAPI.IMyGridProgram)_program;
            gp.Runtime = Runtime;
            gp.GridTerminalSystem = Gts;
            gp.Me = Substitute.For<IMyProgrammableBlock>();
            gp.Storage = storage;
            gp.Echo = s => Echoes.Add(s);
            type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null).Invoke(_program, null);
            return this;
        }

        public ScriptRun Run(string argument = "", int times = 1)
        {
            for (int i = 0; i < times; i++)
                ((Sandbox.ModAPI.IMyGridProgram)_program).Main(argument, UpdateType.Update100);
            return this;
        }
    }

    internal sealed class FakeBlockGroup : IMyBlockGroup
    {
        public readonly List<IMyTerminalBlock> Blocks = new List<IMyTerminalBlock>();
        public FakeBlockGroup(string name) { Name = name; }
        public string Name { get; private set; }

        public void GetBlocks(List<IMyTerminalBlock> blocks, Func<IMyTerminalBlock, bool> collect = null)
        {
            blocks?.Clear();
            foreach (var b in Blocks) if (collect == null || collect(b)) blocks?.Add(b);
        }

        public void GetBlocksOfType<T>(List<IMyTerminalBlock> blocks, Func<IMyTerminalBlock, bool> collect = null) where T : class
        {
            blocks?.Clear();
            foreach (var b in Blocks) if (b is T && (collect == null || collect(b))) blocks?.Add(b);
        }

        public void GetBlocksOfType<T>(List<T> blocks, Func<T, bool> collect = null) where T : class
        {
            blocks?.Clear();
            foreach (var b in Blocks) { var t = b as T; if (t != null && (collect == null || collect(t))) blocks?.Add(t); }
        }
    }

    internal sealed class FakeTerminalSystem : IMyGridTerminalSystem
    {
        public readonly List<IMyTerminalBlock> Blocks = new List<IMyTerminalBlock>();
        public readonly List<FakeBlockGroup> Groups = new List<FakeBlockGroup>();

        public T Add<T>(T block) where T : class, IMyTerminalBlock { Blocks.Add(block); return block; }

        public FakeBlockGroup Group(string name, params IMyTerminalBlock[] blocks)
        {
            var g = new FakeBlockGroup(name);
            g.Blocks.AddRange(blocks);
            Groups.Add(g);
            return g;
        }

        public void GetBlocks(List<IMyTerminalBlock> blocks) { blocks.Clear(); blocks.AddRange(Blocks); }
        public void GetBlockGroups(List<IMyBlockGroup> blockGroups, Func<IMyBlockGroup, bool> collect = null)
        {
            blockGroups.Clear();
            foreach (var g in Groups) if (collect == null || collect(g)) blockGroups.Add(g);
        }
        public void GetBlocksOfType<T>(List<IMyTerminalBlock> blocks, Func<IMyTerminalBlock, bool> collect = null) where T : class
        {
            blocks.Clear();
            foreach (var b in Blocks) if (b is T && (collect == null || collect(b))) blocks.Add(b);
        }
        public void GetBlocksOfType<T>(List<T> blocks, Func<T, bool> collect = null) where T : class
        {
            blocks.Clear();
            foreach (var b in Blocks) { var t = b as T; if (t != null && (collect == null || collect(t))) blocks.Add(t); }
        }
        public void SearchBlocksOfName(string name, List<IMyTerminalBlock> blocks, Func<IMyTerminalBlock, bool> collect = null)
        {
            blocks.Clear();
            foreach (var b in Blocks) if (b.CustomName != null && b.CustomName.Contains(name) && (collect == null || collect(b))) blocks.Add(b);
        }
        public IMyTerminalBlock GetBlockWithName(string name) { return Blocks.FirstOrDefault(b => b.CustomName == name); }
        public IMyBlockGroup GetBlockGroupWithName(string name) { return Groups.FirstOrDefault(g => g.Name == name); }
        public IMyTerminalBlock GetBlockWithId(long id) { return Blocks.FirstOrDefault(b => b.EntityId == id); }
        public bool CanAccess(IMyTerminalBlock block, MyTerminalAccessScope scope = MyTerminalAccessScope.All) { return true; }
        public bool CanAccess(IMyCubeGrid grid, MyTerminalAccessScope scope = MyTerminalAccessScope.All) { return true; }
    }

    /// <summary>Factory for fake blocks; BaRs expose their BuildAndRepair.* terminal properties.</summary>
    internal static class FakeBlocks
    {
        private static long _nextId = 1000;

        public static MyDefinitionId Component(string subtype) { return new MyDefinitionId(typeof(MyObjectBuilder_Component), subtype); }

        private static void Common(IMyTerminalBlock block, string name, string typeName, string subtype)
        {
            var id = ++_nextId;
            block.EntityId.Returns(id);
            block.CustomName.Returns(name);
            block.IsWorking.Returns(true);
            block.IsFunctional.Returns(true);
            block.BlockDefinition.Returns(new SerializableDefinitionId(typeof(MyObjectBuilder_CubeBlock), subtype)); // the script only reads the subtype
            var customData = "";
            block.CustomData.Returns(_ => customData);
            block.When(b => b.CustomData = Arg.Any<string>()).Do(ci => customData = ci.Arg<string>());
        }

        public static void Property<T>(IMyTerminalBlock block, string id, Func<T> getter)
        {
            var property = Substitute.For<ITerminalProperty<T>>();
            property.Id.Returns(id);
            property.GetValue(Arg.Any<IMyCubeBlock>()).Returns(_ => getter());
            block.GetProperty(id).Returns(property);
        }

        /// <summary>A BaR (or, with another subtype, a plain welder carrying the mod's properties).</summary>
        public static FakeBar Bar(string name, string subtype = "SELtdLargeNanobotBuildAndRepairSystem")
        {
            var block = Substitute.For<IMyShipWelder>();
            Common(block, name, "MyObjectBuilder_ShipWelder", subtype);
            return new FakeBar(block);
        }

        public static IMyAssembler Assembler(string name)
        {
            var block = Substitute.For<IMyAssembler>();
            Common(block, name, "MyObjectBuilder_Assembler", "LargeAssembler");
            block.Mode.Returns(Sandbox.ModAPI.Ingame.MyAssemblerMode.Assembly);
            block.OutputInventory.Returns(Substitute.For<IMyInventory>());
            return block;
        }

        public static FakePanel Lcd(string name)
        {
            var block = Substitute.For<IMyTextPanel>();
            Common(block, name, "MyObjectBuilder_TextPanel", "LargeLCDPanel");
            return new FakePanel(block, block);
        }

        /// <summary>A cockpit with <paramref name="surfaces"/> text surfaces.</summary>
        public static FakeCockpit Cockpit(string name, int surfaces)
        {
            var block = Substitute.For<IMyCockpit>();
            Common(block, name, "MyObjectBuilder_Cockpit", "LargeBlockCockpit");
            block.SurfaceCount.Returns(surfaces);
            var cockpit = new FakeCockpit(block);
            for (int i = 0; i < surfaces; i++)
            {
                var surface = Substitute.For<IMyTextSurface>();
                var panel = new FakePanel(null, surface);
                cockpit.Surfaces.Add(panel);
                block.GetSurface(i).Returns(surface);
            }
            return cockpit;
        }
    }

    internal sealed class FakeBar
    {
        public readonly IMyShipWelder Block;
        public readonly MemorySafeDictionary<MyDefinitionId, int> Missing = new MemorySafeDictionary<MyDefinitionId, int>();
        public readonly List<Tuple<long[], MyDefinitionId, int>> Queued = new List<Tuple<long[], MyDefinitionId, int>>();
        public bool ScriptControlled;
        public long Mode = 1;
        public long WorkMode = 1;

        public FakeBar(IMyShipWelder block)
        {
            Block = block;
            FakeBlocks.Property<MemorySafeDictionary<MyDefinitionId, int>>(block, "BuildAndRepair.MissingComponents", () => Missing);
            FakeBlocks.Property<bool>(block, "BuildAndRepair.ScriptControlled", () => ScriptControlled);
            FakeBlocks.Property<long>(block, "BuildAndRepair.Mode", () => Mode);
            FakeBlocks.Property<long>(block, "BuildAndRepair.WorkMode", () => WorkMode);
            FakeBlocks.Property<bool>(block, "BuildAndRepair.AllowBuild", () => true);
            // Every bool the status pages read directly (the mod always registers them in game).
            foreach (var id in new[] { "UseIgnoreColor", "UseGrindColor", "GrindJanitorEnemies", "GrindJanitorNotOwned", "GrindJanitorNeutrals",
                                       "GrindJanitorOptionDisableOnly", "GrindJanitorOptionHackOnly", "CollectIfIdle", "PushIngotOreImmediately" })
                FakeBlocks.Property<bool>(block, "BuildAndRepair." + id, () => false);
            FakeBlocks.Property<MemorySafeList<IMySlimBlock>>(block, "BuildAndRepair.PossibleTargets", () => new MemorySafeList<IMySlimBlock>());
            FakeBlocks.Property<MemorySafeList<IMySlimBlock>>(block, "BuildAndRepair.PossibleGrindTargets", () => new MemorySafeList<IMySlimBlock>());
            FakeBlocks.Property<MemorySafeList<VRage.Game.ModAPI.Ingame.IMyEntity>>(block, "BuildAndRepair.PossibleCollectTargets", () => new MemorySafeList<VRage.Game.ModAPI.Ingame.IMyEntity>());
            FakeBlocks.Property<Func<IEnumerable<long>, MyDefinitionId, int, int>>(block, "BuildAndRepair.ProductionBlock.EnsureQueued",
                () => (ids, item, amount) => { Queued.Add(Tuple.Create(ids.ToArray(), item, amount)); return amount; });
        }

        public FakeBar Needs(string subtype, int amount)
        {
            Missing[FakeBlocks.Component(subtype)] = amount;
            return this;
        }

        public int QueuedAmount(string subtype)
        {
            var last = Queued.LastOrDefault(q => q.Item2.SubtypeName == subtype);
            return last != null ? last.Item3 : 0;
        }
    }

    /// <summary>A text surface (LCD, or one cockpit screen) that remembers what was written and its font size.</summary>
    internal sealed class FakePanel
    {
        public readonly IMyTextPanel Block;
        public readonly IMyTextSurface Surface;
        public string Text = "";
        public int Writes;
        public float FontSize = 1f;

        public FakePanel(IMyTextPanel block, IMyTextSurface surface)
        {
            Block = block;
            Surface = surface;
            surface.When(s => s.WriteText(Arg.Any<string>(), Arg.Any<bool>())).Do(ci =>
            {
                Text = ci.ArgAt<bool>(1) ? Text + ci.ArgAt<string>(0) : ci.ArgAt<string>(0);
                Writes++;
            });
            surface.FontSize.Returns(_ => FontSize);
            surface.When(s => s.FontSize = Arg.Any<float>()).Do(ci => FontSize = ci.Arg<float>());
            surface.Font.Returns("Monospace");
        }
    }

    internal sealed class FakeCockpit
    {
        public readonly IMyCockpit Block;
        public readonly List<FakePanel> Surfaces = new List<FakePanel>();
        public FakeCockpit(IMyCockpit block) { Block = block; }
    }
}

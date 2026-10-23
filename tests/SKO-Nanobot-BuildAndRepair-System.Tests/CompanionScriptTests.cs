using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-API-001 — the companion PB script has no build of its own; compile it like the game does
    // (C# 6, wrapped in a MyGridProgram, default PB usings) so script edits can't ship broken.
    // Compile check only: the in-game whitelist is not enforced here.
    public class CompanionScriptTests
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

        private static string ScriptPath { get { return Path.Combine(TestEnvironment.RepoRoot, "SKO-Nanobot-BuildAndRepair-System-Script", "Script.cs"); } }

        [Fact]
        public void Script_CompilesAsAProgrammableBlockProgram()
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

            var compilation = CSharpCompilation.Create("CompanionScript", new[] { tree }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

            Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors.Take(20).Select(e => e.ToString())));
        }

        [Fact]
        public void Script_UsesNoProhibitedNamespaces()
        {
            var script = File.ReadAllText(ScriptPath);
            Assert.DoesNotContain("System.Threading", script);
            Assert.DoesNotContain("System.Reflection", script);
            Assert.DoesNotContain("System.IO", script);
        }
    }
}

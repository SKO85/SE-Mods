using Microsoft.CodeAnalysis;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-API-001 — the companion PB script has no build of its own; compile it like the game does
    // (C# 6, wrapped in a MyGridProgram, default PB usings) so script edits can't ship broken.
    // Compile check only: the in-game whitelist is not enforced here.
    public class CompanionScriptTests
    {
        [Fact]
        public void Script_CompilesAsAProgrammableBlockProgram()
        {
            var errors = CompanionScriptHost.Compile().GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors.Take(20).Select(e => e.ToString())));
        }

        [Fact]
        public void Script_UsesNoProhibitedNamespaces()
        {
            var script = File.ReadAllText(CompanionScriptHost.ScriptPath);
            Assert.DoesNotContain("System.Threading", script);
            Assert.DoesNotContain("System.Reflection", script);
            Assert.DoesNotContain("System.IO", script);
        }
    }
}

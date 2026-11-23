using NSubstitute;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Chat.Commands;
using SKONanobotBuildAndRepairSystem.Models;
using System;
using System.Collections.Generic;
using System.Reflection;
using VRage.Game.ModAPI;
using Xunit;
using IMyShipWelder = Sandbox.ModAPI.IMyShipWelder;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-CHAT-003 — admin commands that change BaRs server-wide: /nanobars systems enable|disable all | --grid <name> |
    // --owner <name> (case-insensitive partial match, names with spaces, only changed blocks counted, clear errors) and
    // /nanobars sim <0.1-1.0|reset> (invariant number parsing, bounds).
    public class SystemsCommandTests : ModTestBase
    {
        private const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;
        private readonly List<IMyShipWelder> _welders = new List<IMyShipWelder>();

        public SystemsCommandTests()
        {
            Bar(1, "Mining Ship Alpha", 77, true);
            Bar(2, "Mining Ship Beta", 77, false);
            Bar(3, "Home Base", 88, true);
            Bar(4, "Derelict", 0, true);

            var identities = new List<IMyIdentity> { Identity(77, "Sead"), Identity(88, "Other Player") };
            var players = Substitute.For<IMyPlayerCollection>();
            players.When(p => p.GetAllIdentites(Arg.Any<List<IMyIdentity>>(), Arg.Any<Func<IMyIdentity, bool>>()))
                .Do(ci => ci.Arg<List<IMyIdentity>>().AddRange(identities));
            MyAPIGateway.Players = players; // no one online: names come from the identity list
        }

        private static IMyIdentity Identity(long id, string name)
        {
            var i = Substitute.For<IMyIdentity>();
            i.IdentityId.Returns(id);
            i.DisplayName.Returns(name);
            return i;
        }

        private void Bar(long id, string gridName, long owner, bool enabled)
        {
            var system = new NanobotSystem();
            typeof(NanobotSystem).GetField("_Settings", Inst).SetValue(system, new SyncBlockSettings(system));
            var grid = Substitute.For<IMyCubeGrid>();
            grid.DisplayName.Returns(gridName);
            var welder = Substitute.For<IMyShipWelder>();
            welder.EntityId.Returns(id);
            welder.CubeGrid.Returns(grid);
            welder.OwnerId.Returns(owner);
            var state = enabled;
            welder.Enabled.Returns(_ => state);
            welder.When(w => w.Enabled = Arg.Any<bool>()).Do(ci => state = ci.Arg<bool>());
            typeof(NanobotSystem).GetField("_Welder", Inst).SetValue(system, welder);
            Mod.NanobotSystems[id] = system;
            _welders.Add(welder);
        }

        private static string Run(params string[] args)
        {
            var r = SystemsCommand.Execute(args);
            return (!r.IsError ? "ok: " : "error: ") + r.Message;
        }

        [Fact]
        public void DisableAll_CountsOnlyBlocksThatChanged()
        {
            var r = Run("systems", "disable", "all");
            Assert.StartsWith("ok: Disabled 3 BaR", r);
            foreach (var w in _welders) Assert.False(w.Enabled);
        }

        [Fact]
        public void Grid_MatchesPartOfTheName_CaseInsensitive_WithSpaces()
        {
            var r = Run("systems", "disable", "--grid", "mining", "SHIP");
            Assert.StartsWith("ok: Disabled 1 BaR", r); // Beta was already off
            Assert.False(_welders[0].Enabled);
            Assert.True(_welders[2].Enabled); // Home Base untouched
        }

        [Fact]
        public void Grid_NoMatch_IsAnError_AndChangesNothing()
        {
            var r = Run("systems", "disable", "--grid", "Nowhere");
            Assert.StartsWith("error: No BaR blocks found on grids matching 'Nowhere'", r);
            Assert.True(_welders[0].Enabled);
        }

        [Fact]
        public void Owner_MatchesOfflinePlayerNames_AndNobody()
        {
            var r = Run("systems", "enable", "--owner", "sead");
            Assert.StartsWith("ok: Enabled 1 BaR", r); // Beta switched on, Alpha already on
            Assert.True(_welders[1].Enabled);
            r = Run("systems", "disable", "--owner", "Nobody");
            Assert.StartsWith("ok: Disabled 1 BaR", r);
            Assert.False(_welders[3].Enabled);
            Assert.True(_welders[2].Enabled);
        }

        [Fact]
        public void MissingOrUnknownTarget_IsAnError()
        {
            Assert.StartsWith("error: Usage", Run("systems", "disable"));
            Assert.StartsWith("error: Usage", Run("systems", "disable", "--grid"));
            Assert.StartsWith("error: Unknown target 'everything'", Run("systems", "disable", "everything"));
            foreach (var w in _welders) Assert.Equal(w.EntityId != 2, w.Enabled);
        }

        [Theory]
        [InlineData("0.5", true, 0.5f)]
        [InlineData("0.1", true, 0.1f)]
        [InlineData("1", true, 1.0f)]
        [InlineData("0.05", false, 0f)]
        [InlineData("1.5", false, 0f)]
        [InlineData("0,5", false, 0f)]
        [InlineData("abc", false, 0f)]
        public void Sim_ParsesInvariant_WithinBounds(string value, bool ok, float expected)
        {
            Mod.SimSpeedOverride = null;
            var r = SimCommand.Execute(new[] { "sim", value });
            Assert.Equal(ok, !r.IsError);
            if (ok) Assert.Equal(expected, Mod.SimSpeedOverride.Value, 3);
            else Assert.Null(Mod.SimSpeedOverride);
        }

        [Fact]
        public void Sim_Reset_RemovesTheOverride()
        {
            Mod.SimSpeedOverride = 0.5f;
            Assert.False(SimCommand.Execute(new[] { "sim", "reset" }).IsError);
            Assert.Null(Mod.SimSpeedOverride);
        }
    }
}

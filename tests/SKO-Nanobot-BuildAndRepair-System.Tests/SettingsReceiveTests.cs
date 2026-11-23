using NSubstitute;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Handlers;
using SKONanobotBuildAndRepairSystem.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using VRage.Game.ModAPI;
using Xunit;
using IMyShipWelder = Sandbox.ModAPI.IMyShipWelder;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-NET-001 — the server side of a client's BaR settings change (NetworkMessagingHandler.MsgBlockSettingsReceived):
    // only players with access (owner / share via HasPlayerAccess, or admins) are applied; a forger is rejected before
    // the rate limit; one change per (block, sender) per 500 ms; two players' changes are both applied; a client ignores
    // settings that do not come from the server. "Applied" = the per-sender timestamp the handler records first.
    public class SettingsReceiveTests : ModTestBase
    {
        private const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;
        private const long BarId = 4242, OwnerIdentity = 77, OtherIdentity = 88, AdminIdentity = 99;
        private const ulong OwnerSteam = 7001, OtherSteam = 7002, AdminSteam = 7003, UnknownSteam = 7009;
        private readonly IMyUtilities _utilities;
        private readonly NanobotSystem _bar;

        public SettingsReceiveTests()
        {
            _bar = new NanobotSystem();
            typeof(NanobotSystem).GetField("_Settings", Inst).SetValue(_bar, new SyncBlockSettings(_bar));
            // The incoming message's settings, built before the welder fake is attached (CheckLimits reads block data).
            var incoming = new SyncBlockSettings(_bar);
            var welder = Substitute.For<IMyShipWelder>();
            welder.EntityId.Returns(BarId);
            welder.OwnerId.Returns(OwnerIdentity);
            welder.HasPlayerAccess(OwnerIdentity).Returns(true);
            welder.HasPlayerAccess(OtherIdentity).Returns(false);
            welder.HasPlayerAccess(AdminIdentity).Returns(false);
            typeof(NanobotSystem).GetField("_Welder", Inst).SetValue(_bar, welder);
            Mod.NanobotSystems[BarId] = _bar;

            var players = new List<IMyPlayer>
            {
                Player(OwnerSteam, OwnerIdentity, VRage.Game.ModAPI.MyPromoteLevel.None),
                Player(OtherSteam, OtherIdentity, VRage.Game.ModAPI.MyPromoteLevel.None),
                Player(AdminSteam, AdminIdentity, VRage.Game.ModAPI.MyPromoteLevel.Admin),
            };
            var playerCollection = Substitute.For<IMyPlayerCollection>();
            playerCollection.When(p => p.GetPlayers(Arg.Any<List<IMyPlayer>>(), Arg.Any<Func<IMyPlayer, bool>>()))
                .Do(ci => ci.Arg<List<IMyPlayer>>().AddRange(players));
            MyAPIGateway.Players = playerCollection;

            _utilities = Substitute.For<IMyUtilities>();
            _utilities.SerializeFromBinary<MsgBlockSettings>(Arg.Any<byte[]>())
                .Returns(_ => new MsgBlockSettings { EntityId = BarId, Settings = incoming });
            MyAPIGateway.Utilities = _utilities;
        }

        private static IMyPlayer Player(ulong steam, long identity, VRage.Game.ModAPI.MyPromoteLevel level)
        {
            var p = Substitute.For<IMyPlayer>();
            p.SteamUserId.Returns(steam);
            p.IdentityId.Returns(identity);
            p.PromoteLevel.Returns(level);
            return p;
        }

        private static void Receive(ulong sender, bool fromServer)
        {
            typeof(NetworkMessagingHandler).GetMethod("MsgBlockSettingsReceived", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { (ushort)40103, new byte[] { 1 }, sender, fromServer });
        }

        private bool Applied(ulong sender, out TimeSpan at)
        {
            var map = (Dictionary<ulong, TimeSpan>)typeof(NanobotSystem).GetField("_lastClientSettingsAppliedBySender", Inst).GetValue(_bar);
            return map.TryGetValue(sender, out at);
        }

        [Fact]
        public void Owner_IsApplied()
        {
            Receive(OwnerSteam, false);
            TimeSpan at;
            Assert.True(Applied(OwnerSteam, out at));
            Assert.Equal(Now, at);
        }

        [Fact]
        public void PlayerWithoutAccess_IsRejected()
        {
            Receive(OtherSteam, false);
            TimeSpan at;
            Assert.False(Applied(OtherSteam, out at));
        }

        [Fact]
        public void UnknownSender_IsRejected()
        {
            Receive(UnknownSteam, false);
            TimeSpan at;
            Assert.False(Applied(UnknownSteam, out at));
        }

        [Fact]
        public void Admin_IsApplied_WithoutBlockAccess()
        {
            Receive(AdminSteam, false);
            TimeSpan at;
            Assert.True(Applied(AdminSteam, out at));
        }

        [Fact]
        public void SameSender_IsRateLimitedTo500Ms()
        {
            Receive(OwnerSteam, false);
            var first = Now;
            Now = first + TimeSpan.FromMilliseconds(300);
            Receive(OwnerSteam, false);
            TimeSpan at;
            Assert.True(Applied(OwnerSteam, out at));
            Assert.Equal(first, at); // dropped
            Now = first + TimeSpan.FromMilliseconds(600);
            Receive(OwnerSteam, false);
            Assert.True(Applied(OwnerSteam, out at));
            Assert.Equal(first + TimeSpan.FromMilliseconds(600), at);
        }

        [Fact]
        public void TwoSenders_AreBothApplied_InTheSameWindow()
        {
            Receive(OwnerSteam, false);
            Now = Now + TimeSpan.FromMilliseconds(100);
            Receive(AdminSteam, false);
            TimeSpan at;
            Assert.True(Applied(OwnerSteam, out at));
            Assert.True(Applied(AdminSteam, out at));
        }

        [Fact]
        public void Forger_DoesNotUseUpTheOwnersWindow()
        {
            Receive(OtherSteam, false); // rejected before the rate limit
            Receive(OwnerSteam, false);
            TimeSpan at;
            Assert.True(Applied(OwnerSteam, out at));
        }

        [Fact]
        public void Client_IgnoresSettingsNotFromTheServer()
        {
            Session.IsServer.Returns(false);
            Receive(OtherSteam, false);
            _utilities.DidNotReceive().SerializeFromBinary<MsgBlockSettings>(Arg.Any<byte[]>());
        }
    }
}

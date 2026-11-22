using NSubstitute;
using Sandbox.Definitions;
using System;
using System.Collections.Generic;
using System.Reflection;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-WELD-002 — BUG-261122.1 lend Components[0] to the block inventory for proj.Build (SE 1.211.018),
    // BUG-261122.2 silent-fail skips expire instead of lasting until a power cycle.
    public class ProjectedBuildComponentTests : ModTestBase
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private const uint TransportItemId = 3;
        private const uint WelderItemId = 9;

        private static readonly MyPhysicalItemDefinition SteelPlate = new MyPhysicalItemDefinition
        {
            Id = new MyDefinitionId(typeof(MyObjectBuilder_Component), "SteelPlate"),
            Volume = 0.003f,
        };

        /// <summary>Inventory fake: holds one item stack, engine add accepts at most <paramref name="accepts"/>.</summary>
        private sealed class Inv
        {
            public readonly IMyInventory Api = Substitute.For<IMyInventory>();
            public MyFixedPoint Amount;

            public Inv(double amount, uint itemId, double accepts = 1000)
            {
                Amount = (MyFixedPoint)amount;
                var item = Substitute.For<IMyInventoryItem>();
                item.ItemId.Returns(itemId);
                item.Amount.Returns(_ => Amount);
                item.Content.Returns(new MyObjectBuilder_Component { SubtypeName = "SteelPlate" });
                Api.MaxVolume.Returns((MyFixedPoint)1000f);
                Api.CurrentVolume.Returns(MyFixedPoint.Zero);
                Api.GetItemAmount(Arg.Any<VRage.Game.ModAPI.Ingame.MyItemType>()).Returns(_ => Amount);
                Api.FindItem(Arg.Any<VRage.ObjectBuilders.SerializableDefinitionId>()).Returns(_ => Amount > 0 ? item : null);
                Api.GetItemByID(itemId).Returns(_ => Amount > 0 ? item : null);
                Api.When(i => i.AddItems(Arg.Any<MyFixedPoint>(), Arg.Any<MyObjectBuilder_PhysicalObject>(), Arg.Any<int>())).Do(ci =>
                {
                    var a = ci.Arg<MyFixedPoint>();
                    var cap = (MyFixedPoint)accepts;
                    Amount += a > cap ? cap : a;
                });
                Api.When(i => i.RemoveItems(itemId, Arg.Any<MyFixedPoint?>(), Arg.Any<bool>(), Arg.Any<bool>())).Do(ci =>
                {
                    Amount -= ci.ArgAt<MyFixedPoint?>(1) ?? Amount;
                });
            }
        }

        private static NanobotSystem NewSystem(Inv transport, bool creative = false)
        {
            var system = new NanobotSystem();
            system._TransportInventory = transport.Api;
            typeof(NanobotSystem).GetField("CreativeModeActive", Any).SetValue(system, creative);
            return system;
        }

        private static bool Lend(NanobotSystem system, IMyInventory welder, out bool lent)
        {
            var args = new object[] { welder, SteelPlate, false };
            var ok = (bool)typeof(NanobotSystem).GetMethod("ServerLendBuildComponent", Any).Invoke(system, args);
            lent = (bool)args[2];
            return ok;
        }

        [Fact]
        public void Lend_MovesOnePieceFromTheTransport_WhenTheBlockInventoryHasNone()
        {
            var transport = new Inv(5, TransportItemId);
            var welder = new Inv(0, WelderItemId);
            bool lent;
            Assert.True(Lend(NewSystem(transport), welder.Api, out lent));
            Assert.True(lent);
            Assert.Equal((MyFixedPoint)1, welder.Amount);
            Assert.Equal((MyFixedPoint)4, transport.Amount);
        }

        [Fact]
        public void Lend_UsesAPieceAlreadyInTheBlockInventory()
        {
            var transport = new Inv(5, TransportItemId);
            var welder = new Inv(2, WelderItemId);
            bool lent;
            Assert.True(Lend(NewSystem(transport), welder.Api, out lent));
            Assert.False(lent);
            Assert.Equal((MyFixedPoint)5, transport.Amount);
        }

        [Fact]
        public void Lend_FullBlockInventory_FailsWithoutLosingThePiece()
        {
            var transport = new Inv(5, TransportItemId);
            var welder = new Inv(0, WelderItemId, accepts: 0);
            bool lent;
            Assert.False(Lend(NewSystem(transport), welder.Api, out lent));
            Assert.False(lent);
            Assert.Equal((MyFixedPoint)5, transport.Amount);
        }

        [Fact]
        public void Lend_Creative_MovesNothing()
        {
            var transport = new Inv(5, TransportItemId);
            var welder = new Inv(0, WelderItemId);
            bool lent;
            Assert.True(Lend(NewSystem(transport, creative: true), welder.Api, out lent));
            Assert.False(lent);
            Assert.Equal(MyFixedPoint.Zero, welder.Amount);
        }

        [Fact]
        public void Return_MovesTheUntakenPieceBackToTheTransport()
        {
            var transport = new Inv(4, TransportItemId);
            var welder = new Inv(1, WelderItemId);
            typeof(NanobotSystem).GetMethod("ServerReturnBuildComponent", Any).Invoke(NewSystem(transport), new object[] { welder.Api, SteelPlate });
            Assert.Equal(MyFixedPoint.Zero, welder.Amount);
            Assert.Equal((MyFixedPoint)5, transport.Amount);
        }

        [Fact]
        public void SilentFailSkip_Expires_NreSkipStays_CountIsPublished()
        {
            var system = NewSystem(new Inv(0, TransportItemId));
            var skipUntil = (Dictionary<string, TimeSpan>)typeof(NanobotSystem).GetField("_ProjBuildSkipUntil", Any).GetValue(system);
            var nreKeys = (HashSet<string>)typeof(NanobotSystem).GetField("_BrokenProjBuildKeys", Any).GetValue(system);
            var isSkipped = typeof(NanobotSystem).GetMethod("IsProjBuildSkipped", Any);
            var refresh = typeof(NanobotSystem).GetMethod("RefreshProjBuildSkips", Any);

            skipUntil["1:silent"] = Now + TimeSpan.FromMinutes(5);
            nreKeys.Add("1:nre");
            refresh.Invoke(system, null);
            Assert.True((bool)isSkipped.Invoke(system, new object[] { "1:silent" }));
            Assert.Equal(2, system.State.ProjectedBlocksSkipped);

            Now = Now + TimeSpan.FromMinutes(6);
            Assert.False((bool)isSkipped.Invoke(system, new object[] { "1:silent" }));
            Assert.True((bool)isSkipped.Invoke(system, new object[] { "1:nre" }));
            refresh.Invoke(system, null);
            Assert.Empty(skipUntil);
            Assert.Equal(1, system.State.ProjectedBlocksSkipped);
        }
    }
}
